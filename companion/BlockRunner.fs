module Companion.BlockRunner

// Runs one located `http { }` block against a fresh FCS interactive session (ADR-0002's
// mechanism): fresh session per Run, evaluate the target's preceding
// setup (opens/#r/lets/helpers) with every *other* located block excluded, then the target's
// bare CE alone piped to `Request.send`, extracting the raw `Response` by reflection over the
// BCL `HttpContent` type. This module must never reference FsHttp itself — the user's own
// `#r "nuget: FsHttp, x.y.z"`, evaluated as part of their own setup text, is the only thing
// that ever resolves the package, so their version pin always wins.

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open System.Collections.Generic
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Interactive.Shell
open Companion.BlockLocator
open Companion.Envelope

type Diagnostic = { Message: string; Range: BlockRange }

type RunOutcome =
    | Ok of status: int * reason: string * headers: (string * string) list * contentType: string * bodyBase64: string
    | CompileError of Diagnostic list
    | RuntimeError of string

/// Companion-side addendum evaluated after the user's own setup: silences FsHttp's FSI debug
/// logging and forces the whole response body to be read *before* the value we reflect over is
/// returned. Carries no `#r` of its own — the user's setup is the only source of a FsHttp
/// package reference (ADR-0002).
///
/// `httpCompletionOption = ResponseContentRead` is load-bearing, not a nicety. FsHttp defaults
/// to `ResponseHeadersRead`: `Request.send` returns as soon as the headers land and the body
/// stays a *read-once* `HttpConnectionResponseContent` bound to the live (keep-alive) socket.
/// FsHttp's own `bufferResponseContent = true` is supposed to drain that stream into a replayable
/// buffer, but because FsHttp reuses a single process-wide static `HttpClient` across every
/// Run's fresh FSI session, a Run that reuses a pooled keep-alive connection can hand us a
/// content whose stream is already consumed — and our `ReadAsByteArrayAsync` (`extractResponse`)
/// then throws "The stream was already consumed. It cannot be read again." (the reliable
/// trigger is a server that streams the body a beat after its headers). `ResponseContentRead`
/// makes the BCL read the whole body into memory as part of the send, independent of any later
/// connection state, so every Run reads it cleanly. `bufferResponseContent` is left on as belt
/// and braces.
let private companionAddendum =
    [ "open FsHttp"
      "FsHttp.Fsi.disableDebugLogs()"
      "GlobalConfig.set (GlobalConfig.defaults |> Config.update (fun c -> { c with bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead }))" ]
    |> String.concat "\n"

let private asPairs (h: IEnumerable<KeyValuePair<string, IEnumerable<string>>>) =
    h |> Seq.map (fun kv -> kv.Key, String.Join(", ", kv.Value))

/// Blanks a statement's span in place across `lines`, preserving every newline so every other
/// line's row/column numbers stay aligned with the original source. The `()` placeholder
/// keeps a `let`-bound statement well-formed without evaluating (and so without firing) the
/// request it displaces; blanking the *whole* statement (not just the CE) means a trailing
/// `|> Request.send` on the excluded block's own line has nothing left to dangle onto.
let private blankStatement (lines: string[]) (r: BlockRange) =
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

/// Builds the target's preceding setup: everything before the target block, with every
/// *other* located block's whole enclosing statement blanked out so clicking the target fires
/// exactly that one request (the setup-isolation criterion). Blanking preserves line
/// count and untouched columns, so a compile error's range still lands on the real source
/// position.
///
/// Known boundary of this per-block isolation model: blanking a *let-bound* block replaces its
/// binding with `()`, so if the target (or later setup) references the value that block
/// produced — e.g. `let auth = http { ... } |> Request.send` consumed by a downstream block —
/// evaluation fails with an "undefined identifier" compileError. Each block is treated as
/// independent by design; cross-block value reuse is out of scope here and would need a
/// different isolation strategy (tracked for whoever revisits block dependencies).
let private buildSetup (source: string) (blocks: LocatedBlock list) (target: LocatedBlock) : string =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    blocks
    |> List.filter (fun b -> b.Block <> target.Block)
    |> List.iter (fun b -> blankStatement lines b.Statement)

    let prefixLines = lines.[0 .. target.Block.StartLine - 2]

    let startLineText =
        lines.[target.Block.StartLine - 1].Substring(0, target.Block.StartCol)

    Array.append prefixLines [| startLineText |] |> String.concat "\n"

let private errorDiagnostics (diags: FSharpDiagnostic[]) =
    diags |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

/// Maps a diagnostic from the combined setup eval back onto the original source. Its first
/// `realLineCount` lines *are* the original text (minus blanked-out other blocks), so a
/// diagnostic within them is already native and needs no translation. A diagnostic beyond them
/// originates in the appended `companionAddendum` (e.g. `open FsHttp` failing because the
/// user's script carries no resolvable `#r`) and has no source counterpart, so anchor it at the
/// top of the script — where the missing reference belongs — rather than a phantom line the UI
/// would try, and fail, to highlight.
let private setupDiagnostic (realLineCount: int) (d: FSharpDiagnostic) : Diagnostic =
    if d.StartLine > realLineCount then
        { Message = d.Message
          Range =
            { StartLine = 1
              StartCol = 0
              EndLine = 1
              EndCol = 0 } }
    else
        { Message = d.Message
          Range =
            { StartLine = d.StartLine
              StartCol = d.StartColumn
              EndLine = d.EndLine
              EndCol = d.EndColumn } }

/// Maps a diagnostic from the isolated target snippet (`<CE>\n|> Request.send`) back onto the
/// original source. The snippet's lines 1..N are the CE's own lines verbatim, so only line 1's
/// column needs the target's start-column offset; the synthetic `|> Request.send` line beyond
/// the CE has no original counterpart and clamps to the block's end.
let private targetDiagnostic (target: BlockRange) (ceLineCount: int) (d: FSharpDiagnostic) : Diagnostic =
    let mapPoint line col =
        if line > ceLineCount then
            target.EndLine, target.EndCol
        else
            let origLine = target.StartLine + line - 1
            let origCol = if line = 1 then target.StartCol + col else col
            origLine, origCol

    let sl, sc = mapPoint d.StartLine d.StartColumn
    let el, ec = mapPoint d.EndLine d.EndColumn

    { Message = d.Message
      Range =
        { StartLine = sl
          StartCol = sc
          EndLine = el
          EndCol = ec } }

/// `PropertyInfo.GetProperty` is nullable-annotated (the name might not exist); every field
/// this reads is one ADR-0002 already commits to as stable across FsHttp 13-15, so a missing
/// property is a genuine extraction bug, not a case to recover from.
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

/// Evaluates the `blockIndex`-th block located in `source` (0-based, in source order — matching a
/// `locate`/`blocks` envelope's ordering) *in the current process* and returns its outcome. A
/// fresh `FsiEvaluationSession` is created and disposed per call — one fresh session per Run.
///
/// This is the warm fast path. `run` calls it directly when the target's `#r "nuget:"` pins don't
/// conflict with a version already loaded into this process, and the `--worker` entry point calls
/// it in a throwaway child process to serve a conflicting pin against a clean ALC.
let runInProcessDirect (source: string) (blockIndex: int) : RunOutcome =
    let located = locateBlocks source

    match List.tryItem blockIndex located with
    | None -> RuntimeError(sprintf "block index %d out of range (%d blocks located)" blockIndex located.Length)
    | Some target ->
        let ceText = sliceRange source target.Block
        let setupText = buildSetup source located target
        let combinedSetup = setupText + "\n" + companionAddendum

        // Lines 1..setupLineCount of `combinedSetup` are native source; anything the addendum
        // reports past them has no source position (see `setupDiagnostic`).
        let setupLineCount = setupText.Split('\n').Length

        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
        let args = [| "fsi.exe"; "--noninteractive"; "--nologo" |]
        use inReader = new IO.StringReader("")

        // Collectible: the companion is long-lived and creates one session per Run, so each
        // session's own dynamically-compiled user-code assembly should be reclaimable once
        // disposed rather than accumulating for the life of the process.
        //
        // NOTE — `collectible` only isolates the per-session dynamic assembly, not `#r "nuget:"`-
        // resolved package assemblies, which load into the process-wide default
        // AssemblyLoadContext and outlive the session. Two in-process Runs pinning *different*
        // versions of the same package would collide there ("Could not load type … from assembly
        // …"). `run` prevents that by never entering this path for a conflicting pin: it routes
        // such a Run to a throwaway `--worker` child process whose ALC dies with it. So by
        // the time we reach here, the target's pins are safe to load in-process.
        use session =
            FsiEvaluationSession.Create(fsiConfig, args, inReader, Console.Error, Console.Error, collectible = true)

        let setupResult, setupDiags = session.EvalInteractionNonThrowing(combinedSetup)

        match setupResult with
        | Choice2Of2 ex ->
            match
                errorDiagnostics setupDiags
                |> Array.map (setupDiagnostic setupLineCount)
                |> Array.toList
            with
            | [] -> RuntimeError ex.Message
            | errors -> CompileError errors
        | Choice1Of2 _ ->
            let ceLineCount = target.Block.EndLine - target.Block.StartLine + 1
            let targetCode = ceText + "\n|> Request.send"
            let targetResult, targetDiags = session.EvalExpressionNonThrowing(targetCode)

            match targetResult with
            | Choice2Of2 ex ->
                match
                    errorDiagnostics targetDiags
                    |> Array.map (targetDiagnostic target.Block ceLineCount)
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

// ---------------------------------------------------------------------------------------------
// Multi-version isolation. `#r "nuget:"`-resolved assemblies load into the process-wide
// default AssemblyLoadContext and outlive each per-Run FSI session, so a Run pinning a version
// of a package that a previous in-process Run already loaded at a *different* version collides.
// The fix keeps the warm in-process fast path (`runInProcessDirect`) and, only when a pin
// actually conflicts, delegates that one Run to a short-lived `--worker` child process — a fresh
// process, hence a fresh ALC that dies with it. `run` is the router; everything below serves it.
// ---------------------------------------------------------------------------------------------

/// Serialises a `RunOutcome` to the same tagged wire shape the host-facing `run`/`ok`/
/// `compileError`/`runtimeError` envelope uses. Shared by the `--worker` child (which emits its
/// outcome over this shape) and `RequestHandler` (which emits the host's response), so the two
/// channels can't drift apart.
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

/// Parses a `--worker` child's response frame back into a `RunOutcome` — the inverse of
/// `outcomeToWire` — so delegation is transparent to `run`'s caller.
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

/// Matches a `#r "nuget: Package[, Version]"` directive, capturing the package id and (if pinned)
/// the version. Only explicit pins participate in conflict detection — a version-less `#r` is
/// treated as "whatever resolves" and never triggers a worker (best effort; the demo pins).
let private nugetPinRegex =
    Regex("""#r\s+"nuget:\s*(?<pkg>[^,"\s]+)\s*(?:,\s*(?<ver>[^",\s]+))?""", RegexOptions.Compiled)

/// Extracts the `#r "nuget: Package[, Version]"` pins in a script as `(package, version option)`
/// pairs, in source order. A version-less `#r` yields `None` (see `nugetPinRegex`). Public for
/// direct pin-parsing tests; `run` consumes it for conflict routing.
let extractPins (source: string) : (string * string option) list =
    [ for m in nugetPinRegex.Matches source do
          let ver = m.Groups.["ver"]

          m.Groups.["pkg"].Value,
          (if ver.Success && ver.Value <> "" then
               Some ver.Value
           else
               None) ]

// Package ids resolved into this process's default ALC so far, id -> version. Nuget ids are
// case-insensitive. Guarded by a lock: the companion's request loop is serial today, but the
// state is process-global and cheap to make robust against a future concurrent caller.
let private loadLock = obj ()

let private loadedVersions =
    Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

/// True when any of `pins` names a package this process already loaded at a *different* version —
/// the exact condition an in-process Run would collide on.
let private conflictsWithLoaded (pins: (string * string option) list) : bool =
    lock loadLock (fun () ->
        pins
        |> List.exists (fun (pkg, ver) ->
            match ver with
            | Some v ->
                match loadedVersions.TryGetValue pkg with
                | true, loaded -> loaded <> v
                | _ -> false
            | None -> false))

/// Records the versions an about-to-run in-process eval will load, so a later differently-pinned
/// Run is detected as a conflict. Called only on the in-process path — a worker Run loads into
/// its own process, not ours, so it must not touch this map.
let private markLoaded (pins: (string * string option) list) =
    lock loadLock (fun () ->
        for pkg, ver in pins do
            match ver with
            | Some v -> loadedVersions.[pkg] <- v
            | None -> ())

/// Runs one block in a throwaway child process (`dotnet Companion.dll --worker`) so its
/// `#r "nuget:"` assemblies load into a fresh default ALC that is reclaimed when the process
/// exits — sidestepping the process-global collision. The child speaks one framed
/// `{ source, blockIndex }` request in, one outcome envelope out, then exits.
let private runInWorker (source: string) (blockIndex: int) : RunOutcome =
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

            let request: obj =
                {| source = source
                   blockIndex = blockIndex |}

            writeFrame proc.StandardInput.BaseStream (JsonSerializer.SerializeToUtf8Bytes request)
            proc.StandardInput.Close()

            // A crashed worker closes stdout without a frame -> None -> a clean runtimeError,
            // rather than a hang. Its stderr (FCS/user output) inherits ours, so no drain needed.
            let outcome =
                match tryReadFrame proc.StandardOutput.BaseStream with
                | Some payload ->
                    use doc = JsonDocument.Parse(payload: byte[])
                    wireToOutcome doc.RootElement
                | None -> RuntimeError "worker: evaluation process produced no response"

            proc.WaitForExit()
            outcome
    with ex ->
        RuntimeError(sprintf "worker: %s" ex.Message)

/// Runs the `blockIndex`-th located block (0-based, source order) and returns its outcome. Routes
/// on whether the target's `#r "nuget:"` pins conflict with a version already loaded in this
/// process: no conflict -> the warm in-process session; conflict -> a fresh `--worker` child
/// whose ALC won't collide.
let run (source: string) (blockIndex: int) : RunOutcome =
    let pins = extractPins source

    if conflictsWithLoaded pins then
        runInWorker source blockIndex
    else
        markLoaded pins
        runInProcessDirect source blockIndex
