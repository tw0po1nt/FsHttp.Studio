module Companion.Envelope

open System.IO
open System.Text.Json

/// Framing rather than line delimiting, so a large base64 body has no line or size ceiling.
let writeFrame (out: Stream) (payload: byte[]) =
    let len = payload.Length
    let prefix = [| byte (len >>> 24); byte (len >>> 16); byte (len >>> 8); byte len |]
    out.Write(prefix, 0, 4)
    out.Write(payload, 0, payload.Length)
    out.Flush()

/// The companion writes each envelope with this encoder, and the Golden fixtures hold its bytes.
let encode (envelope: obj) : byte[] =
    JsonSerializer.SerializeToUtf8Bytes envelope

let private readExactly (input: Stream) (buffer: byte[]) =
    let mutable offset = 0
    let mutable eof = false

    while offset < buffer.Length && not eof do
        let n = input.Read(buffer, offset, buffer.Length - offset)
        if n = 0 then eof <- true else offset <- offset + n

    not eof

/// Blocks until a full frame arrives. Returns None after the input stream closes.
let tryReadFrame (input: Stream) : byte[] option =
    let prefix = Array.zeroCreate<byte> 4

    if not (readExactly input prefix) then
        None
    else
        let len =
            (int prefix.[0] <<< 24)
            ||| (int prefix.[1] <<< 16)
            ||| (int prefix.[2] <<< 8)
            ||| int prefix.[3]

        let payload = Array.zeroCreate<byte> len

        if not (readExactly input payload) then
            None
        else
            Some payload

// A missing or JSON-null string reads as "". A missing int reads as 0.

/// Returns "" when the element is JSON null.
let jsonString (e: JsonElement) : string =
    match e.GetString() with
    | null -> ""
    | s -> s

/// Returns "" when the property is absent or JSON null.
let getStringProp (name: string) (root: JsonElement) : string =
    match root.TryGetProperty name with
    | true, v -> jsonString v
    | false, _ -> ""

/// Reads an optional string property by name. Returns `None` when the property is absent,
/// JSON null, or the empty string. Those are the three shapes that mean "no value" on this wire.
let getOptionalStringProp (name: string) (root: JsonElement) : string option =
    match getStringProp name root with
    | "" -> None
    | s -> Some s

/// Returns 0 when the property is absent.
let getIntProp (name: string) (root: JsonElement) : int =
    match root.TryGetProperty name with
    | true, v -> v.GetInt32()
    | false, _ -> 0

/// Reads a float property by name. Returns 0.0 when the property is absent, which matches
/// `getIntProp`'s missing-value default. A duration is the only float on this wire, and an
/// absent one means "not measured" rather than "the frame is broken".
let getFloatProp (name: string) (root: JsonElement) : float =
    match root.TryGetProperty name with
    | true, v -> v.GetDouble()
    | false, _ -> 0.0
