module ProcessStop

let stopDelayMs = 1000

let taskkillTimeoutMs = 5000

type Platform =
    | Windows
    | Posix

/// `nodePlatform` is the value of Node's `process.platform`.
let platformOf (nodePlatform: string) : Platform =
    if nodePlatform = "win32" then Windows else Posix

type KillCommand =
    | SignalGroup of pid: int
    | Exec of file: string * args: string[]

let killCommand (platform: Platform) (pid: int) : KillCommand =
    match platform with
    | Windows -> Exec("taskkill", [| "/T"; "/F"; "/PID"; string pid |])
    | Posix -> SignalGroup pid

/// Windows has no process group to signal, and a detached child there opens a console window.
let detachOnSpawn (platform: Platform) : bool =
    match platform with
    | Windows -> false
    | Posix -> true
