# Executing one `http { }` block and getting a structured response back

Research for [issue #2](https://github.com/tw0po1nt/FsHttp.Explorer/issues/2). Charted 2026-07-15.

**Question:** how can a .NET process execute a single `http { }` block from an F# source file and
return the response as structured data — status, headers, `Content-Type`, and the raw body as
bytes — rather than as text a printer has already flattened?

## How to read this document

Every claim is tagged:

- **[verified]** — I ran it on this machine and observed the result. Reproductions are in the doc.
- **[source]** — read directly from primary source code, cited `file:line`.
- **[inferred]** — reasoning from the above, not directly observed. Treat with suspicion.

**Primary sources used:**

- FsHttp repository, commit `da0f78e9ccf55e9a20bd9abafd4b6c3f5aeb471e` (2026-03-09), version `15.0.3`
  (`Directory.Build.props:4`). Line references below are to that commit.
- `dotnet/fsharp` repository (`main`) for the F# Interactive implementation and the `fsi` object.
- Microsoft Learn / FSharp.Compiler.Service docs, cited by URL.

**Environment for all [verified] claims:** macOS (darwin 25.5.0), .NET SDK `10.0.201`,
F# Interactive `15.2.201.0` for F# 10.0, FsHttp `15.0.3`, FSharp.Compiler.Service `43.9.300`,
against a local Python test server (JSON, a 1×1 PNG, and a 5 KB body).

---

## TL;DR

The premise of the ticket — "FSI's printers flatten the response, so the bytes are gone" — is
**false in a useful way**. The flattening is not a property of FsHttp's `Response`; it's a property
of the *display layer* FSI puts on top of it. `Response` carries the live `HttpContent`, so status,
headers, `Content-Type`, and raw bytes are all reachable **[source]**.

The real problem is different and sharper: **the response body is a read-once stream by default,
and printing it destroys it — silently, with no exception** **[verified]**. That's the actual
hazard this project has to design around.

The recommended approach avoids the printer channel entirely. See
[Recommendation](#recommendation-and-the-strongest-objection-to-it).

---

## Q1. What does FsHttp's `Response` actually expose?

**Raw bytes and `Content-Type` are fully reachable. The printers do not flatten anything on the way
out — they are a separate, additive display concern.**

`Response` is a record that holds the live `HttpContent` **[source]** (`src/FsHttp/Domain.fs:270`):

```fsharp
type Response = {
    request: Request
    requestMessage: System.Net.Http.HttpRequestMessage
    content: System.Net.Http.HttpContent          // <-- the raw body, unflattened
    headers: System.Net.Http.Headers.HttpResponseHeaders
    reasonPhrase: string
    statusCode: System.Net.HttpStatusCode
    version: System.Version
    printHint: PrintHint
    originalHttpRequestMessage: System.Net.Http.HttpRequestMessage
    originalHttpResponseMessage: System.Net.Http.HttpResponseMessage
    dispose: unit -> unit
}
```

Everything the ticket asks for is present:

| Wanted | Where it lives | Note |
| --- | --- | --- |
| status | `response.statusCode` (+ `reasonPhrase`, `version`) | `Domain.fs:275-277` |
| headers | `response.headers` **and** `response.content.Headers` | **split across two collections** |
| `Content-Type` | `response.content.Headers.ContentType` | on the *content* headers, not `response.headers` |
| raw body bytes | `response.content` → `ReadAsByteArrayAsync()` | or `Response.toBytes` (fragile — see Q1b) |

Two details that will bite an implementer:

**Headers are split.** `Content-Type` and `Content-Length` are *content* headers and do **not**
appear in `response.headers`. FsHttp's own printer concatenates both collections to render the
full set (`src/FsHttp/Print.fs:125`):

```fsharp
let allHeaders = (response.headers |> Seq.toList) @ (response.content.Headers |> Seq.toList)
```

Any structured serializer must do the same. **[verified]** — a request against an endpoint sending
`X-Custom: hello` returned `["Server"; "Date"]` from `response.headers` alone; `Content-Type` was
only on `response.content.Headers`.

**`Response` is not "printed" during `send`.** Printing is `Print.Response.print : Response -> string`
(`src/FsHttp/Print.fs:171`), a pure function invoked by FSI's display layer, never by the request
pipeline. `Request.send` (`src/FsHttp/Request.fs:197`) returns the `Response` with content untouched.
**[verified]** — extracting status/`Content-Type`/bytes from a PNG response with no printer involved:

```
A/status: 200        A/contentType: image/png       A/byteCount: 69
A/base64: iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1Pe...    A/isPngMagic: true
```

So rich rendering is *not* blocked by FsHttp's design. It's blocked by whatever sits between the
block and the caller.

### Q1b. The real hazard: the body is read-once, and printing destroys it

This is the most important finding in this document, and it is not in FsHttp's docs.

FsHttp's defaults (`src/FsHttp/Defaults.fs:66,69`) are:

```fsharp
httpCompletionOption = HttpCompletionOption.ResponseHeadersRead
bufferResponseContent = false
```

so the body is a **live network stream**, not a buffer **[source]**. `Response.toText`
(`Response.fs:90`) goes through `toStringWithLengthAsync` (`Response.fs:64`), which does
`use! stream = response |> toStreamAsync` (`Response.fs:66`) — it **disposes** the stream. And
`Print.fs:130-137` (`printResponseBody`) calls exactly that. So *printing the response consumes and
closes the body*.

**[verified]** — measured behaviour, default config:

| Scenario | Result |
| --- | --- |
| `Print.Response.print r` then `Response.toBytes r` | **`0` bytes — silent, no exception** |
| `Response.toBytes r` twice, no printing | first `46`, second **`0`** |
| `Print.Response.print r` then `r.content.ReadAsByteArrayAsync()` | **throws** `InvalidOperationException` |
| `bufferResponseContent = true`, print, then `ReadAsByteArrayAsync()` | **`46` — works** |
| `bufferResponseContent = true`, print, then `Response.toBytes` | **throws** — `Cannot access a closed Stream` |

Two lessons, both counter-intuitive:

1. **`Response.toBytes` is the wrong API for this project.** It routes through
   `content.ReadAsStreamAsync()` (`Response.fs:30`), which caches a single stream instance; a second
   read starts at EOF and returns `0` bytes. Buffering does **not** save it (row 5 above) — the
   printer disposed the cached stream. Use **`response.content.ReadAsByteArrayAsync()`**, which
   serializes from the buffer and is repeatable.
2. **Set `bufferResponseContent = true`.** With buffering on, `ReadAsByteArrayAsync()` is
   order-independent and survives an arbitrary number of readers, including a printer that ran first.

The silent `0`-byte case is the dangerous one: an "empty response" bug with no exception to trace.
**[inferred]** this is the single most likely source of confusing bugs in v0.1.

Buffering can be turned on **globally**, so the user's `http { }` block needs no modification
(`src/FsHttp/GlobalConfig.fs:23-24`, `src/FsHttp/Dsl.fs:462-464`) **[verified]**:

```fsharp
GlobalConfig.set (
    GlobalConfig.defaults |> Config.update (fun c -> { c with bufferResponseContent = true }))
```

Cost: the whole body is held in memory. Fine for an explorer UI that intends to render it anyway;
wrong for a multi-GB download. **[inferred]** v0.1 should buffer unconditionally and revisit only if
someone complains.

---

## Q2. How is FsHttp's printer mechanism extended? Could a custom printer emit structured JSON?

**Yes, mechanically — and I got it working — but it's the wrong channel. Don't build on it.**

FsHttp has no printer extension point of its own. It registers printers on the standard FSI `fsi`
object, by **reflection**, to avoid a hard FCS dependency (`src/FsHttp/FsiInit.fs:79-95`) **[source]**:

```fsharp
let addPrinter (f: 'a -> string) =
    let addPrinterMethod = t.GetMethod("AddPrinter").MakeGenericMethod([| typeof<'a> |])
    addPrinterMethod.Invoke(fsiInstance, [| f |]) |> ignore
let responsePrinter (r: Response) = printSafe (fun () -> Response.print r)
do addPrinter responsePrinter
```

The mechanism is FSI's own, documented in `dotnet/fsharp` at
`src/FSharp.Compiler.Interactive.Settings/fsiaux.fsi:53-57` **[source]**:

```fsharp
/// Register a printer that controls the output of the interactive session.
member AddPrinter: ('T -> string) -> unit
/// Register a print transformer that controls the output of the interactive session.
member AddPrintTransformer: ('T -> obj) -> unit
```

**A printer must return a `string`.** So structured JSON with a base64 body is possible only by
encoding it *into that string* (or by side-effecting to a file/socket and returning `""`).

**Resolution order — last registered wins.** `AddPrinter` *prepends*
(`fsiaux.fs:153-154`): `addedPrinters <- Choice1Of2(...) :: addedPrinters` **[source]**, and
`fsi.fs:631` iterates `for x in fsi.AddedPrinters` building intercepts in that order, first match
winning **[source]**.

**The ordering trap.** FsHttp registers its printer **lazily**, via `FsiInit.init ()` called from
`HeaderContext.create ()` (`src/FsHttp/Dsl.fs:31`) — i.e. on the **first `http { }` construction**,
not at `open FsHttp`. So a printer registered in a prelude loses to FsHttp's, which registers later
and lands in front of it. **[verified]**: registering a custom `Response` printer in a `--use` prelude
had **no effect** — FsHttp's pretty-printed JSON still won. Forcing FsHttp's init first (by building
a throwaway `http { }` context in the prelude) *then* registering made the custom printer win:

```
> val r: Response =
  <<<CUSTOM status=200 ct=application/json; charset=utf-8 b64=eyJuYW1lIjogImZzaHR0cCIsI...>>>
```

That works — and it is exactly the kind of load-bearing ordering hack that breaks on an FsHttp
upgrade.

Two further findings, both **[verified]**:

- **Long base64 is *not* wrapped or truncated.** I feared FSI's layout engine (`wordL`) would break
  the line at `PrintWidth`, or `PrintLength` would truncate it. A 5 KB body produced a **6730-char
  single line**, intact. The printer text is emitted as one atomic word.
- **The output is still framed by FSI**, prefixed with `val r: Response =` and indented two spaces —
  so a caller must strip FSI's chrome regardless.

**Verdict:** technically viable, strategically bad. A printer is a *display* hook. It only fires when
FSI decides to echo a value, is suppressed entirely by `--quiet`, never fires if the user's block ends
in `|> ignore`, depends on registration order against a lazy competitor, and shares a channel with
arbitrary user output. **[inferred]** every one of those is a failure mode a user would experience as
"the extension randomly doesn't work."

---

## Q3. Can `dotnet fsi` be driven programmatically, and can a caller distinguish the result?

**Yes to driving it. "Distinguishing the result" is where it gets uncomfortable.**

Verified against `dotnet fsi --help` on the installed SDK **[verified]**, consistent with the
[F# Interactive options reference](https://learn.microsoft.com/dotnet/fsharp/language-reference/fsharp-interactive-options):

| Option | Behaviour |
| --- | --- |
| `--use:<file>` | run a file on startup as initial input — the prelude hook |
| `--exec` | exit fsi after running the script — one-shot mode |
| `--quiet` | "Suppress fsi writing to stdout" — kills the `val x = ...` echo **and printer output** |
| `--nologo` | suppress the banner |

**Driving over stdin works and state is retained.** Submissions are terminated by `;;`
([Learn: F# Interactive](https://learn.microsoft.com/en-us/dotnet/fsharp/tools/fsharp-interactive/),
which also states: *"Code entered in the same session has access to any constructs entered
previously"*). **[verified]** — piping two submissions, the second saw the first's binding:

```
SUB1 done
SUB2 sees: http://127.0.0.1:8777/json
```

**A full working prototype.** I built the whole thing: a long-lived `dotnet fsi --use:prelude.fsx`
driven over stdin, where the prelude disables FsHttp's debug logs, turns on global buffering, and
defines an `exec` that prints sentinel-delimited JSON with a base64 body. **[verified]**:

```
SUB1 status: 200 | contentType: application/json; charset=utf-8
SUB1 body: {"name": "fshttp", "nested": {"a": [1, 2, 3]}}
SUB1 X-Custom header: hello
SUB1 raw stdout had noise: True          <-- user's own printfn coexisted; sentinels still parsed
SUB2 status: 200 | contentType: image/png | bytes: 69 | isPNG: True
SUB2 reused binding from SUB1: True      <-- state retention across submissions
```

So: it works, binary round-trips, and state persists. **But the separation is by convention, not by
construction:**

- **stdout is a shared channel.** The user's script prints into the same pipe as the protocol. A
  sentinel is a *convention*; a script printing the sentinel string breaks it. Mitigable with a
  per-session random nonce, but it remains parsing, not typing.
- **FsHttp pollutes stdout by default in FSI.** `FsiInit` enables debug logging on init
  (`FsiInit.fs:73-77`), producing `Sending request GET ... ` / `Download finished.` lines
  **[verified]**. `FsHttp.Fsi.disableDebugLogs()` (`src/FsHttp/Fsi.fs:6`) silences it.
- **`--quiet` and printers conflict.** `--quiet` suppresses the display channel, so a printer-based
  design *cannot* use `--quiet`, and must then filter FSI's `val`/prompt chrome instead.
- **Errors arrive as text** to be scraped, with no structured ranges.

**Measured costs [verified]** (same machine; local server, so ~0 network time):

| Path | Time |
| --- | --- |
| bare `dotnet fsi --exec` (no nuget, no FsHttp) — startup floor | 0.39 s |
| `--exec` one-shot incl. FsHttp via cached `#r "nuget:"` | 0.55 s |
| same, forcing re-resolution (`--clearResultsCache`) | 1.10 s |
| **long-lived session, per subsequent request** | **0.004–0.005 s** |

A live session is ~100× cheaper per request than re-launching. **[inferred]** a per-keystroke-ish
"run this block" UX wants a persistent session; a 0.55 s one-shot would feel sluggish but not fatal.
Caveat: the very first run on a machine with an empty NuGet cache must download the package over the
network — not measured, and much slower.

---

## Q4. What would a companion .NET process cost instead?

**The framing in the ticket contains a trap worth naming.**

"A companion .NET process that references FsHttp as a library and runs requests directly" cannot
work as literally stated. An `http { }` block **is F# code**, not data. Its contents are arbitrary
expressions — `http { GET (baseUrl + "/users"); Authorization token }` references bindings from
elsewhere in the script. To "run it directly" a companion would have to *parse and interpret F#*.
Restricting to literal-only blocks (no variables, no helpers) would break real scripts and defeat
the point of FsHttp being an F# DSL. **[inferred]**

So the honest choice isn't "FSI vs. no FSI". It's **which F# evaluator, and where it runs**:

1. **Shell out to `dotnet fsi`** and talk over stdin/stdout (Q3).
2. **Host the evaluator in-process** via FSharp.Compiler.Service's `FsiEvaluationSession` (Q5) —
   still FSI, but as a *library*, with the result returned as a **typed object** instead of text.

Option 2 is the one the ticket doesn't consider, and it's the strongest. It keeps everything FSI
gives you (surrounding bindings, `#r`/`#load`, the user's real script semantics) while giving up
nothing — because the response never becomes text at all.

**What a companion buys** (all **[verified]**, see Q5): the typed `Response` in-process, no printer,
no stdout parsing, no sentinels, structured compile diagnostics, and complete control over the user's
console output.

**What it costs:**

- **~35 MB on disk / 18 MB main assembly** for FSharp.Compiler.Service `43.9.300` **[verified]** —
  a real bundle cost for a VSCode extension. Shelling out to `dotnet fsi` costs 0 extra bytes since
  it ships with the SDK the user already needs.
- **A second process and an IPC channel to maintain.** Note this is unavoidable *anyway*: the
  extension is F#-compiled-with-Fable, so it runs as JavaScript in the VSCode extension host and
  **cannot host a .NET evaluator in-process** regardless. Either way there's a .NET child process.
  The only question is what protocol crosses the boundary.
- **An assembly-identity hazard** — see the version-skew finding in Q5, which is the sharpest thing
  I found.

---

## Q5. Does FSI retain state, and can a block be evaluated where earlier bindings exist?

**Yes for both `dotnet fsi` and `FsiEvaluationSession`, and this is the load-bearing capability for
the whole feature.**

For `dotnet fsi`, see Q3 — verified via stdin, and stated in the
[Learn docs](https://learn.microsoft.com/en-us/dotnet/fsharp/tools/fsharp-interactive/).

For **FSharp.Compiler.Service**, the
[FCS interactive docs](https://fsharp.github.io/fsharp-compiler-docs/fcs/interactive.html) give:

```fsharp
FsiEvaluationSession.Create(fsiConfig, argv, inReader, outWriter, errorWriter, ?collectible)
member EvalExpression: code: string -> FsiValue option
member EvalInteraction: code: string * ?cancellationToken -> unit
member EvalExpressionNonThrowing: code: string -> Choice<FsiValue option,exn> * FSharpDiagnostic array
```

with state retained across evaluations, and `FsiValue.ReflectionValue : obj` giving the result.

**This is the finding that should change the design.** `EvalExpression` hands back the **typed
`Response` object**. No printer. No serialization. No stdout. **[verified]** — evaluating a binding,
then an `http { }` block referencing it:

```
FCS/ReflectionType: FsHttp.Domain+Response
FCS/sessionAsmLocation: ~/.nuget/packages/fshttp/15.0.3/lib/net6.0/FsHttp.dll
FCS/hostAsmLocation:    ~/.nuget/packages/fshttp/15.0.3/lib/net6.0/FsHttp.dll
FCS/sameAssembly: true
FCS/status=200 ct=image/png bytes=69 isPNG=true
```

### The version-skew trap (the sharpest finding)

To *cast* `ReflectionValue :?> FsHttp.Domain.Response`, the host must reference FsHttp. **Doing so is
a bug.** **[verified]** — with the host referencing FsHttp `15.0.3` and the user's script asking for
`14.5.0`:

```
SKEW/sessionAsm: ~/.nuget/packages/fshttp/15.0.3/lib/net6.0/FsHttp.dll   <-- NOT 14.5.0
SKEW/sameAssembly: true
SKEW/CAST OK
```

The user's pin was **silently overridden** by the host's already-loaded assembly. The script says
`14.5.0`; it runs `15.0.3`. Removing the host's reference restores correct behaviour **[verified]**:

```
NOSKEW/sessionAsmLocation: ~/.nuget/packages/fshttp/14.5.0/lib/net6.0/FsHttp.dll
NOSKEW/sessionAsmVersion: 14.5.0.0
NOSKEW/reflection: status=OK ct=application/json; charset=utf-8 bytes=46
```

**Therefore: the host must not statically reference FsHttp.** Extract by reflection instead —
version-agnostic, and honours whatever the user pinned **[verified]**:

```fsharp
let t = v.ReflectionType
let status  = t.GetProperty("statusCode").GetValue(v.ReflectionValue)          // HttpStatusCode
let content = t.GetProperty("content").GetValue(v.ReflectionValue)
              :?> System.Net.Http.HttpContent                                   // BCL type - always shared
let bytes   = content.ReadAsByteArrayAsync().Result
let ctype   = content.Headers.ContentType
```

This works because `HttpContent` is a **BCL type**, shared across every FsHttp version, so the
interesting payload (bytes + `Content-Type`) crosses the boundary on a stable, version-proof type.
Only the property *names* (`statusCode`, `content`) are coupled — public record fields, stable across
FsHttp 13–15 **[inferred]** from the release notes (`Directory.Build.props`), which record no renames
of these fields.

### The sentinel problem dissolves

**[verified]** — redirecting `Console.SetOut` around evaluation captures the user's output into a
private buffer, completely separate from the result, *even when the user deliberately prints the
sentinel*:

```
CAPTURE/userStdoutCaptured: USER SCRIPT NOISE: <<<FSHTTP:BEGIN>>> even a sentinel collision!
CAPTURE/structuredResult: status=OK ct=application/json; charset=utf-8 bytes=46
CAPTURE/channelsAreSeparate: true (response came back as a value, not via stdout)
```

The response is a return value; user output is captured out-of-band and can be shipped to the UI as
its own field. There is no channel to collide on. (Note **[verified]**: `printfn` inside the session
writes to the *host's real* `Console.Out`, not to FCS's `outWriter` — `outWriter` only receives FSI's
own echo and diagnostics. Capturing user output **requires** `Console.SetOut`; this surprised me and
would be easy to get wrong.)

### Errors come back structured

**[verified]**, using `EvalExpressionNonThrowing`:

| Case | Exception | Diagnostics |
| --- | --- | --- |
| syntax error | `FsiCompilationException` | 2 — `Error: Identifiers followed by '#' are reserved` |
| type error | `FsiCompilationException` | 1 — `Error: This expression was expected to have typ...` |
| **network error** (connection refused) | `AggregateException` → root `System.Net.Sockets.SocketException` | **0** |
| success | — | 0, value returned |

Compile errors carry `FSharpDiagnostic[]` (severity, message, **source ranges** — mappable to VSCode
squigglies); runtime errors surface as real exceptions with `diags = 0`. The two are cleanly
distinguishable. The `dotnet fsi` path would have to recover this by scraping text.

Use `EvalExpressionNonThrowing`, not `EvalExpression`: the throwing variant wrapped *both* a network
failure and a syntax error as `FsiCompilationException` in my first attempt, losing the distinction.

**Measured [verified]** (from inside a warm host, so these *understate* a cold start — FCS was
already loaded and JIT'd; add ~0.4 s for real process startup):

```
sessionCreate: 67ms   nugetRef: 18ms   open+init: 12ms
firstRequest: 36ms    subsequentRequest: 5-7ms
```

### Where the block text comes from

Out of scope for this ticket, but a hard dependency **[inferred]**: `EvalExpression` needs the block
as an **expression**. The extension must extract the block's text and ensure the preceding file
context is evaluated first (via `EvalInteraction`). Edge cases: a block written as
`let r = http { ... }` is not an expression; a block already ending in `|> Request.send` must not have
`send` appended twice. Extraction and normalisation is its own problem.

---

## Recommendation and the strongest objection to it

### Recommendation

**Build a companion .NET process that hosts `FsiEvaluationSession` (FCS) and returns the response as
a typed object, extracted by reflection. Do not use the printer channel. Do not scrape stdout.**

Concretely:

1. **Companion .NET process**, long-lived, one `FsiEvaluationSession` per open script. The Fable/JS
   extension talks to it over a JSON protocol on its *own* stdio — the extension controls both ends,
   so no user code shares that channel.
2. **The host must NOT reference FsHttp.** Extract via reflection on `FsiValue.ReflectionValue`
   (`statusCode`, `content`), read bytes via `HttpContent.ReadAsByteArrayAsync()`. Version-agnostic,
   honours the user's `#r "nuget: FsHttp, x.y.z"` pin.
3. **Prelude the session** with `FsHttp.Fsi.disableDebugLogs()` and global
   `bufferResponseContent = true`, so the body survives multiple reads and the user's block needs no
   modification.
4. **Evaluate preceding file context with `EvalInteraction`, the target block with
   `EvalExpressionNonThrowing`.** Compile errors → `FSharpDiagnostic[]` → VSCode squigglies. Runtime
   errors → exception with `diags = 0`.
5. **Capture user output with `Console.SetOut`** around evaluation; ship it as a separate field.
6. **Merge `response.headers` and `response.content.Headers`** when serializing.

Why this and not the `dotnet fsi` + sentinel protocol, which I also got fully working: the sentinel
design is correct *by convention* — it relies on the user's script not colliding with a magic string,
on FSI chrome staying stable, on debug logs staying off, and on text-scraped errors. The FCS design is
correct *by construction*: the response is a value, so there is no channel to collide on and no
parsing to get wrong. Both were ~5 ms/request warm, so this isn't a performance argument — it's a
"how many weird bug reports will v0.1 generate" argument.

### The strongest objection to it

**It's ~35 MB of FSharp.Compiler.Service and a bespoke process to build and maintain, to solve a
problem the 60-line sentinel prototype already solved — and I have the working prototype to prove
it.** For a v0.1 whose *entire purpose* is to get a reaction in the F# Discord, that's a lot of
plumbing before anyone has said they want the thing. The map says the renderer is the cargo; this
recommendation spends the budget on the transport instead. The sentinel design's failure modes are
real but *unlikely* (users rarely print `<<<FSHTTP:BEGIN>>>`), and a random per-session nonce shrinks
them further. A demo that renders a JSON tree and a PNG doesn't care which transport delivered the
bytes.

**The counter-counter**, for the record: the FCS path is not obviously more work — the prototype's
prelude, sentinel framing, nonce handling, FSI-chrome filtering, and text-error-scraping are all code
too, and all of it is code you throw away when you migrate. And the version-skew and read-once-body
traps bite *both* designs equally; only the transport differs. **[inferred]**

**The honest tiebreaker:** if v0.1 is truly a throwaway demo, ship the sentinel prototype — it works
today. If any of this code is expected to survive the demo, start with FCS. That's a call about the
project's intent, not about the technology, and it's Matt's to make.

---

## Appendix: reproductions

All experiments were run against a local Python server exposing `/json` (with an `X-Custom` header),
`/png` (a 1×1 PNG), and `/big` (a 5 KB body). Key snippets are inline above. The decisive ones:

- **Body destroyed by printing** (Q1b) — `Print.Response.print r` then `Response.toBytes r` → `0`.
- **The fix** — global `bufferResponseContent = true` + `content.ReadAsByteArrayAsync()`.
- **Printer ordering trap** (Q2) — a prelude-registered printer loses until FsHttp's lazy
  `FsiInit.init ()` is forced first by constructing a throwaway `http { }`.
- **Version skew** (Q5) — host referencing FsHttp silently overrides the user's `#r` pin; removing the
  reference and using reflection restores it.
- **Channel separation** (Q5) — `Console.SetOut` captures user output even when it contains the
  sentinel.
