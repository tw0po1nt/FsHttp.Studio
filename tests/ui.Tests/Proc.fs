// Node and shell helpers for the UI harness: sidecar I/O, HTTP probes, and process lookup.
module Proc

open Fable.Core
open Fable.Core.JsInterop

/// Runs a shell command and returns its stdout. A non-zero exit returns "" rather than
/// throwing: every caller here treats "no output" as the answer.
[<Emit("(() => { try { return require('node:child_process').execSync($0, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }); } catch (e) { return ''; } })()")>]
let sh (_command: string) : string = jsNative

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
    (sh (sprintf "pgrep -f '%s'" pattern)).Split('\n')
    |> Array.choose (fun line ->
        match System.Int32.TryParse(line.Trim()) with
        | true, pid -> Some pid
        | _ -> None)

let killCompanionProcesses () =
    sh "pkill -f 'dist/companion/Companion.dll' 2>/dev/null || true" |> ignore

let httpStatus (url: string) : string =
    (sh (sprintf "curl -sS -m 10 -o /dev/null -w '%%{http_code}' '%s'" url)).Trim()

let httpBody (url: string) : string =
    (sh (sprintf "curl -sS -m 10 '%s'" url)).Trim()

/// True when nothing accepts connections on `url` (the sidecar dead port).
let curlConnectionRefused (url: string) : bool =
    let code = httpStatus url
    code = "" || code = "000"

let tryParseSidecar (path: string) =
    if not (fileExists path) then
        None
    else
        try
            let parsed: obj = JS.JSON.parse (readFile path)
            let baseUrl = unbox<string> (parsed?("baseUrl"): obj)
            let deadUrl = unbox<string> (parsed?("deadUrl"): obj)

            if
                System.String.IsNullOrWhiteSpace baseUrl
                || System.String.IsNullOrWhiteSpace deadUrl
            then
                None
            else
                Some(baseUrl.TrimEnd('/'), deadUrl)
        with _ ->
            None

let appendJobSummary (markdown: string) =
    let summary = env "GITHUB_STEP_SUMMARY" ""

    if summary <> "" then
        appendFile summary markdown

    log markdown
