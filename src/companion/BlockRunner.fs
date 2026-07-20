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

/// `PropertyInfo.GetProperty` is nullable-annotated (the name might not exist); the fields this
/// reads are FsHttp's `Response` record shape, which has been stable across the FsHttp versions
/// this targets — so a missing property is a genuine extraction bug, not a case to recover from.
/// (The response body itself is read off the BCL `HttpContent` type, which ADR-0002 commits to as
/// version-independent.)
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

/// What a package resolved to when it was loaded into this process's default ALC. `Pinned v` is an
/// explicit `#r "nuget: pkg, v"`; `Versionless` is a `#r "nuget: pkg"` that resolved *some* latest
/// we can't name. Tracking the version-less case (rather than nothing) is load-bearing: it still
/// poisons the ALC, so a later Run pinning a *different* version would collide against it in-process
/// (ADR-0006). Public for the routing unit tests.
type LoadedVersion =
    | Pinned of string
    | Versionless

/// Pure routing decision for one pin against the state a package is already loaded in (`None` = not
/// loaded here yet). The rule is "route to a worker unless we can *prove* the requested load matches
/// what's already in the ALC":
///
/// - A version-less pin against a version-less load is the same latest (nuget resolves `#r
///   "nuget: pkg"` to one version per process), so it's safe in-process.
/// - Two explicit pins conflict exactly when they name different versions.
/// - Every *mixed* pairing conflicts: a version-less load vs. a later explicit pin, and an explicit
///   load vs. a later version-less pin, both route to a worker — the version-less side may resolve a
///   different latest than the named one, and we can't prove it doesn't.
///
/// Deliberately conservative: a false conflict costs one cold worker Run; a false match reopens the
/// original "Could not load type … from assembly …" ALC collision. Correctness wins.
let pinConflicts (loaded: LoadedVersion option) (pin: string option) : bool =
    match loaded, pin with
    | None, _ -> false
    | Some Versionless, None -> false
    | Some(Pinned l), Some v -> l <> v
    | Some(Pinned _), None
    | Some Versionless, Some _ -> true

// Package ids resolved into this process's default ALC so far, id -> what loaded it. Nuget ids are
// case-insensitive. Guarded by a lock: the companion's request loop is serial today, but the
// state is process-global and cheap to make robust against a future concurrent caller.
let private loadLock = obj ()

let private loadedVersions =
    Dictionary<string, LoadedVersion>(StringComparer.OrdinalIgnoreCase)

/// Where `run` sends one Run: the warm in-process session, or a fresh `--worker` child.
type private RunRoute =
    | InProcess
    | Worker

/// Routes one Run's `pins` and, on the in-process path, reserves them in the load map — the
/// conflict *check* and the reservation *act* under a single `lock loadLock` so the two are one
/// atomic step. Taking the lock once per logical operation (not once per access) is the point: a
/// check in one lock scope followed by a mark in another leaves a TOCTOU gap where a future
/// concurrent caller could load a conflicting version between them (coding-standards rule 4). The
/// request loop is serial today, but the lock exists precisely to stay correct when it isn't.
///
/// Reserving *before* the eval runs — rather than after a successful load — is deliberate. The map
/// is a conservative over-approximation of what the shared ALC may hold: a Run that reaches the
/// in-process path can resolve its `#r "nuget:"` into that ALC, and once resolved the assembly
/// outlives the session whether the eval then compile-errors or throws. Over-marking a Run that
/// never actually loaded only ever over-routes a *later* Run to a (safe, cold) worker; *under*-
/// marking one that did load reopens the "Could not load type … from assembly …" ALC collision
/// (ADR-0006). The errors aren't symmetric, so we err toward marking, and mark up front.
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
            // A version-less `#r` is recorded as `Versionless` — *not* skipped — so it still
            // participates in a later Run's conflict detection.
            for pkg, ver in pins do
                loadedVersions.[pkg] <-
                    match ver with
                    | Some v -> Pinned v
                    | None -> Versionless

            InProcess)

/// Bound on how long a `--worker` child may take to produce its response frame before the Run is
/// force-terminated (coding-standards rule 3: every external process gets a bounded wait + kill
/// path). Long enough to absorb a cold first-run `#r "nuget:"` restore of a freshly-pinned
/// version, short enough that a wedged worker — a user block that loops forever, or a request to a
/// server that never answers — can't hang the Run indefinitely. Public so tests can drive the
/// hung path on a short bound.
let workerTimeoutMs = 120_000

/// Runs one block in a throwaway child process (`dotnet Companion.dll --worker`) so its
/// `#r "nuget:"` assemblies load into a fresh default ALC that is reclaimed when the process
/// exits — sidestepping the process-global collision. The child speaks one framed
/// `{ source, blockIndex }` request in, one outcome envelope out, then exits.
///
/// The wait is bounded by `timeoutMs`: a worker that never produces its frame in time (a user
/// block looping forever, a request that never answers), or produces it then stalls before
/// exiting, is `Kill()`ed and the Run mapped to a `RuntimeError` rather than wedging the caller
/// forever. `use proc = proc` disposes the handle but does not unblock a wait, so the bound and
/// the kill — not disposal — are what guarantee the Run always terminates.
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

            // Best-effort teardown: a worker mid-restore can spawn child `dotnet` processes, so
            // take the whole tree. Never throw out of a kill — the process may already be gone.
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
            // *crashed* child closes stdout without a frame -> None -> a clean runtimeError; a
            // *hung* child produces nothing at all, so the read never returns — `Wait timeoutMs`
            // caps that and we Kill() on expiry instead of blocking here forever. Killing closes
            // the pipe, so the read task then completes (as None) and its thread is released. The
            // child's stderr (FCS/user output) inherits ours, so no drain is needed.
            let readFrame = Task.Run(fun () -> tryReadFrame proc.StandardOutput.BaseStream)

            if not (readFrame.Wait timeoutMs) then
                kill ()
                RuntimeError(sprintf "worker: no response within %dms; evaluation process terminated" timeoutMs)
            else
                let outcome =
                    match readFrame.Result with
                    | Some payload ->
                        use doc = JsonDocument.Parse(payload: byte[])
                        wireToOutcome doc.RootElement
                    | None -> RuntimeError "worker: evaluation process produced no response"

                // Frame's in hand; the child should now exit on its own. Bound that wait too — a
                // worker that emitted its frame then stalled must not wedge `WaitForExit` — and
                // Kill() it if it overstays.
                if not (proc.WaitForExit timeoutMs) then
                    kill ()

                outcome
    with ex ->
        RuntimeError(sprintf "worker: %s" ex.Message)

/// Runs the `blockIndex`-th located block (0-based, source order) and returns its outcome. Routes
/// on whether the target's `#r "nuget:"` pins conflict with a version already loaded in this
/// process: no conflict -> the warm in-process session; conflict -> a fresh `--worker` child
/// whose ALC won't collide.
let run (source: string) (blockIndex: int) : RunOutcome =
    match routeAndReserve (extractPins source) with
    | Worker -> runInWorker workerTimeoutMs source blockIndex
    | InProcess -> runInProcessDirect source blockIndex
