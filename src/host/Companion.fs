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
open Js
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

[<NoComparison; NoEquality>]
type private Pending =
    { Resolve: obj -> unit
      Abandon: unit -> unit }

[<NoComparison>]
type Handle =
    private
        { Process: ChildProcess
          Pending: ResizeArray<Pending>
          mutable Closed: bool }

let private toBlockRange (r: obj) : BlockRange =
    { StartLine = unbox<int> (r?startLine: obj)
      StartCol = unbox<int> (r?startCol: obj)
      EndLine = unbox<int> (r?endLine: obj)
      EndCol = unbox<int> (r?endCol: obj)
      Refusal =
        let refusal: obj = r?refusal

        if isNullish refusal then
            None
        else
            Some(unbox<string> refusal) }

let private toHeaders (h: obj) : (string * string) list =
    let keys: string[] = JsInterop.emitJsExpr h "Object.keys($0)"
    keys |> Array.map (fun k -> k, unbox<string> (h?(k): obj)) |> Array.toList

let private toOkRequestFields (request: obj) : OkRequestFields =
    { Method = unbox<string> (request?method: obj)
      Url = unbox<string> (request?url: obj)
      Headers = toHeaders (request?headers: obj)
      BodyState = unbox<string> (request?bodyState: obj)
      BodyBase64 = unbox<string> (request?bodyBase64: obj)
      BodyReason = unbox<string> (request?bodyReason: obj) }

let private decodeRunFrame (json: obj) : RunFrame =
    match unbox<string> (json?tag: obj) with
    | "ok" ->
        let request: obj = json?request

        OkFrame
            { Status = unbox<int> (json?status: obj)
              Reason = unbox<string> (json?reason: obj)
              Headers = toHeaders (json?headers: obj)
              ContentType = unbox<string> (json?contentType: obj)
              BodyBase64 = unbox<string> (json?bodyBase64: obj)
              RequestMs = unbox<float> (json?requestMs: obj)
              Request =
                if isNullish request then
                    None
                else
                    Some(toOkRequestFields request) }
    | "compileError" ->
        let diagnostics: obj[] = unbox (json?diagnostics: obj)

        diagnostics
        |> Array.map (fun d ->
            { Message = unbox<string> (d?message: obj)
              Range = toBlockRange (d?range: obj) })
        |> Array.toList
        |> CompileErrorFrame
    | "runtimeError" -> RuntimeErrorFrame(unbox<string> (json?message: obj))
    | "refused" ->
        // The companion omits `name` for every code it produces today, so a missing property
        // decodes to `None` rather than to `undefined` (the same shape as `refusal` above).
        let name: obj = json?name

        RefusedFrame(unbox<string> (json?code: obj), (if isNullish name then None else Some(unbox<string> name)))
    | _ -> ProtocolErrorFrame(unbox<string> (json?message: obj))

/// Abandons every pending entry along its own path and marks the handle closed
/// (docs/spec/0004-run-path-robustness.md, Decision 6), so a `send` that arrives afterwards
/// abandons immediately instead of enqueueing onto a queue that nothing will flush again.
/// Called from both the `exit` and the `error` handler, because a spawn failure such as
/// `ENOENT` reaches `error` and leaves the same queue behind. A second call drains an empty
/// queue, so the two handlers may both fire.
///
/// The queue is drained and the handle is closed *before* any `Abandon` runs. That order is
/// load-bearing: an abandon path that re-enters `send` then abandons in turn, rather than
/// enqueueing onto the queue this call is already flushing.
let private flushPending (handle: Handle) =
    let entries = handle.Pending.ToArray()
    handle.Pending.Clear()
    handle.Closed <- true
    entries |> Array.iter (fun entry -> entry.Abandon())

/// `dotnetPath` is the SDK-bearing `dotnet` host that activation resolved (see Extension.fs).
/// It is the `fshttpStudio.dotnetPath` override when the user sets that override. Otherwise it
/// is `"dotnet"` from PATH, after `--list-sdks` confirms an SDK at or above the companion's
/// target major version. The companion needs a full SDK, not only a runtime, because FSI's
/// `#r "nuget:"` restore needs one.
let start (dotnetPath: string) (companionDllPath: string) (onState: State -> unit) : Handle =
    onState Starting

    let options: obj = nonNull (box {| stdio = [| "pipe"; "pipe"; "pipe" |] |})
    let child = childProcess.spawn (dotnetPath, [| companionDllPath |], options)

    let handle =
        { Process = child
          Pending = ResizeArray<Pending>()
          Closed = false }

    let parser =
        FrameParser(fun payload ->
            let json: obj = JS.JSON.parse (decodeUtf8 payload)
            let tag = (json?tag: obj) |> unbox<string>

            match tag with
            | "ready" -> onState Ready
            | _ ->
                if handle.Pending.Count > 0 then
                    let { Resolve = resolve }: Pending = handle.Pending.[0]
                    handle.Pending.RemoveAt(0)
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

            flushPending handle
    )

    child.on (
        "exit",
        fun _ ->
            onState Stopped
            flushPending handle
    )

    child.stdin.write (encodeFrame (encodeUtf8 "{\"tag\":\"hello\"}")) |> ignore

    handle

let private send (handle: Handle) (payloadJson: string) (entry: Pending) =
    if handle.Closed then
        entry.Abandon()
    else
        handle.Pending.Add(entry)
        handle.Process.stdin.write (encodeFrame (encodeUtf8 payloadJson)) |> ignore

/// Sends a `locate` request over the framed envelope. It resolves with the block ranges after
/// the companion's `blocks` response arrives, or with an empty list if the companion is gone
/// (docs/spec/0004-run-path-robustness.md, Decision 6) — the honest degraded state, since there
/// is nothing left to locate blocks in.
let locate (handle: Handle) (source: string) : Async<BlockRange list> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        let payload: obj = createObj [ "tag" ==> "locate"; "source" ==> source ]

        let entry =
            { Resolve =
                fun json ->
                    let ranges: obj[] = unbox (json?ranges: obj)
                    resolve (ranges |> Array.map toBlockRange |> Array.toList)
              Abandon = fun () -> resolve [] }

        send handle (JS.JSON.stringify payload) entry)

/// Sends a `run` request for the located block at `blockIndex`, and resolves with its outcome.
/// The index is 0-based, and it matches the order of an earlier `locate`. `scriptFileName` is
/// the script's own absolute path when it is saved on disk, so FSI can set
/// `__SOURCE_DIRECTORY__`. It is `None` for a script with no such path, which keeps FSI's
/// default. `timeoutMs` is the request bound from `fshttpStudio.requestTimeoutMs`. `0` means
/// do not inject a bound. Abandons to `RunProtocolError` if the companion is gone
/// (docs/spec/0004-run-path-robustness.md, Decision 6).
let run
    (handle: Handle)
    (source: string)
    (blockIndex: int)
    (scriptFileName: string option)
    (timeoutMs: int)
    : Async<RunResult> =
    Async.FromContinuations(fun (resolve, _reject, _cancel) ->
        // One shape, always. The companion reads the empty string as "no value", so the absent
        // case needs no second payload to construct here.
        let payload: obj =
            createObj
                [ "tag" ==> "run"
                  "source" ==> source
                  "blockIndex" ==> blockIndex
                  "scriptFileName" ==> defaultArg scriptFileName ""
                  "timeoutMs" ==> timeoutMs ]

        let entry =
            { Resolve = fun json -> resolve (parseRunResult (decodeRunFrame json))
              Abandon = fun () -> resolve (RunProtocolError Refusals.companionStopped.Detail) }

        send handle (JS.JSON.stringify payload) entry)

let stop (handle: Handle) = handle.Process.kill () |> ignore
