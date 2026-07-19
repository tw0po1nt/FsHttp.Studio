// Hand-rolled Node.js interop — only the slice this project touches (SageFs's proven
// bindings strategy).
module Node

open Fable.Core

type Writable =
    abstract write: chunk: byte[] -> bool

type Readable =
    abstract on: event: string * listener: (obj -> unit) -> unit

type ChildProcess =
    abstract stdin: Writable
    abstract stdout: Readable
    abstract on: event: string * listener: (obj -> unit) -> unit
    abstract kill: unit -> bool

type IChildProcessModule =
    abstract spawn: command: string * args: string[] * options: obj -> ChildProcess
    /// child_process.execFile(file, args, callback) — runs a short-lived command and buffers its
    /// output. The callback is `(error, stdout, stderr)`: `error` is null on a zero exit, or a
    /// Node error (e.g. `ENOENT` when `file` isn't found) otherwise. Used to probe `dotnet
    /// --list-sdks` at activation without blocking the extension host.
    abstract execFile: file: string * args: string[] * callback: (obj -> string -> string -> unit) -> unit

[<Import("*", "child_process")>]
let childProcess: IChildProcessModule = jsNative

// path.join is variadic; Node won't accept a single array argument, so this spreads
// the F# array at the JS call site rather than passing it as one positional arg.
[<Import("*", "path")>]
let private pathModule: obj = jsNative

[<Emit("$0.join(...$1)")>]
let private joinNative (_m: obj) (_segments: string[]) : string = jsNative

module Path =
    let join (segments: string[]) : string = joinNative pathModule segments
