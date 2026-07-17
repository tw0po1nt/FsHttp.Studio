// The extension-host side of the framed envelope transport (ADR-0002). Mirrors
// companion/Envelope.fs's framing exactly; this side runs in Node/JS via Fable.
module Envelope

open System.Text

/// Incrementally decodes length-prefixed frames out of a stream of Node Buffer chunks,
/// buffering a partial frame across calls.
type FrameParser(onFrame: byte[] -> unit) =
    let mutable buffer: byte[] = [||]

    member _.Push(chunk: byte[]) =
        buffer <- Array.append buffer chunk

        let mutable keepGoing = true

        while keepGoing do
            if buffer.Length < 4 then
                keepGoing <- false
            else
                let len =
                    (int buffer.[0] <<< 24)
                    ||| (int buffer.[1] <<< 16)
                    ||| (int buffer.[2] <<< 8)
                    ||| int buffer.[3]

                if buffer.Length < 4 + len then
                    keepGoing <- false
                else
                    let payload = Array.sub buffer 4 len
                    buffer <- Array.sub buffer (4 + len) (buffer.Length - 4 - len)
                    onFrame payload

let encodeFrame (payload: byte[]) : byte[] =
    let len = payload.Length

    let prefix = [| byte (len >>> 24); byte (len >>> 16); byte (len >>> 8); byte len |]

    Array.append prefix payload

let encodeUtf8 (s: string) : byte[] = Encoding.UTF8.GetBytes s

let decodeUtf8 (bytes: byte[]) : string = Encoding.UTF8.GetString bytes
