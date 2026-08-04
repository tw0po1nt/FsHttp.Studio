module Companion.BlockRunner

// Runs one located `http { }` block against a fresh FCS interactive session, which is
// ADR-0002's mechanism. Each Run gets a fresh session. The Run reaches the block where the user
// wrote it (docs/spec/0002-reach-a-block-anywhere.md): the Setup interaction evaluates the
// script from line 1 through the end of the target block's own expression, with every *other*
// located block's `Blank` span excluded, and the target block itself included and named. A
// second interaction then invokes the target by that name, applies the response-reading guard,
// pipes to `Request.send`, and extracts the raw `Response` by reflection over the BCL
// `HttpContent` type.
//
// This module must never reference FsHttp itself. The user's own `#r "nuget: FsHttp, x.y.z"`,
// evaluated as part of their own setup text, is the only thing that resolves the package, so
// their version pin always wins.

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Interactive.Shell
open Companion.BlockLocator
open Companion.Envelope

type Diagnostic = { Message: string; Range: BlockRange }

type RunOutcome =
    | Ok of status: int * reason: string * headers: (string * string) list * contentType: string * bodyBase64: string
    | CompileError of Diagnostic list
    | RuntimeError of string

/// Companion-side addendum, evaluated after the user's own setup. It silences FsHttp's FSI
/// debug logging, and it forces a read of the whole response body *before* the value that we
/// reflect over returns. It carries no `#r` of its own, because the user's setup is the only
/// source of an FsHttp package reference (ADR-0002).
///
/// `httpCompletionOption = ResponseContentRead` is load-bearing, not a nicety. FsHttp defaults
/// to `ResponseHeadersRead`. With that default, `Request.send` returns as soon as the headers
/// arrive, and the body stays a *read-once* `HttpConnectionResponseContent` bound to the live
/// keep-alive socket.
///
/// FsHttp's own `bufferResponseContent = true` must drain that stream into a replayable buffer.
/// But FsHttp reuses one process-wide static `HttpClient` across every Run's fresh FSI session.
/// A Run that reuses a pooled keep-alive connection can therefore receive a content whose
/// stream is already consumed. `ReadAsByteArrayAsync` (`extractResponse`) then throws "The
/// stream was already consumed. It cannot be read again." A server that streams the body a
/// moment after its headers is the reliable trigger.
///
/// `ResponseContentRead` makes the BCL read the whole body into memory as part of the send,
/// independent of any later connection state, so every Run reads it cleanly.
/// `bufferResponseContent` stays on as a second guard.
let private companionAddendum =
    [ "open FsHttp"
      "FsHttp.Fsi.disableDebugLogs()"
      "GlobalConfig.set (GlobalConfig.defaults |> Config.update (fun c -> { c with bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead }))" ]
    |> String.concat "\n"

let private asPairs (h: IEnumerable<KeyValuePair<string, IEnumerable<string>>>) =
    h |> Seq.map (fun kv -> kv.Key, String.Join(", ", kv.Value))

/// Blanks a block's `Blank` span in place across `lines`. It keeps every newline, so every other
/// line's row and column numbers stay aligned with the original source. The `()` placeholder
/// keeps a `let`-bound declaration well-formed, and does not evaluate the request that it
/// displaces. The span reaches past the CE itself, so a trailing `|> Request.send` on the
/// excluded block's own line has nothing left to pipe from.
let private blankSpan (lines: string[]) (r: BlockRange) =
    let startIdx = r.StartLine - 1
    let endIdx = r.EndLine - 1

    if startIdx = endIdx then
        let line = lines.[startIdx]
        let before = line.Substring(0, r.StartCol)
        let spanLen = r.EndCol - r.StartCol
        let placeholder = "()" + String(' ', max 0 (spanLen - 2))
        let after = line.Substring(r.EndCol)
        lines.[startIdx] <- before + placeholder + after
    else
        let firstLine = lines.[startIdx]
        let before = firstLine.Substring(0, r.StartCol)
        let spanLen = firstLine.Length - r.StartCol
        let placeholder = "()" + String(' ', max 0 (spanLen - 2))
        lines.[startIdx] <- before + placeholder

        for i in startIdx + 1 .. endIdx - 1 do
            lines.[i] <- String(' ', lines.[i].Length)

        let lastLine = lines.[endIdx]
        lines.[endIdx] <- String(' ', r.EndCol) + lastLine.Substring(r.EndCol)

/// The R1 route names nothing, so the Run invents a name. Backtick-quoted so that no legal user
/// identifier can ever collide with it by accident (ADR-0007 records the deliberate one: a user
/// binding of the same backtick-quoted name in the same scope still collides, and the Run does
/// not avoid it).
[<Literal>]
let private reservedTargetName = "__fsHttpStudio_target"

/// Inserted at the block's own start column, on the block's own line (Decision 2's R1 rule).
/// Its length is the column residue that `unshiftPos` and `shiftForward` carry as `offset`
/// (Decision 9) — measured at 32 characters for this exact text.
let private r1InsertText = sprintf "let ``%s`` = " reservedTargetName

/// The invocation's own name, unqualified: what the R1 route inserted, or what the R2 route's
/// binding already offers. `Refused` never reaches this — `runInProcessDirect` returns before
/// building any text for it.
let private baseInvocation (route: Route) : string =
    match route with
    | NamedByTheRun -> sprintf "``%s``" reservedTargetName
    | NamedByTheBinding invocation -> invocation
    | Refused _ -> invalidArg "route" "a refused route builds no invocation"

/// Prefixes the invocation's own name with the enclosing-module qualifier (outermost first),
/// and leaves a trailing arity suffix — `getSnorlax ()`'s `" ()"` — after the qualified name,
/// not before it: `Outer.getSnorlax ()`, not `Outer.getSnorlax()`.
let private qualifyInvocation (qualifier: string list) (invocation: string) : string =
    match invocation.IndexOf ' ' with
    | -1 -> (qualifier @ [ invocation ]) |> String.concat "."
    | i ->
        let name = invocation.Substring(0, i)
        let rest = invocation.Substring i
        ((qualifier @ [ name ]) |> String.concat ".") + rest

/// The R1 column shift, as `(line, insertCol, offset)` (Decision 9). `None` on the R2 route,
/// which names nothing and inserts no text.
type private ColumnShift = (int * int * int) option

let private shiftFor (target: LocatedBlock) : ColumnShift =
    match target.Route with
    | NamedByTheRun -> Some(target.Block.StartLine, target.Block.StartCol, r1InsertText.Length)
    | _ -> None

/// Moves an *original*-source column forward across the R1 insertion, so a boundary computed in
/// source coordinates (the block's own end column, for the truncation point) lands on the same
/// character in the edited Setup text. Identity off the shifted line, and left of the insertion.
let private shiftForward (shift: ColumnShift) (line: int, col: int) =
    match shift with
    | Some(shiftLine, insertCol, offset) when line = shiftLine && col >= insertCol -> line, col + offset
    | _ -> line, col

/// Moves a Setup-interaction-coordinate column back to the original source (Decision 9). A
/// column before the insertion point is untouched. A column inside the inserted text itself has
/// no original counterpart, and clamps to the insertion point. A column at or past the inserted
/// text's end is the block's own text, shifted forward by `offset`, so it subtracts back out.
let private unshiftPos (shift: ColumnShift) (line: int, col: int) =
    match shift with
    | Some(shiftLine, insertCol, offset) when line = shiftLine ->
        if col < insertCol then line, col
        elif col < insertCol + offset then line, insertCol
        else line, col - offset
    | _ -> line, col

/// Builds the Setup text (Decision 1): everything from line 1 through the end of the target
/// block's own expression, truncated at its end column, with every *other* located block's
/// `Blank` span replaced first. A click on the target therefore fires exactly one request, which
/// is the isolation criterion, and the code after the target never runs at all — it is not part
/// of either FSI interaction, not even blanked.
///
/// The R1 route inserts `let <name> = ` at the block's own start (Decision 2) before the
/// truncation point is computed, because that insertion can land on the same line the boundary
/// truncates.
let private buildSetupText (source: string) (blocks: LocatedBlock list) (target: LocatedBlock) : string * ColumnShift =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    blocks
    |> List.filter (fun b -> b.Block <> target.Block)
    |> List.iter (fun b -> blankSpan lines b.Blank)

    let shift = shiftFor target

    match shift with
    | Some(insertLine, insertCol, _) ->
        let idx = insertLine - 1
        let line = lines.[idx]
        lines.[idx] <- line.Substring(0, insertCol) + r1InsertText + line.Substring(insertCol)
    | None -> ()

    let cutLine, cutCol = shiftForward shift (target.Block.EndLine, target.Block.EndCol)
    let cutIdx = cutLine - 1

    let prefixLines = lines.[0 .. cutIdx - 1]
    let lastLineText = lines.[cutIdx].Substring(0, cutCol)

    Array.append prefixLines [| lastLineText |] |> String.concat "\n", shift

/// The second interaction: invokes the target by its qualified name, and applies the
/// response-reading guard to its value before sending (Decision 10). The Setup builds the
/// block's context *inside* itself, and thus before the companion addendum's `GlobalConfig.set`
/// runs, so the context would otherwise still carry FsHttp's `ResponseHeadersRead` default and
/// leave the body a read-once stream. `Config.update` re-applies the guard on the built value,
/// which is idempotent with the addendum's own guard.
let private invocationText (target: LocatedBlock) : string =
    let qualified = baseInvocation target.Route |> qualifyInvocation target.Qualifier

    sprintf
        "%s |> Config.update (fun c -> { c with bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead }) |> Request.send"
        qualified

let private errorDiagnostics (diags: FSharpDiagnostic[]) =
    diags |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

/// True when `point` (already unshifted to original-source coordinates) falls inside `block`'s
/// own span: at or after its start, and strictly before its end.
let private withinBlock (block: BlockRange) (line: int, col: int) =
    let atOrAfterStart =
        line > block.StartLine || (line = block.StartLine && col >= block.StartCol)

    let beforeEnd = line < block.EndLine || (line = block.EndLine && col < block.EndCol)
    atOrAfterStart && beforeEnd

/// Maps a diagnostic from the combined Setup evaluation back onto the original source, and
/// names the Setup in its message. Every position first passes through `unshiftPos`, which is
/// the identity off the R1 insertion line and off the R2 route (`shift = None`).
///
/// The first `realLineCount` lines *are* the original text, minus the other blocks that the
/// setup blanked. A diagnostic inside those lines is already native and needs no translation.
///
/// A diagnostic past those lines comes from the appended `companionAddendum`, or from the
/// invocation interaction (`realLineCount = 0` there — see `runInProcessDirect`), and has no
/// source counterpart. One example is a failed `open FsHttp`, because the user's script carries
/// no resolvable `#r`. Anchor such a diagnostic at the top of the script, where the missing
/// reference belongs. A phantom line past the end would fail to highlight in the UI.
///
/// *Every* diagnostic from here keeps its compiler text verbatim behind a `Setup failed to
/// evaluate:` prefix, not only an anchored one. See
/// `docs/spec/0001-report-setup-compile-error.md`, Decision 3.
let private setupDiagnostic (realLineCount: int) (shift: ColumnShift) (d: FSharpDiagnostic) : Diagnostic =
    let message = sprintf "Setup failed to evaluate: %s" d.Message
    let sl, sc = unshiftPos shift (d.StartLine, d.StartColumn)

    if sl > realLineCount then
        { Message = message
          Range =
            { StartLine = 1
              StartCol = 0
              EndLine = 1
              EndCol = 0 } }
    else
        let el, ec = unshiftPos shift (d.EndLine, d.EndColumn)

        { Message = message
          Range =
            { StartLine = sl
              StartCol = sc
              EndLine = el
              EndCol = ec } }

/// A diagnostic that starts inside the target block's own span keeps the compiler's text
/// unchanged, at its own (unshifted) position (Decision 8) — no introductory sentence, because
/// the fault is in the user's block, not in text the companion generated.
let private blockDiagnostic (shift: ColumnShift) (d: FSharpDiagnostic) : Diagnostic =
    let sl, sc = unshiftPos shift (d.StartLine, d.StartColumn)
    let el, ec = unshiftPos shift (d.EndLine, d.EndColumn)

    { Message = d.Message
      Range =
        { StartLine = sl
          StartCol = sc
          EndLine = el
          EndCol = ec } }

/// Splits a Setup-interaction diagnostic between the two treatments above, by whether its
/// (unshifted) start position lands inside the target's own block span (Decision 8).
let private splitDiagnostic
    (shift: ColumnShift)
    (blockRange: BlockRange)
    (realLineCount: int)
    (d: FSharpDiagnostic)
    : Diagnostic =
    let start = unshiftPos shift (d.StartLine, d.StartColumn)

    if withinBlock blockRange start then
        blockDiagnostic shift d
    else
        setupDiagnostic realLineCount shift d

/// `PropertyInfo.GetProperty` is nullable-annotated, because the name can be absent. The fields
/// that this function reads are FsHttp's `Response` record shape, which is stable across the
/// FsHttp versions that we target. A missing property is therefore a real extraction bug, and
/// not a case to recover from. The response body itself comes from the BCL `HttpContent` type,
/// which ADR-0002 commits to as version-independent.
let private prop (name: string) (t: Type) : Reflection.PropertyInfo =
    match t.GetProperty name with
    | null -> failwithf "reflection: property '%s' not found on %s" name t.FullName
    | p -> p

let private extractResponse (v: FsiValue) : RunOutcome =
    let t = v.ReflectionType
    let rv = v.ReflectionValue
    let getValue name = (prop name t).GetValue(rv)

    let statusInt = int (getValue "statusCode" :?> Net.HttpStatusCode)
    let reason = string (getValue "reasonPhrase")

    let content =
        match getValue "content" with
        | :? HttpContent as c -> c
        | _ -> failwith "reflection: 'content' property was not an HttpContent"

    let respHeaders =
        match getValue "headers" with
        | :? Net.Http.Headers.HttpResponseHeaders as h -> h
        | _ -> failwith "reflection: 'headers' property was not HttpResponseHeaders"

    let bytes = content.ReadAsByteArrayAsync().Result

    let ctype =
        match content.Headers.ContentType with
        | null -> ""
        | c -> string c

    let headers =
        Seq.append (asPairs respHeaders) (asPairs content.Headers)
        |> Seq.distinctBy fst
        |> Seq.toList

    Ok(statusInt, reason, headers, ctype, Convert.ToBase64String bytes)

/// Evaluates the block at `blockIndex` in `source` *in the current process*, and returns its
/// outcome. The index is 0-based and in source order, which matches the order in a `locate` and
/// `blocks` envelope. Each call creates and disposes a fresh `FsiEvaluationSession`, which
/// gives one fresh session per Run.
///
/// This is the warm fast path. `run` calls it directly when the target's `#r "nuget:"` pins do
/// not conflict with a version already loaded into this process. The `--worker` entry point
/// also calls it in a throwaway child process, to serve a conflicting pin against a clean ALC.
let runInProcessDirect (source: string) (blockIndex: int) : RunOutcome =
    let located = locateBlocks source

    match List.tryItem blockIndex located with
    | None -> RuntimeError(sprintf "block index %d out of range (%d blocks located)" blockIndex located.Length)
    // A refused target is never evaluated at all (Decision 11) — not the Setup, not the
    // companion addendum, nothing. The refusal-lens spec (#97) replaces this outcome; until it
    // lands, a plain, readable sentence is the interim contract.
    | Some { Route = Refused(_, reason) } ->
        RuntimeError(sprintf "FsHttp.Studio cannot run a block in this position: %s." reason)
    | Some target ->
        let setupText, shift = buildSetupText source located target
        let combinedSetup = setupText + "\n" + companionAddendum

        // Lines 1 to setupLineCount of `combinedSetup` are native source (the target's own
        // block among them — Decision 1). Anything the addendum reports past them has no source
        // position (see `setupDiagnostic`).
        let setupLineCount = setupText.Split('\n').Length

        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
        let args = [| "fsi.exe"; "--noninteractive"; "--nologo" |]
        use inReader = new IO.StringReader("")

        // The session is collectible. The companion is long-lived and creates one session per
        // Run, so the runtime must reclaim each session's own dynamically-compiled user-code
        // assembly after disposal, instead of an accumulation for the life of the process.
        //
        // NOTE: `collectible` isolates only the per-session dynamic assembly. It does not
        // isolate the package assemblies that `#r "nuget:"` resolves, which load into the
        // process-wide default AssemblyLoadContext and outlive the session. Two in-process Runs
        // that pin *different* versions of the same package would collide there ("Could not
        // load type … from assembly …"). `run` prevents that, because it never enters this path
        // for a conflicting pin. It routes such a Run to a throwaway `--worker` child process
        // whose ALC ends with it. The target's pins are therefore safe to load in-process here.
        use session =
            FsiEvaluationSession.Create(fsiConfig, args, inReader, Console.Error, Console.Error, collectible = true)

        let setupResult, setupDiags = session.EvalInteractionNonThrowing(combinedSetup)

        // `Choice1Of2` means only that the Setup threw no exception; it does not mean the
        // Setup has no errors. FSI can return `Choice1Of2` for a Setup that fails to parse or
        // type-check, and discard the failure into the diagnostics array instead of the
        // `Choice`. The array is therefore the only reliable signal, and it is read *before*
        // the `Choice`, independent of which branch the `Choice` took. A Setup failure also
        // stops the Run: evaluating the block against a Setup that never took effect only
        // produces a second, misleading error. See
        // `docs/spec/0001-report-setup-compile-error.md`, Decisions 1 and 2.
        //
        // The target block's own text is now *inside* this interaction (Decision 1), so its
        // diagnostics arrive here too. `splitDiagnostic` tells a fault in the user's block from
        // a fault in the Setup around it (Decision 8), by whether the diagnostic's start
        // position — unshifted back past the R1 insertion, if any — lands inside the block's own
        // span.
        match
            errorDiagnostics setupDiags
            |> Array.map (splitDiagnostic shift target.Block setupLineCount)
            |> Array.toList
        with
        | [] ->
            match setupResult with
            | Choice2Of2 ex -> RuntimeError ex.Message
            | Choice1Of2 _ ->
                let targetResult, targetDiags =
                    session.EvalExpressionNonThrowing(invocationText target)

                match targetResult with
                | Choice2Of2 ex ->
                    // The invocation is the companion's own generated text, with no user-source
                    // position of its own, so every diagnostic here gets the Setup treatment,
                    // anchored at the top of the script (`realLineCount = 0` forces the anchor
                    // on every line). `targetDiagnostic` is gone: there is no longer a separate
                    // block interaction to map a native position back from.
                    match
                        errorDiagnostics targetDiags
                        |> Array.map (setupDiagnostic 0 None)
                        |> Array.toList
                    with
                    | [] -> RuntimeError ex.Message
                    | errors -> CompileError errors
                | Choice1Of2 None -> RuntimeError "expression returned no value"
                | Choice1Of2(Some v) ->
                    try
                        extractResponse v
                    with ex ->
                        RuntimeError ex.Message
        | errors -> CompileError errors

// ---------------------------------------------------------------------------------------------
// Multi-version isolation. The assemblies that `#r "nuget:"` resolves load into the
// process-wide default AssemblyLoadContext, and they outlive each per-Run FSI session. A Run
// that pins a version of a package collides when an earlier in-process Run already loaded that
// package at a *different* version. The fix keeps the warm in-process fast path
// (`runInProcessDirect`). Only when a pin conflicts does it delegate that one Run to a
// short-lived `--worker` child process, which is a fresh process with a fresh ALC that ends
// with it. `run` is the router, and everything below serves it.
// ---------------------------------------------------------------------------------------------

/// Serializes a `RunOutcome` to the same tagged wire shape that the host-facing `run`, `ok`,
/// `compileError`, and `runtimeError` envelope uses. The `--worker` child emits its outcome
/// over this shape, and `RequestHandler` emits the host's response over it. The two channels
/// therefore cannot drift apart.
let outcomeToWire (outcome: RunOutcome) : obj =
    match outcome with
    | Ok(status, reason, headers, contentType, bodyBase64) ->
        {| tag = "ok"
           status = status
           reason = reason
           headers = dict headers
           contentType = contentType
           bodyBase64 = bodyBase64 |}
    | CompileError diagnostics ->
        {| tag = "compileError"
           diagnostics =
            diagnostics
            |> List.map (fun d ->
                {| message = d.Message
                   range =
                    {| startLine = d.Range.StartLine
                       startCol = d.Range.StartCol
                       endLine = d.Range.EndLine
                       endCol = d.Range.EndCol |} |}) |}
    | RuntimeError message ->
        {| tag = "runtimeError"
           message = message |}

/// Parses a `--worker` child's response frame back into a `RunOutcome`. It is the inverse of
/// `outcomeToWire`, so the delegation is transparent to `run`'s caller.
let private wireToOutcome (root: JsonElement) : RunOutcome =
    match jsonString (root.GetProperty "tag") with
    | "ok" ->
        let headers =
            [ for p in root.GetProperty("headers").EnumerateObject() -> p.Name, jsonString p.Value ]

        Ok(
            root.GetProperty("status").GetInt32(),
            jsonString (root.GetProperty "reason"),
            headers,
            jsonString (root.GetProperty "contentType"),
            jsonString (root.GetProperty "bodyBase64")
        )
    | "compileError" ->
        [ for d in root.GetProperty("diagnostics").EnumerateArray() do
              let r = d.GetProperty "range"

              { Message = jsonString (d.GetProperty "message")
                Range =
                  { StartLine = r.GetProperty("startLine").GetInt32()
                    StartCol = r.GetProperty("startCol").GetInt32()
                    EndLine = r.GetProperty("endLine").GetInt32()
                    EndCol = r.GetProperty("endCol").GetInt32() } } ]
        |> CompileError
    | _ -> RuntimeError(jsonString (root.GetProperty "message"))

/// Matches a `#r "nuget: Package[, Version]"` directive. It captures the package id, and the
/// version when the directive pins one.
let private nugetPinRegex =
    Regex("""#r\s+"nuget:\s*(?<pkg>[^,"\s]+)\s*(?:,\s*(?<ver>[^",\s]+))?""", RegexOptions.Compiled)

/// Extracts the `#r "nuget: Package[, Version]"` pins in a script as `(package, version option)`
/// pairs, in source order. A version-less `#r` yields `None` (see `nugetPinRegex`). This is
/// public for the direct pin-parsing tests, and `run` consumes it for conflict routing.
let extractPins (source: string) : (string * string option) list =
    [ for m in nugetPinRegex.Matches source do
          let ver = m.Groups.["ver"]

          m.Groups.["pkg"].Value,
          (if ver.Success && ver.Value <> "" then
               Some ver.Value
           else
               None) ]

/// What a package resolved to when it loaded into this process's default ALC. `Pinned v` is an
/// explicit `#r "nuget: pkg, v"`. `Versionless` is a `#r "nuget: pkg"` that resolved *some*
/// latest version that we cannot name. The version-less case is load-bearing, and the map
/// records it instead of nothing. It still poisons the ALC, so a later Run that pins a
/// *different* version would collide with it in-process (ADR-0006). This is public for the
/// routing unit tests.
type LoadedVersion =
    | Pinned of string
    | Versionless

/// Pure routing decision for one pin against the state that a package is already loaded in.
/// `None` means that this process has not loaded the package yet. The rule is "route to a
/// worker unless we can *prove* that the requested load matches what the ALC already holds":
///
/// - A version-less pin against a version-less load is the same latest version, because nuget
///   resolves `#r "nuget: pkg"` to one version per process. It is therefore safe in-process.
/// - Two explicit pins conflict exactly when they name different versions.
/// - Every *mixed* pair conflicts. A version-less load with a later explicit pin, and an
///   explicit load with a later version-less pin, both route to a worker. The version-less side
///   can resolve a different latest version than the named one, and we cannot prove otherwise.
///
/// This rule is deliberately conservative. A false conflict costs one cold worker Run. A false
/// match reopens the original "Could not load type … from assembly …" ALC collision.
/// Correctness wins.
let pinConflicts (loaded: LoadedVersion option) (pin: string option) : bool =
    match loaded, pin with
    | None, _ -> false
    | Some Versionless, None -> false
    | Some(Pinned l), Some v -> l <> v
    | Some(Pinned _), None
    | Some Versionless, Some _ -> true

// The package ids resolved into this process's default ALC so far, as id -> what loaded it.
// Nuget ids are case-insensitive. A lock guards the map. The companion's request loop is serial
// today, but the state is process-global and cheap to make safe for a future concurrent caller.
let private loadLock = obj ()

let private loadedVersions =
    Dictionary<string, LoadedVersion>(StringComparer.OrdinalIgnoreCase)

/// Where `run` sends one Run: to the warm in-process session, or to a fresh `--worker` child.
type private RunRoute =
    | InProcess
    | Worker

/// Routes one Run's `pins`. On the in-process path it also reserves them in the load map. The
/// conflict *check* and the reservation *act* run under a single `lock loadLock`, so the two
/// are one atomic step. The lock is taken once for each logical operation, not once for each
/// access. A check in one lock scope, followed by a mark in another scope, leaves a TOCTOU gap.
/// A future concurrent caller could load a conflicting version in that gap (coding-standards
/// rule 4). The request loop is serial today, and the lock exists to stay correct when it is not.
///
/// The reservation happens *before* the evaluation runs, not after a successful load, and this
/// is deliberate. The map is a conservative over-approximation of what the shared ALC can hold.
/// A Run that reaches the in-process path can resolve its `#r "nuget:"` into that ALC, and the
/// resolved assembly then outlives the session even when the evaluation compile-errors or
/// throws. An over-mark of a Run that never loaded only over-routes a *later* Run to a safe,
/// cold worker. An under-mark of a Run that did load reopens the "Could not load type … from
/// assembly …" ALC collision (ADR-0006). The two errors are not symmetric, so we mark up front.
let private routeAndReserve (pins: (string * string option) list) : RunRoute =
    lock loadLock (fun () ->
        let conflicts =
            pins
            |> List.exists (fun (pkg, ver) ->
                let loaded =
                    match loadedVersions.TryGetValue pkg with
                    | true, v -> Some v
                    | false, _ -> None

                pinConflicts loaded ver)

        if conflicts then
            Worker
        else
            // The map records a version-less `#r` as `Versionless`, and does *not* skip it, so
            // it still takes part in a later Run's conflict detection.
            for pkg, ver in pins do
                loadedVersions.[pkg] <-
                    match ver with
                    | Some v -> Pinned v
                    | None -> Versionless

            InProcess)

/// The bound on the time that a `--worker` child can take to produce its response frame. After
/// this time the Run terminates by force (coding-standards rule 3: every external process gets
/// a bounded wait and a kill path). The bound is long enough to absorb a cold first-run
/// `#r "nuget:"` restore of a newly pinned version. It is short enough that a stalled worker
/// cannot hang the Run indefinitely. A user block that loops forever, or a request to a server
/// that never answers, both stall a worker. This is public so that tests can drive the hung
/// path on a short bound.
let workerTimeoutMs = 120_000

/// Runs one block in a throwaway child process (`dotnet Companion.dll --worker`), so that its
/// `#r "nuget:"` assemblies load into a fresh default ALC. The runtime reclaims that ALC when
/// the process exits, which avoids the process-global collision. The child reads one framed
/// `{ source, blockIndex }` request, writes one outcome envelope, and then exits.
///
/// `timeoutMs` bounds the wait. `Kill()` terminates a worker that does not produce its frame in
/// time, and also a worker that produces the frame and then stalls before it exits. The Run
/// then maps to a `RuntimeError`, instead of a block of the caller forever. A user block that
/// loops forever, or a request that never answers, both cause the first case. `use proc = proc`
/// disposes the handle but does not unblock a wait. The bound and the kill, not the disposal,
/// are what guarantee that the Run always terminates.
let runInWorker (timeoutMs: int) (source: string) (blockIndex: int) : RunOutcome =
    let companionDll = typeof<RunOutcome>.Assembly.Location

    let psi = ProcessStartInfo(FileName = "dotnet")
    psi.ArgumentList.Add companionDll
    psi.ArgumentList.Add "--worker"
    psi.RedirectStandardInput <- true
    psi.RedirectStandardOutput <- true
    psi.UseShellExecute <- false

    try
        match Process.Start psi with
        | null -> RuntimeError "worker: failed to start evaluation process"
        | proc ->
            use proc = proc

            // Best-effort teardown. A worker in the middle of a restore can spawn child
            // `dotnet` processes, so take the whole tree. Never throw out of a kill, because
            // the process can be gone already.
            let kill () =
                try
                    if not proc.HasExited then
                        proc.Kill(entireProcessTree = true)
                with _ ->
                    ()

            let request: obj =
                {| source = source
                   blockIndex = blockIndex |}

            writeFrame proc.StandardInput.BaseStream (JsonSerializer.SerializeToUtf8Bytes request)
            proc.StandardInput.Close()

            // The frame read blocks with no native timeout, so cap it on a worker thread. A
            // *crashed* child closes stdout without a frame -> None -> a clean runtimeError. A
            // *hung* child produces nothing at all, so the read never returns. `Wait timeoutMs`
            // caps that case, and we Kill() on expiry instead of a block here forever. The kill
            // closes the pipe, so the read task then completes as None and releases its thread.
            // The child's stderr (FCS and user output) inherits ours, so no drain is necessary.
            let readFrame = Task.Run(fun () -> tryReadFrame proc.StandardOutput.BaseStream)

            if not (readFrame.Wait timeoutMs) then
                kill ()
                RuntimeError(sprintf "worker: no response within %dms. Evaluation process terminated." timeoutMs)
            else
                let outcome =
                    match readFrame.Result with
                    | Some payload ->
                        use doc = JsonDocument.Parse(payload: byte[])
                        wireToOutcome doc.RootElement
                    | None -> RuntimeError "worker: evaluation process produced no response"

                // The frame is in hand, so the child must now exit on its own. Bound that wait
                // too, because a worker that emitted its frame and then stalled must not block
                // `WaitForExit`. Kill() the child if it stays past the bound.
                if not (proc.WaitForExit timeoutMs) then
                    kill ()

                outcome
    with ex ->
        RuntimeError(sprintf "worker: %s" ex.Message)

/// Runs the located block at `blockIndex` (0-based, source order) and returns its outcome. It
/// routes on one condition: whether the target's `#r "nuget:"` pins conflict with a version
/// already loaded in this process. No conflict -> the warm in-process session. A conflict -> a
/// fresh `--worker` child, whose ALC cannot collide.
let run (source: string) (blockIndex: int) : RunOutcome =
    match routeAndReserve (extractPins source) with
    | Worker -> runInWorker workerTimeoutMs source blockIndex
    | InProcess -> runInProcessDirect source blockIndex
