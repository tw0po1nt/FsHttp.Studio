// PROTOTYPE — the bits of Node the slice needs: kill a process by pattern, hit the test
// server's control endpoint, read env, and stamp the clock.
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

/// Every pid whose command line matches `pattern`. Empty when nothing matches — `pgrep`
/// exits 1 in that case, which `sh` swallows.
let pidsMatching (pattern: string) : int[] =
    (sh (sprintf "pgrep -f '%s'" pattern)).Split('\n')
    |> Array.choose (fun line ->
        match System.Int32.TryParse(line.Trim()) with
        | true, pid -> Some pid
        | _ -> None)

/// SIGKILL, not SIGTERM: the check is about the companion vanishing, not about a clean exit.
let kill (pid: int) = sh (sprintf "kill -9 %d" pid) |> ignore

let isAlive (pid: int) =
    (sh (sprintf "kill -0 %d 2>/dev/null && echo alive" pid)).Contains("alive")

/// A plain GET, used for the test server's `/release` control endpoint and its healthcheck.
let httpStatus (url: string) : string =
    (sh (sprintf "curl -sS -m 10 -o /dev/null -w '%%{http_code}' '%s'" url)).Trim()

let httpBody (url: string) : string =
    (sh (sprintf "curl -sS -m 10 '%s'" url)).Trim()

let readSidecarField (path: string) (field: string) : string =
    let parsed: obj = JS.JSON.parse(readFile path)
    unbox<string> (parsed?(field): obj)
