module Companion.Envelope

open System.IO

/// 4-byte big-endian length prefix + JSON payload. Framed, not line-delimited, so an
/// arbitrarily large base64 body has no line/size ceiling (ADR-0002).
let writeFrame (out: Stream) (payload: byte[]) =
    let len = payload.Length
    let prefix = [| byte (len >>> 24); byte (len >>> 16); byte (len >>> 8); byte len |]
    out.Write(prefix, 0, 4)
    out.Write(payload, 0, payload.Length)
    out.Flush()

let private readExactly (input: Stream) (buffer: byte[]) =
    let mutable offset = 0
    let mutable eof = false
    while offset < buffer.Length && not eof do
        let n = input.Read(buffer, offset, buffer.Length - offset)
        if n = 0 then eof <- true else offset <- offset + n
    not eof

/// Blocks until a full frame arrives, or returns None once the input stream is closed.
let tryReadFrame (input: Stream) : byte[] option =
    let prefix = Array.zeroCreate<byte> 4
    if not (readExactly input prefix) then
        None
    else
        let len = (int prefix.[0] <<< 24) ||| (int prefix.[1] <<< 16) ||| (int prefix.[2] <<< 8) ||| int prefix.[3]
        let payload = Array.zeroCreate<byte> len
        if not (readExactly input payload) then None else Some payload
