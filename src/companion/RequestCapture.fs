module Companion.RequestCapture

// Captures a request body at send time, before `HttpClient` disposes the content. The body is
// the one part of the sent request that `Response.requestMessage` cannot yield after the send
// (docs/spec/0012-request-as-sent.md, Decisions 2 and 4-8). Method, URL, and headers come from
// `requestMessage` itself; this module owns the body alone.
//
// A transformer installed on `Config.httpMessageTransformers` calls `captureRequest` while the
// content is still alive. The capture is keyed by the same `HttpRequestMessage` instance that
// later appears on `Response.requestMessage`, so correlation is reference identity rather than
// "last capture wins".

open System
open System.Net.Http
open System.Runtime.CompilerServices

/// A request body as the viewer must see it. Blank must not mean "no body", "captured bytes",
/// and "we chose not to read it" at once.
type CapturedBody =
    | NoBody
    | Captured of bytes: byte[]
    | NotCaptured of reason: string

[<Literal>]
let private maxCaptureBytes = 1_048_576L

/// Shown when the content is a `StreamContent` (or a multipart that contains one). Reading
/// would force the upload into memory and change what goes on the wire.
[<Literal>]
let streamedBodyReason =
    "streamed body — not captured, so that the upload is unchanged"

/// Shown when the top-level `Content-Length` is absent on a content that is not streamed. The
/// cap of Decision 6 needs a known size before any read, so an unknown size is a refusal to
/// read. It is a separate sentence from `streamedBodyReason`, because the user must not read
/// "streamed body" about a body that is not streamed.
[<Literal>]
let unknownLengthReason =
    "body length unknown — not captured, so that the send is unchanged"

/// Shown when the read itself failed. An exotic `HttpContent` may throw on read. The capture
/// is a bystander to the Run: it reports the failure here rather than letting it reach the
/// send.
[<Literal>]
let unreadableBodyReason = "body could not be read, so it is not shown"

/// Shown when the lookup finds no entry for a request that carried content. The transformer
/// stores a body for every request it sees, so a miss means it never ran — and a request with
/// content did send a body. "No body" would be a false statement about a real one.
[<Literal>]
let uncapturedBodyReason = "body was not captured, so it is not shown"

/// Compact size text for the over-cap reason. A deliberate copy of `Renderer.humanSize`: the
/// renderer is Fable-side and this is the companion, and the two projects share no compilation
/// unit. Both must render one size the same way, so change them together.
let private humanSize (bytes: int64) : string =
    let b = float bytes

    if bytes < 1024L then sprintf "%d B" bytes
    elif b < 1024.0 * 1024.0 then sprintf "%.1f KB" (b / 1024.0)
    else sprintf "%.1f MB" (b / 1024.0 / 1024.0)

let private tooLargeReason (byteCount: int64) : string =
    sprintf "body too large to show (%s)" (humanSize byteCount)

/// True when reading this content would force a stream into memory. The decision is the
/// runtime type alone, before any header read or body read: a seekable `StreamContent` still
/// reports a `ContentLength`, so length is not the signal. Inside a multipart, only type
/// tests are safe — reading a nested part's `ContentLength` materializes a header into that
/// part and grows the payload on the wire.
let rec isStreamed (c: HttpContent) : bool =
    match c with
    | :? StreamContent -> true
    | :? MultipartContent as mp -> mp |> Seq.exists isStreamed
    | _ -> false

/// Process-wide table from the live `HttpRequestMessage` to its captured body. Weak keys so a
/// finished request does not leak. The transformer and the later lookup share this one table.
let private capturedBodies =
    ConditionalWeakTable<HttpRequestMessage, CapturedBody>()

let private store (m: HttpRequestMessage) (body: CapturedBody) = capturedBodies.AddOrUpdate(m, body)

/// Looks up the body stored for `m`. `None` is a miss: no entry was stored for this instance.
/// A miss does not throw. Callers decide the state it degrades to from the content, since only
/// a request with no content can honestly report "no body" (Decisions 7-8).
let tryGetCapturedBody (m: HttpRequestMessage) : CapturedBody option =
    match capturedBodies.TryGetValue m with
    | true, body -> Some body
    | false, _ -> None

let private captureContent (c: HttpContent) : CapturedBody =
    if isStreamed c then
        NotCaptured streamedBodyReason
    else
        match c.Headers.ContentLength |> Option.ofNullable with
        | None ->
            // Top-level length is unknown. Do not buffer: the cap check needs a known size
            // before any read (Decision 6).
            NotCaptured unknownLengthReason
        | Some len when len > maxCaptureBytes -> NotCaptured(tooLargeReason len)
        | Some _ ->
            let bytes = c.ReadAsByteArrayAsync().Result
            Captured bytes

/// `httpMessageTransformers` entry. Decides whether to read, stores the result under `m`, and
/// returns `m` unchanged so the send itself is untouched.
///
/// Nothing here may throw. The transformer runs inside the user's send, so an exception from a
/// content that refuses to be read would fail a Run that would otherwise have succeeded. A
/// failed capture costs the body display alone.
let captureRequest (m: HttpRequestMessage) : HttpRequestMessage =
    let body =
        try
            match m.Content with
            | null -> NoBody
            | c -> captureContent c
        with _ ->
            NotCaptured unreadableBodyReason

    store m body
    m
