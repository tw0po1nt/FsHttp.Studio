// Node and shell helpers for the UI harness: sidecar I/O, HTTP probes, and process lookup.
module Proc

open Fable.Core
open Fable.Core.JsInterop

/// Upper bound on any shell command this module runs. A hung `curl` or `pgrep` must not hold the
/// harness past its own budget, so the wait is bounded and expiry reads as "no output".
let private shellTimeoutMs = 30_000

/// Runs a shell command and returns its stdout. A non-zero exit, or a run that outlives
/// `shellTimeoutMs`, returns "" rather than throwing: every caller here treats "no output" as the
/// answer.
[<Emit("(() => { try { return require('node:child_process').execSync($0, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], timeout: $1 }); } catch (e) { return ''; } })()")>]
let sh (_command: string) (_timeoutMs: int) : string = jsNative

let private run (command: string) = sh command shellTimeoutMs

[<Emit("process.env[$0] ?? $1")>]
let env (_name: string) (_fallback: string) : string = jsNative

[<Emit("Date.now()")>]
let now () : float = jsNative

[<Emit("console.log($0)")>]
let log (_message: string) : unit = jsNative

[<Emit("require('node:fs').readFileSync($0, 'utf8')")>]
let readFile (_path: string) : string = jsNative

[<Emit("require('node:fs').existsSync($0)")>]
let fileExists (_path: string) : bool = jsNative

[<Emit("require('node:fs').appendFileSync($0, $1)")>]
let appendFile (_path: string) (_text: string) : unit = jsNative

/// Every pid whose command line matches `pattern`. Empty when nothing matches — `pgrep`
/// exits 1 in that case, which `sh` swallows.
let pidsMatching (pattern: string) : int[] =
    (run (sprintf "pgrep -f '%s'" pattern)).Split('\n')
    |> Array.choose (fun line ->
        match System.Int32.TryParse(line.Trim()) with
        | true, pid -> Some pid
        | _ -> None)

/// SIGKILL, not SIGTERM: the companion-death check is about the process vanishing, not about a
/// clean exit the runtime could still report on.
let kill (pid: int) : unit =
    run (sprintf "kill -9 %d" pid) |> ignore

/// True when `pid` still exists. Uses `kill -0`, which does not deliver a signal.
let isAlive (pid: int) : bool =
    (run (sprintf "kill -0 %d 2>/dev/null && echo alive" pid)).Contains "alive"

let httpStatus (url: string) : string =
    (run (sprintf "curl -sS -m 10 -o /dev/null -w '%%{http_code}' '%s'" url)).Trim()

let httpBody (url: string) : string =
    (run (sprintf "curl -sS -m 10 '%s'" url)).Trim()

/// True when nothing accepts connections on `url` (the sidecar dead port).
let curlConnectionRefused (url: string) : bool =
    let code = httpStatus url
    code = "" || code = "000"

/// Where this run's test server wrote its sidecar, as `run.sh` exports it. `None` when the
/// variable is unset — the name of the variable lives here alone, and each caller words its own
/// failure around what it needed the path for.
let sidecarPath () : string option =
    match env "UI_TEST_SIDECAR" "" with
    | "" -> None
    | path -> Some path

/// The outcome of reading the test server's sidecar. Missing and unreadable are separate cases
/// because setup must name which of the two happened: a missing file means the server never
/// started, and an unreadable one means it wrote something the harness cannot trust.
type SidecarRead =
    | SidecarMissing
    | SidecarUnreadable of reason: string
    | SidecarLive of baseUrl: string * deadUrl: string

let readSidecar (path: string) : SidecarRead =
    if not (fileExists path) then
        SidecarMissing
    else
        try
            let parsed: obj = JS.JSON.parse (readFile path)
            let baseUrl = unbox<string> (parsed?("baseUrl"): obj)
            let deadUrl = unbox<string> (parsed?("deadUrl"): obj)

            if System.String.IsNullOrWhiteSpace baseUrl then
                SidecarUnreadable "it names no baseUrl"
            elif System.String.IsNullOrWhiteSpace deadUrl then
                SidecarUnreadable "it names no deadUrl"
            else
                SidecarLive(baseUrl.TrimEnd('/'), deadUrl)
        with _ ->
            SidecarUnreadable "it is not valid JSON"

/// Appends `markdown` to the GitHub Actions job summary, and echoes it to the console so a local
/// run sees the same table. Returns true when the job summary file itself received it, which is
/// false outside Actions.
let appendJobSummary (markdown: string) : bool =
    let summary = env "GITHUB_STEP_SUMMARY" ""
    let reachedJobSummary = summary <> ""

    if reachedJobSummary then
        appendFile summary markdown

    log markdown
    reachedJobSummary
