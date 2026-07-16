// Spawns the companion process and drives the one framed-envelope round-trip that
// proves the transport (issue #14). Block location / run requests are later tickets.
module Companion

open Fable.Core
open Fable.Core.JsInterop
open Node
open Envelope

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

type Handle = { Process: ChildProcess }

let start (companionDllPath: string) (onState: State -> unit) : Handle =
    onState Starting

    let options = box {| stdio = [| "pipe"; "pipe"; "pipe" |] |}
    let child = childProcess.spawn ("dotnet", [| companionDllPath |], options)

    let parser =
        FrameParser(fun payload ->
            let json = JS.JSON.parse (decodeUtf8 payload)
            let tag = json?tag |> unbox<string>

            match tag with
            | "ready" -> onState Ready
            | _ -> ())

    child.stdout.on ("data", fun chunk -> parser.Push(unbox<byte[]> chunk))

    child.on (
        "error",
        fun err ->
            let code = (err: obj)?code |> unbox<string>

            if code = "ENOENT" then
                onState SdkNotFound
            else
                onState Stopped
    )

    child.on ("exit", fun _ -> onState Stopped)

    child.stdin.write (encodeFrame (encodeUtf8 "{\"tag\":\"hello\"}")) |> ignore

    { Process = child }

let stop (handle: Handle) = handle.Process.kill () |> ignore
