// Hand-rolled Node.js interop. It covers only the part that this project uses, which follows
// SageFs's proven bindings strategy.
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

    /// child_process.execFile(file, args, options, callback). It runs a short-lived command and
    /// buffers the output. The callback is `(error, stdout, stderr)`. `error` is null on a zero
    /// exit, and a Node error otherwise. It is `ENOENT` when `file` does not exist. It is a
    /// killed-on-`timeout` error when the child outlives the `timeout` option, because Node
    /// kills the child with `killSignal`. Activation uses this to probe `dotnet --list-sdks`.
    /// The bound makes sure that a stalled `dotnet` cannot hang the probe.
    abstract execFile:
        file: string * args: string[] * options: obj * callback: (obj -> string -> string -> unit) -> unit

[<Import("*", "child_process")>]
let childProcess: IChildProcessModule = jsNative

type IFileSystemModule =
    /// fs.readFileSync(path, encoding). It reads a file to a string, or throws, for example
    /// with `ENOENT`.
    abstract readFileSync: path: string * encoding: string -> string

[<Import("*", "fs")>]
let fs: IFileSystemModule = jsNative

// path.join is variadic, and Node does not accept a single array argument. This binding
// therefore spreads the F# array at the JS call site, instead of one positional argument.
[<Import("*", "path")>]
let private pathModule: obj = jsNative

[<Emit("$0.join(...$1)")>]
let private joinNative (_m: obj) (_segments: string[]) : string = jsNative

module Path =
    let join (segments: string[]) : string = joinNative pathModule segments
