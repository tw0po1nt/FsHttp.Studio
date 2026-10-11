module ProcessStop

/// The time between the close of the companion stdin and the kill of its process tree.
let stopDelayMs = 1000

type KillCommand =
    | SignalGroup of pid: int
    | Taskkill of pid: int

/// `platform` is the value of Node's `process.platform`.
let killCommand (platform: string) (pid: int) : KillCommand =
    if platform = "win32" then Taskkill pid else SignalGroup pid

let taskkillArgs (pid: int) : string[] = [| "/T"; "/F"; "/PID"; string pid |]

/// Windows has no process group to signal, and a detached child there opens a console window.
let detachOnSpawn (platform: string) : bool = platform <> "win32"
