# The request as sent — a truthful status line and a Request section in the viewer

Spec for v0.2 feature 1 of 3: the response viewer shows the request that was actually sent, and
the status line stops guessing at it.

## Problem Statement

**1. The status line's URL is blank for a computed URL, and wrong for a built one.**
`Protocol.extractMethodAndUrl` (`Protocol.fs:73`) is a text heuristic over the block's own source.
It splits the block text on whitespace and braces, finds the first token that is an HTTP verb, and
then takes the text between the next two double quotes. Its own doc comment admits the shape it
assumes. So:

- `GET url` where `url` is a binding yields the verb and a **blank URL**.
- `GET (baseUrl + "/items")` yields `/items`, which is not a URL anybody requested.
- A block that adds query parameters through the DSL shows none of them, because they are not in
  the string literal.

The user reads a status line that claims to describe their request and does not.

**2. Nothing shows the request at all.** The viewer shows the response status, the response headers,
and the response body. What was sent is invisible: no request headers, no request body. A user
staring at a `400 Bad Request` cannot see the JSON they posted, and cannot see whether the auth
header they set actually went out. This is the single most common thing an HTTP client shows that
we do not.

**3. It costs a companion round trip per Run to get the wrong answer.** `RunCommand.runOne` calls
`Companion.locate` (`RunCommand.fs:51`) for one reason only: to slice the block's text so the
heuristic can read it. That is a full IPC round trip whose only product is a method and a URL that
are frequently wrong, on a path where `Companion.run` is about to locate the same blocks again.

## Solution

**The request the user sees is the request FsHttp actually built.** Its method, URL, and headers are
read off the `Response` record's `requestMessage`, which is a BCL `HttpRequestMessage` and therefore
inside what ADR-0002 commits to. Its body is captured at send time, because the body — and only the
body — is gone by the time the response arrives.

The viewer gains a collapsible **Request** section above the response headers, showing the request
headers and the request body. The status line's method and URL come from the same place, so the
blank-URL bug ends.

The host-side `locate` round trip, `Protocol.extractMethodAndUrl`, and `Protocol.sliceRange` are all
deleted. The Run path gets shorter and more truthful at the same time.

## User Stories

1. As a script author whose URL is computed, I want the status line to show the URL that was
   requested, so that a blank URL does not make me doubt the Run happened.
2. As a script author who builds a URL from parts, I want to see the whole assembled URL including
   its query string, so that I can tell a wrong path from a wrong parameter.
3. As a script author debugging a `400`, I want to see the body I sent, so that I can compare it
   against what the API documents.
4. As a script author, I want to see the request headers that went out, so that I can confirm an
   auth or content-type header was really set.
5. As a script author, I want the request headers to include the ones the HTTP stack added on my
   behalf, so that what I read is what was sent and not only what I wrote.
6. As a script author posting a large file, I want the Run to behave exactly as it does today, so
   that a viewer feature does not change how my upload is sent.
7. As a script author posting a large file, I want to be told the body was not captured rather than
   shown a blank section, so that I do not read the absence as "no body was sent".
8. As a script author sending JSON, I want the request body rendered as JSON, so that I can read it
   as easily as I read the response.
9. As a maintainer, I want the status line to stop depending on a text heuristic over user source,
   so that a shape nobody anticipated degrades to nothing instead of to a confident wrong answer.

## Implementation Decisions

### 1. The method, URL, and headers come from `requestMessage` on the `Response` record

`extractResponse` (`BlockRunner.fs:176`) already reflects over the `Response` record through the
`prop` helper. It gains one more read.

Measured across every FsHttp version in the cache — 13.2.0, 13.3.0, 14.5.0, 14.5.1, 15.0.1, 15.0.3 —
the `Response` record carries `requestMessage : System.Net.Http.HttpRequestMessage` in every one, at
the same name. `HttpRequestMessage` is a BCL type, so this is the same class of read that ADR-0002
already blesses for `content` and `headers`.

- **Method**: `m.Method.ToString()`.
- **URL**: `m.RequestUri.AbsoluteUri`. Use `AbsoluteUri`, **not** `ToString()`: `ToString()` decodes
  percent-escapes, so `?q=one%20two` is displayed as `?q=one two`, which is not what went on the
  wire. `AbsoluteUri` preserves the escaping.
- **Headers**: `m.Headers`, plus `m.Content.Headers` when `m.Content` is not null. Both are readable
  after the send (measured). Join a multi-valued header with `", "`, and dedupe by name, exactly as
  the response headers already do.

`requestMessage` is never null on a successful Run, on any version or body shape measured.

**Why the headers come from here and not from the capture in Decision 4.** The capture runs before
the HTTP stack adds its own headers. Measured on the same GET: `requestMessage.Headers` carries
`Accept`, `X-Trace`, **and `Accept-Encoding`**, while the message seen at capture time carries only
what FsHttp set. `Accept-Encoding` is genuinely sent, so `requestMessage` is the more truthful
source, and the capture is used for the body alone.

### 2. The request body cannot come from `requestMessage`, and this is the ticket's dead premise

The ticket that raised this work assumed the body could be read off the same record. It cannot.
`HttpClient` disposes the request content once the send completes. Measured on 15.0.3, every body
shape, on both `requestMessage` and `originalHttpRequestMessage`:

```
content read = <THROW ObjectDisposedException: Cannot access a disposed object.
                Object name: 'System.Net.Http.StringContent'.>
```

The same throw for `ByteArrayContent` and `FormUrlEncodedContent`. The content **headers** survive —
`Content-Type` and `Content-Length` read back fine — but the bytes are gone.

### 3. FsHttp's own `request` domain record is not the way out

The `Response` record also carries `request : FsHttp.Domain.Request`, which holds the body before it
is sent. Reading it is rejected, because it is exactly the version-fragile reflection ADR-0002
exists to forbid. Measured:

| | 13.2.0 | 15.0.3 |
|---|---|---|
| `url` | `FsHttpUrl` | **absent** |
| `header` | `Header` | `Header` |
| `content` | `RequestContent` | **`BodyContent`** |
| `config` | `Config` | `Config` |
| `printHint` | absent | `PrintHint` |

The field that carries the URL was removed outright, and the type of the field that carries the body
was renamed, inside the range ADR-0002 claims. Any code reading this record would break on a version
bump with no compiler to catch it.

### 4. The body is captured by an `httpMessageTransformers` entry, installed at invocation time

`Config` carries `httpMessageTransformers : (HttpRequestMessage -> HttpRequestMessage) list`. The
field is present, identically named, and identically typed on 13.2.0 and 15.0.3 (measured), and its
type is BCL on both sides of the arrow. A transformer runs while the body is still alive.

It is installed at the **invocation-time `Config.update`** that #96 introduces — the same call site
that carries the response-reading guard and, after #98, the request timeout. No new seam:

```fsharp
context
|> Config.update (fun c ->
    { c with httpMessageTransformers = captureRequest :: c.httpMessageTransformers })
```

Measured working end to end on **13.2.0, 14.5.1 and 15.0.3**, against a local `HttpListener` that
echoes back the byte count it received.

**The invocation reaches `captureRequest` by reflection.** The fragment above names the function
directly, but the invocation runs inside FSI, and FSI cannot close over a companion CLR value. So the
companion addendum declares one binding that finds `Companion.RequestCapture.captureRequest` in the
loaded assemblies, and the invocation prepends that binding. The lookup is total: a lookup that finds
nothing, or that throws, binds `id` instead. A Run must not fail because the capture could not be
found, so the cost of a failed lookup is the body display alone.

### 5. The capture never forces a streaming body into memory, and never touches a nested part

This is the rule, and both halves of it were found by measurement.

**Reading is buffering.** `HttpContent.ReadAsByteArrayAsync()` buffers internally. There is no such
thing as a read that leaves a stream streaming. So the decision must be made from the content's
**runtime type, before any read**:

```fsharp
let rec private isStreamed (c: HttpContent) : bool =
    match c with
    | :? StreamContent -> true
    | :? MultipartContent as mp -> mp |> Seq.exists isStreamed
    | _ -> false
```

`StreamContent` is what FsHttp produces for both `body stream` and `body file` (measured). A
`filePart` inside a multipart appears as a nested `StreamContent`, which is why the test recurses.

**Content length is not a usable signal.** A seekable stream reports a length like any buffered
content (`ContentLength = 25` for a `MemoryStream`, `29` for a file). Only a non-seekable stream
reports `<null>`. The type test is the signal; the length is not.

**Never read a nested part's headers.** Reading `ContentLength` on a part *materializes* a
`Content-Length` header into that part, which is then serialized into the multipart payload.
Measured on a one-text-part multipart, by what the server received:

| touch applied in the transformer | bytes the server received |
|---|---|
| no touch at all | 181 |
| read the top-level content type | 181 |
| read the top-level `ContentLength` | 181 |
| enumerate the parts | 181 |
| enumerate the parts and read each part's **type** | 181 |
| enumerate the parts and read each part's **`ContentLength`** | **200** |
| `ReadAsByteArrayAsync` on the whole content | 181 |

In a second arrangement the same read failed the send outright:
`HttpRequestException: Unable to write content to request stream; content would exceed
Content-Length.` — the top-level length had been computed before the parts grew.

So: **type tests only inside a multipart.** Top-level content headers are safe and are read
normally.

**Verification.** The rule above was run against eight body shapes on 13.2.0, 14.5.1 and 15.0.3,
with each request sent twice — once with no capture at all, once with the capture — and the server's
received byte count compared. All three versions, all eight shapes: **send unchanged**.

| shape | captured? |
|---|---|
| GET, no body | no body |
| `json` | captured |
| `binary` | captured |
| `formUrlEncoded` | captured |
| `multipart` with a text part | captured |
| `multipart` with a **file** part | not captured — streamed |
| `body file` | not captured — streamed |
| `body stream` | not captured — streamed |

### 6. A one-megabyte cap, checked before any read

A captured body is base64-encoded onto the wire and rendered in a webview, and the renderer costs
roughly 0.1 ms per KB. An unbounded capture would let a 50 MB in-memory body stall the panel and
double the companion's peak memory.

Cap the capture at **1 MB (1_048_576 bytes)**, decided from the top-level
`Content-Length` **before** any read, so an oversized body is never buffered at all. A content whose
length is absent is not captured either, and it carries its own reason in Decision 8: a body that is
not streamed must never tell the user that it was streamed.

This is a constant, not a setting. v0.2 already adds one setting in #98, and nobody has yet asked to
see a request body larger than a megabyte.

### 7. Correlate the captured body by reference identity

A block may send more than one request; only the last `Response` is returned. "Last capture wins"
would then be a guess. It does not need to be one.

Measured on 13.2.0 and 15.0.3: **the `HttpRequestMessage` the transformer receives is the same
instance that appears on `Response.requestMessage`**, and the transformer fires exactly once per
request. So the capture stores into a
`ConditionalWeakTable<HttpRequestMessage, CapturedBody>`, and `extractResponse` looks the body up by
the `requestMessage` it already holds. Exact, and the weak table cannot leak.

A lookup that misses yields the "not captured" state of Decision 8 rather than an error. The method,
URL, and headers do not depend on the capture at all, so a miss degrades to a Request section with
no body — never to a broken status line.

### 8. The body is a three-state value, not a string

Blank must not be able to mean three different things. "No body was sent", "a body was sent and here
it is", and "a body was sent and we chose not to read it" are distinct, and the viewer says which.

```fsharp
type CapturedBody =
    | NoBody
    | Captured of bytes: byte[]
    | NotCaptured of reason: string
```

`reason` is one of four written strings, and it is shown to the user:

- `"streamed body — not captured, so that the upload is unchanged"`
- `"body too large to show (%s)"`, with the size rendered by the existing `humanSize`.
- `"body length unknown — not captured, so that the send is unchanged"`, for the absent
  `Content-Length` of Decision 6.
- `"body could not be read, so it is not shown"`, for a content that throws on read.

The last two reasons each name their own condition. One string for all four conditions would tell a
user that a body was streamed when it was not, and the viewer would state something that is false.

The capture must not throw. It runs inside the user's send, so an exception from an exotic
`HttpContent` would fail a Run that would otherwise succeed. A failed capture costs the body display
alone, and the reason above says so.

### 9. `RunOutcome.Ok` becomes records rather than a wider tuple

`Ok` is a five-field tuple today. #98 adds `requestMs`, and this spec adds five more. An eleven-field
tuple is unreadable and its positional call sites are a defect waiting to happen.

```fsharp
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

type ResponseData =
    { Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      BodyBase64: string
      RequestMs: float }

type RunOutcome =
    | Ok of request: RequestData * response: ResponseData
    | CompileError of Diagnostic list
    | RuntimeError of string
```

This is a mechanical change to `extractResponse`, `outcomeToWire`, and `wireToOutcome`, all in
`BlockRunner.fs`.

### 10. The wire shape

The `ok` envelope gains a `request` object. Both channels that carry it — `RequestHandler`'s
response to the host, and the `--worker` child's frame — go through `outcomeToWire` and
`wireToOutcome`, so neither can drift.

```json
{
  "tag": "ok",
  "status": 200,
  "reason": "OK",
  "headers": { },
  "contentType": "application/json",
  "bodyBase64": "…",
  "requestMs": 42.0,
  "request": {
    "method": "POST",
    "url": "https://api.example.com/items?q=one%20two",
    "headers": { "Content-Type": "application/json" },
    "bodyState": "captured",
    "bodyBase64": "…",
    "bodyReason": ""
  }
}
```

`bodyState` is `"none"`, `"captured"`, or `"notCaptured"`. `bodyBase64` is populated only for
`"captured"`, `bodyReason` only for `"notCaptured"`.

### 11. The host stops locating, and two Protocol functions are deleted

`RunCommand.runOne` loses the `Companion.locate` call and the `method, url` computation that follows
it. Both `method` and `url` now arrive on the run result. This removes an IPC round trip from every
Run.

Delete, with their tests:

- `Protocol.extractMethodAndUrl` (`Protocol.fs:73`) and its four tests in `ProtocolTests.fs:90`.
- `Protocol.sliceRange` (`Protocol.fs:49`). Its doc comment states its only purpose is the status
  line's method and URL, and `RunCommand.fs:55` is its only caller in `src`.

Both deletions were checked against the specs already filed: neither #95, #96, nor #97 mentions
either function, and #97's classification lives in the companion's own `BlockLocator`. Note that the
companion has its own `BlockLocator.sliceRange`, which is a different function and stays.

### 12. The renderer gains a Request section, and the envelope stops carrying method and URL twice

`ResponseEnvelope` currently carries `Method` and `Url` beside the response fields. They move onto a
request record, so there is one source for them:

```fsharp
type RequestView =
    { Method: string
      Url: string
      Headers: (string * string) list
      ContentType: string
      Body: CapturedBody }

type ResponseEnvelope =
    { Request: RequestView
      Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      Body: byte[]
      TotalMs: float }
```

`renderStatusLine` reads `env.Request.Method` and `env.Request.Url`. The rest of it is unchanged.

**The Request section** is a `<details>` element that mirrors `renderHeaders`, placed **between the
status line and the response headers**, and **collapsed by default** — the response is what the user
came for. Its summary reads `Request` when there is no body, and `Request (2.1 KB)` when there is
one.

Inside: the header rows, using the existing `header-row`, `header-name`, and `header-value` classes,
and then the body.

**The request body renders as JSON, text, or hex only.** Factor the existing content dispatch into
`renderContent (contentType: string) (body: byte[])`, which `renderBody` then calls, and have the
request body call it with the **image and HTML branches excluded**. A request body is data that was
sent, not a document to preview, and this keeps the sandboxed-iframe surface exactly where it is
today.

For `NoBody` the body area is omitted entirely. For `NotCaptured reason` it renders the reason in the
existing `.binary-note` italic style, which is what that class is already for.

The section needs one new style rule, `.request { margin-bottom: 12px; }`, plus reuse of the
`.headers` rules. Add it to `responseStyles` in `ResponseViewer.fs`.

## Testing Decisions

### Seam 1: the companion, against a local server

The capture rule is where the risk is, and it is testable without FSI. Drive `captureRequest`
directly with hand-built `HttpRequestMessage` values, and separately run real sends through
`TestServer`.

1. `isStreamed` returns false for `StringContent`, `ByteArrayContent`, `FormUrlEncodedContent`, and
   a `MultipartFormDataContent` of text parts.
2. `isStreamed` returns true for `StreamContent`, and for a `MultipartFormDataContent` containing a
   `StreamContent` part.
3. **The send is unchanged by the capture.** For each of the eight shapes in Decision 5, send twice —
   once plain, once with the capture installed — and assert the server received identical bytes.
   This is the regression test for the nested-part hazard, and it must fail if anybody makes the
   capture read a part's headers.
4. A body at the cap is captured; a body over the cap yields `NotCaptured`, and the server still
   receives it in full.
5. The correlation returns the right body when a block sends two requests with different bodies.
6. `requestMessage` yields the absolute URL with percent-escapes intact for a computed URL with a
   query string — the defect this spec exists to fix.

### Seam 2: the renderer

The renderer is pure, so these are canned-envelope assertions in the existing suite.

7. A `Captured` JSON body renders a JSON tree inside the Request section.
8. A `Captured` binary body renders a hex dump.
9. A `NotCaptured` body renders its reason and no body element.
10. `NoBody` renders headers and no body area.
11. The status line shows the method and URL from `env.Request`.
12. The Request section renders before the response headers and is collapsed by default.

### Seam 3: the host's pure part

13. `parseRunResult` reads the `request` object from an `ok` frame, in all three `bodyState` values.
14. A frame missing the `request` object is a `RunProtocolError` and not a crash.

### What is not tested here

`RunCommand.runOne` is Fable and VSCode interop with no suite. The deletion of the `locate` call is
verified by hand and recorded in the PR: a Run against a computed URL must show the real URL, and
the response viewer must still show the response.

## Out of Scope

- **Copy as curl.** The obvious follow-on once the request is on the envelope, and a v0.3 item by
  the feature-cap ticket's decision.
- **Editing or replaying the request** from the viewer. This shows what was sent. It does not send.
- **Showing the request for a Run that failed.** A compile error, a refusal, or a runtime error has
  no `Response`, so there is no request to read. Those paths post an error message with no status
  line today, and that is unchanged.
- **Capturing a streamed body by teeing the stream.** It is possible and it is not worth it: it would
  change how a user's upload is sent, which User Story 6 rules out.
- **Redirects.** The transformer fires once per request, and the message on the `Response` is the one
  FsHttp built. Whether a redirect chain should show every hop is a question nobody has asked.

## Further Notes

### Sequencing

This spec is written against the code **after #96 and #98**.

- **#96** introduces the invocation-time `Config.update`, which is where Decision 4 installs the
  capture. That call site does not exist before it.
- **#98** touches the same `ok` envelope and renames `ElapsedMs` to `TotalMs`. Its own spec asks for
  this one to land second, so the envelope grows once and then once again, rather than twice at the
  same time. Decision 9's `ResponseData` assumes `requestMs` is already there.

### One amendment to #98

#98's FIFO-flush decision justifies `locate` abandoning to an empty list with "it must not throw
because `runOne` awaits it before it sends". Decision 11 **deletes that await**. The abandon path is
still needed — `CodeLensProvider` remains a caller — so #98's decision stands unchanged, but its
stated reason no longer applies. Post this as a comment on #98 so whoever implements it is not
confused by a justification that has moved.

### Corrections to the ticket that raised this work

- **"read off the `Response` record through the reflection seam `extractResponse` already uses"** is
  true of the method, the URL, and the headers, and **false of the body**. Refer to Decision 2.
- The ticket did not anticipate that a body capture is needed at all, so it did not anticipate the
  streaming rule, the multipart hazard, or the cap. Those are Decisions 5 and 6, and they are the
  bulk of the risk in this spec.

### Measurements

All numbers were measured while writing this spec, against a local `HttpListener`, on the FsHttp
versions named at each point.

- `Response.requestMessage : HttpRequestMessage` is present on 13.2.0, 13.3.0, 14.5.0, 14.5.1,
  15.0.1, and 15.0.3.
- `requestMessage.Content.ReadAsStringAsync()` throws `ObjectDisposedException` after the send, for
  `StringContent`, `ByteArrayContent`, and `FormUrlEncodedContent`, on both `requestMessage` and
  `originalHttpRequestMessage`. The content headers still read.
- `Config.httpMessageTransformers` is identically named and typed on 13.2.0 and 15.0.3.
- `FsHttp.Domain.Request` lost `url` and renamed `content`'s type between 13.2.0 and 15.0.3.
- The capture rule leaves the send byte-identical across 8 body shapes on 13.2.0, 14.5.1 and 15.0.3.
- Reading a nested multipart part's `ContentLength` grows the payload from 181 to 200 bytes, and in
  another arrangement fails the send with `content would exceed Content-Length`. Reading a nested
  part's **type** is safe.
- `StreamContent` reports a non-null `ContentLength` when its stream is seekable, so length cannot
  stand in for the type test.
- The message the transformer sees is reference-identical to `Response.requestMessage`, on 13.2.0
  and 15.0.3, and the transformer fires exactly once per request.

### Provenance

The feature and its two halves come from an earlier feature-cap ticket, which chose it
as feature 1 of 3. The status line's blank URL for a computed URL was found there by source
inspection. Everything in Decisions 2 through 8 — the dead premise, the capture seam, the streaming
rule, the multipart hazard, the cap, and the correlation — was decided and measured while writing
this spec.
