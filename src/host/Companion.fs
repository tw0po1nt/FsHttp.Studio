// Spawns the companion process and speaks its request and response protocol. That protocol is
// the walking skeleton's `hello` and `ready` handshake, plus `locate` and `run` on top of it.
//
// The companion's own I/O loop (`Program.fs`) reads one frame, responds, and then reads the
// next frame. Responses therefore always arrive in the order of their requests. One FIFO queue
// of pending resolvers, dequeued on every frame that is not `ready`, is enough to pair each
// response with the request that caused it. No request id needs to cross the wire.
module Companion

open Fable.Core
open Fable.Core.JsInterop
open Node
open Envelope
open Protocol

type State =
    | Starting
    | Ready
    | SdkNotFound
    | Stopped

let statusText =
    function
    | Starting -> "starting…"
    | Ready -> "ready"
    | SdkNotFound -> ".NET SDK not found"
    | Stopped -> "companion stopped"

[<NoComparison>]
type Handle =
    { Process: ChildProcess
      Pending: ResizeArray<obj -> unit> }

let private toBlockRange (r: obj) : BlockRange =
    { StartLine = unbox<int> (r?startLine: obj)
      StartCol = unbox<int> (r?startCol: obj)
      EndLine = unbox<int> (r?endLine: obj)
      EndCol = unbox<int> (r?endCol: obj) }

let private toHeaders (h: obj) : (string * string) list =
    let keys: string[] = JsInterop.emitJsExpr h "Object.keys($0)"
    keys |> Array.map (fun k -> k, unbox<string> (h?(k): obj)) |> Array.toList

let private parseRunResult (json: obj) : RunResult =
    match unbox<string> (json?tag: obj) with
    | "ok" ->
        RunOk(
            unbox<int> (json?status: obj),
            unbox<string> (json?reason: obj),
            toHeaders (json?headers: obj),
            unbox<string> (json?contentType: obj),
            unbox<string> (json?bodyBase64: obj)
        )
    | "compileError" ->
        let diagnostics: obj[] = unbox (json?diagnostics: obj)

        diagnostics
        |> Array.map (fun d ->
            { Message = unbox<string> (d?message: obj)
              Range = toBlockRange (d?range: obj) })
        |> Array.toList
        |> RunCompileError
    | "runtimeError" -> RunRuntimeError(unbox<string> (json?message: obj))
    | _ -> RunProtocolError(unbox<string> (json?message: obj))

/// `dotnetPath` is the SDK-bearing `dotnet` host that activation resolved (see Extension.fs).
/// It is the `fshttpStudio.dotnetPath` override when the user sets that override. Otherwise it
/// is `"dotnet"` from PATH, after `--list-sdks` confirms an SDK at or above the companion's
/// target major version. The companion needs a full SDK, not only a runtime, because FSI's
/// `#r "nuget:"` restore needs one.
let start (dotnetPath: string) (companionDllPath: string) (onState: State -> unit) : Handle =
    onState Starting

    let options: obj = nonNull (box {| stdio = [| "pipe"; "pipe"; "pipe" |] |})
    let child = childProcess.spawn (dotnetPath, [| companionDllPath |], options)
    let pending = ResizeArray<obj -> unit>()

    let parser =
        FrameParser(fun payload ->
            let json = JS.JSON.parse (decodeUtf8 payload)
            let tag = (json?tag: obj) |> unbox<string>

            match tag with
            | "ready" -> onState Ready
            | _ ->
                if pending.Count > 0 then
                    let resolve = pending.[0]
                    pending.RemoveAt(0)
                    resolve json)

    child.stdout.on ("data", fun chunk -> parser.Push(unbox<byte[]> chunk))

    child.on (
        "error",
        fun err ->
            let code = ((err: obj)?code: obj) |> unbox<string>

            if code = "ENOENT" then
                onState SdkNotFound
            else
                onState Stopped
    )

    child.on ("exit", fun _ -> onState Stopped)

    child.stdin.write (encodeFrame (encodeUtf8 "{\"tag\":\"hello\"}")) |> ignore

    { Process = child; Pending = pending }

let private send (handle: Handle) (payloadJson: string) (onResponse: obj -> unit) =
    handle.Pending.Add(onResponse)
    handle.Process.stdin.write (encodeFrame (encodeUtf8 payloadJson)) |> ignore

/// Sends a `locate` request over the framed envelope. It resolves with the block ranges after
/// the companion's `blocks` response arrives.
let locate (handle: Handle) (source: string) : Async<BlockRange list> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        let payload: obj = createObj [ "tag" ==> "locate"; "source" ==> source ]

        send handle (JS.JSON.stringify payload) (fun json ->
            let ranges: obj[] = unbox (json?ranges: obj)
            resolve (ranges |> Array.map toBlockRange |> Array.toList)))

/// Sends a `run` request for the located block at `blockIndex`, and resolves with its outcome.
/// The index is 0-based, and it matches the order of an earlier `locate`.
let run (handle: Handle) (source: string) (blockIndex: int) : Async<RunResult> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        let payload: obj =
            createObj [ "tag" ==> "run"; "source" ==> source; "blockIndex" ==> blockIndex ]

        send handle (JS.JSON.stringify payload) (fun json -> resolve (parseRunResult json)))

let stop (handle: Handle) = handle.Process.kill () |> ignore
