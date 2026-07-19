// Spawns the companion process and speaks its request/response protocol: the walking
// skeleton's `hello`/`ready` handshake, plus `locate`/`run` built on top.
// The companion's own I/O loop (`Program.fs`) reads one frame, responds, then reads the next —
// so responses always arrive in the order their requests were sent. A single FIFO queue of
// pending resolvers, dequeued on every non-`ready` frame, is therefore enough to pair each
// response with the request that caused it; no request id needs to cross the wire.
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

/// `dotnetPath` is the host executable the .NET Install Tool resolved (see Extension.fs / #57) —
/// never the bare `"dotnet"`, so the companion runs against the acquired runtime rather than
/// whatever is (or isn't) on PATH.
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

/// Sends a `locate` request over the framed envelope and resolves with the block ranges once
/// the companion's `blocks` response arrives.
let locate (handle: Handle) (source: string) : Async<BlockRange list> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        let payload: obj = createObj [ "tag" ==> "locate"; "source" ==> source ]

        send handle (JS.JSON.stringify payload) (fun json ->
            let ranges: obj[] = unbox (json?ranges: obj)
            resolve (ranges |> Array.map toBlockRange |> Array.toList)))

/// Sends a `run` request for the `blockIndex`-th located block (0-based, matching a prior
/// `locate`'s ordering) and resolves with its outcome.
let run (handle: Handle) (source: string) (blockIndex: int) : Async<RunResult> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        let payload: obj =
            createObj [ "tag" ==> "run"; "source" ==> source; "blockIndex" ==> blockIndex ]

        send handle (JS.JSON.stringify payload) (fun json -> resolve (parseRunResult json)))

let stop (handle: Handle) = handle.Process.kill () |> ignore
