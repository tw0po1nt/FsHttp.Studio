# Run-path robustness: bound the request, flush the pending queue, tell the truth about timing

Spec for #98, three repairs to the shipped Run path. None of them is a feature. Each one is a path
that already ships and that hangs or lies.

## Problem Statement

Three defects, found by reading the source, all on the path between a `▶ Run request` click and the
status line the user reads afterwards.

**1. A slow server holds the Run for 100 seconds.** The companion never sets a request timeout, so
the bound is whatever `HttpClient` defaults to. Measured, that is 100 seconds. The response viewer
says `Running…` for the whole of it, and the user cannot tell a slow server from a wedged
extension. Coding-standards rule 3 asks for a bounded wait on every external process. Only the
`--worker` path honors it today, at `workerTimeoutMs = 120_000`. The warm in-process path, which is
the path almost every Run takes, has no bound of its own.

**2. A companion that exits drops every Run in flight.** `Companion.fs` keeps one FIFO of pending
resolvers and dequeues one per response frame. When the child exits, `child.on ("exit", …)` sets the
state to `Stopped` and returns. Every queued resolver is dropped, unresolved. The response viewer
stays on `Running…` forever, and its counter keeps ticking. Nothing restarts the companion, so the
Run cannot recover on its own either.

**3. The timing number is mostly our own overhead.** The status line's `142 ms` is a host-side
bracket around `Companion.run`. It contains the IPC round trip, the `FsiEvaluationSession.Create`,
the whole Setup evaluation including any `#r "nuget:"` restore, and then the request. Measured on a
warm companion, a Run against an instant local server is about 140 ms total, of which the session
creation alone is about 100 ms. The user reads a number that says their request took 140 ms. Their
request took about 5 ms.

## Solution

**Bound the request at 30 seconds**, injected by the companion into the block's own configuration at
invocation time, never overriding a bound the user set. A request that passes the bound ends the Run
with a written message that names the bound and the setting, and not with `A task was canceled.`

**Flush the pending queue when the companion exits**, and refuse to enqueue anything after it has.
Each abandoned Run resolves rather than hanging, and its message tells the user the companion
stopped and how to get it back.

**Show both numbers**: `142 ms · 380 ms total`. The companion measures the invocation and puts it on
the `ok` envelope. The host keeps the total it already brackets. The user gets the request number
they wanted, and the difference between the two numbers is the extension's own cost, stated rather
than hidden.

## User Stories

1. As a script author, I want a request to a server that never answers to end in well under a
   minute, so that I can tell a slow server from a broken extension.
2. As a script author, I want the message for that case to say that we stopped waiting, so that I do
   not read `A task was canceled.` and go looking for a defect in my script.
3. As a script author who set a timeout in my own block, I want my timeout to be the one that
   applies, so that FsHttp.Studio does not quietly shorten a wait I chose.
4. As a script author with a legitimately slow API, I want to raise the bound in settings, so that a
   default meant for the common case does not block my case.
5. As a script author, I want a large response body to download inside the bound, so that the
   timeout does not cut a transfer that is making progress.
6. As a script author whose companion crashed, I want the Run in flight to end with a message, so
   that the viewer does not sit on `Running…` forever.
7. As a script author whose companion crashed, I want the next click to fail immediately with the
   same message, so that I am not left waiting on a process that is gone.
8. As a script author, I want the status line to tell me how long my request took, so that I can
   judge the API and not the extension.
9. As a script author, I want to see the extension's own overhead beside it, so that the number I am
   reading is not silently inflated.
10. As a maintainer, I want the warm in-process Run path to honor coding-standards rule 3, so that
    the rule is not true of the worker only.

## Implementation Decisions

### 1. The bound is 30 seconds, and it is a setting

`fshttpStudio.requestTimeoutMs`, default `30000`.

30 seconds is chosen against what the bound actually covers, which is narrower than the ticket that
raised this assumed. Refer to Decision 3 for the measurements. It is long enough for a slow API and
for a sizeable body over an ordinary link. It is short enough that a wedged Run clears while the user
is still looking at the panel. And it sits well below `workerTimeoutMs = 120_000`, so on the worker
path the request bound always fires first and produces the better message, instead of the worker's
`no response within 120000ms` kill.

`0` means **do not inject**. The user then gets whatever FsHttp and `HttpClient` decide, which is
today's behavior. This is the escape hatch for someone who wants the old shape back, and it is
simpler than a second `enabled` setting.

The `package.json` contribution sits beside `fshttpStudio.dotnetPath`:

```json
"fshttpStudio.requestTimeoutMs": {
  "type": "number",
  "default": 30000,
  "minimum": 0,
  "markdownDescription": "How long a Run waits for a response, in milliseconds, before it gives up. This covers the connection, the request, and the download of the response body. It does not cover the `#r \"nuget:\"` restore, which happens before the request is sent. A block that sets its own timeout keeps it. Set this to `0` to wait as long as `HttpClient` allows."
}
```

### 2. Inject the bound, never override

Apply it as an `Option.orElse`, not an assignment:

```fsharp
{ c with timeout = c.timeout |> Option.orElse (Some (TimeSpan.FromMilliseconds timeoutMs)) }
```

FsHttp's `Config.timeout` is `TimeSpan option` and defaults to `None`, so `orElse` reads exactly as
"a default, not an override". A block that carries `config_timeoutInSeconds`, and a script that calls
`GlobalConfig.set` with a timeout of its own, both keep what they chose. This was measured: with a
user timeout of 10 s and an injected 3 s, the request ran for 10 s.

This is the one place in the companion's injected configuration where the user wins. The
response-reading guard (`bufferResponseContent`, `httpCompletionOption`) overrides deliberately,
because a Run cannot read a consumed stream. A timeout carries no such requirement.

### 3. The bound rides the invocation's `Config.update`, not the `GlobalConfig.set` preamble

**This corrects the ticket that raised this work.** It asked for the timeout in the `GlobalConfig.set`
preamble at `BlockRunner.fs:55`. After the reach mechanism (#96) that preamble no longer configures
the block: `http { }` copies the global configuration **at build time**, and the block is now built
inside the Setup, before the addendum runs. #96 Decision 10 moved the response-reading guard out of
the preamble and into the invocation for exactly this reason. The timeout must travel the same way,
in the same `Config.update`, or it configures nothing.

What the bound covers, measured against a local server:

| | |
|---|---|
| Connection, request, response headers | **covered** |
| The response body download | **covered** — we force `ResponseContentRead`, so the body read is part of the send. A 6 s dribbling body ended at a 2 s bound. |
| `#r "nuget:"` restore | **not covered** — it runs during the Setup evaluation, before the invocation. Measured at 516 ms with a warm package cache. |
| A user's own infinite loop | **not covered**, knowingly. Refer to Out of Scope. |

The restore is the anchor the ticket asked us to justify the number against, and it turns out not to
bear on it at all. That removes the main reason to pick a large number.

### 4. The wire carries the bound; the host reads the setting

The companion cannot read VSCode settings. Add `timeoutMs` to the `run` envelope:

```
{ "tag": "run", "source": …, "blockIndex": …, "timeoutMs": 30000 }
```

The host reads `fshttpStudio.requestTimeoutMs` per Run, in the same shape as `configuredDotnetPath`.
Reading it per Run, rather than at activation, means a change to the setting applies to the next
click with no window reload.

`runInWorker`'s child payload grows the same field, and `runWorker` in `Program.fs` reads it, so both
Run paths carry the same bound. `getIntProp` already exists for both readers. A payload with no
`timeoutMs` means "do not inject", which keeps an older host talking to a newer companion honest.

### 5. A timeout is a Runtime error with a written message

The exception that surfaces is `TaskCanceledException` with the message `A task was canceled.` and
no inner exception. Shipping that is not a fix.

The companion never passes a cancellation token of its own, so a cancellation out of the invocation
is the bound firing. Recognize it there:

- Unwrap `AggregateException` first. A refused connection arrives wrapped
  (`AggregateException` → `HttpRequestException` → `SocketException`); the timeout arrives bare.
  Both shapes were measured.
- Any `OperationCanceledException` in that chain is the timeout. `TaskCanceledException` derives
  from it, so one test covers both.

The message:

```
No response within 30000 ms. FsHttp.Studio stopped waiting.
Raise fshttpStudio.requestTimeoutMs to wait longer, or set it to 0 to wait as long as HttpClient allows.
```

The number in the message is the bound that was actually applied, so a user who raised the setting
reads their own number back.

**No new Run outcome, and no vocabulary change.** `CONTEXT.md`'s **Runtime error** is "a Run that
produced no response, because the user's code or the host failed", and it lists a refused connection
as an example. A timeout is the same kind of thing. This spec adds no fifth outcome beside the fourth
that #97 introduced, and no `CONTEXT.md` edit.

One case belongs to the user, not to us: if the user set their own timeout, the bound that fired was
theirs. The message is the same, because the number in it is the applied bound, and pointing at our
setting is still the correct advice for someone who wants to wait longer.

### 6. Flush the pending queue, and close it

The naive fix — resolve every queued resolver with a `runtimeError` object — breaks `locate`. The
FIFO holds untyped `obj -> unit` resolvers, and `locate`'s resolver does `unbox (json?ranges)`. Hand
it an error object and it throws inside a continuation that nobody catches.

Give each pending entry an abandon path of its own:

```fsharp
type private Pending =
    { Resolve: obj -> unit
      Abandon: unit -> unit }
```

- `locate` abandons to an **empty list**. The companion is gone, so there are no blocks to show, and
  the lenses disappear. That is the honest degraded state, and it is what `CodeLensProvider.setReady
  false` produces anyway. It must not throw, because `runOne` awaits it before it ever sends the Run.
- `run` abandons to `RunProtocolError` with the message below.

Flush on **both** terminal paths — the `exit` handler and the `error` handler — through one function,
because a spawn that fails with `ENOENT` leaves the same queue behind.

After the flush, mark the handle closed and **abandon immediately on `send`** rather than enqueueing.
Without this the fix is half a fix: `runOne` awaits `locate`, then sends the `run`, and if the exit
landed in between, the new entry is queued onto a queue that will never be flushed again. Writing to
a dead `stdin` does not produce a response.

The message:

```
The FsHttp.Studio companion stopped. Reload the window to start it again.
```

Reload is the accurate instruction: nothing restarts the companion today. Refer to Out of Scope.

### 7. Honest timing: the companion measures the invocation, the host keeps the total

The companion brackets the invocation — the single `EvalExpressionNonThrowing` that sends the
request — and puts the result on the `ok` envelope as `requestMs`. The host keeps its existing
`Date.now()` bracket around `Companion.run` and renames it to what it is.

Wire and types, in order:

| Where | Change |
|---|---|
| `BlockRunner.RunOutcome.Ok` | gains `requestMs: float` |
| `BlockRunner.outcomeToWire` / `wireToOutcome` | `requestMs` on the `ok` shape, so the worker path carries it too |
| `Protocol.RunResult.RunOk` | gains `requestMs: float` |
| `RunCommand.resultMessage` | `requestMs` beside the existing number |
| `Renderer.ResponseEnvelope` | `ElapsedMs` becomes `TotalMs`; add `RequestMs` |

`ElapsedMs` must be **renamed**, not joined by a second field. The name is what let it pass for a
request time; leaving it in place beside `RequestMs` invites the next reader to make the same
mistake. `requestMs` names the HTTP request and not the Block, so it does not collide with the
glossary's `_Avoid_` list for **Block**.

Only `Ok` carries a duration. A compile error and a runtime error render no status line.

The status line renders two spans:

```
GET  https://pokeapi.co/api/v2/pokemon/ditto   200 OK   142 ms · 380 ms total   1.2 KB
```

`status-time` keeps `142 ms`. A new `status-total` span carries `380 ms total`, and joins the
existing `.status-time, .status-size` CSS rule, which already gives the muted color and tabular
numerals. The separator is a `·` inside the total span, so the two numbers do not drift apart when
the line wraps.

### 8. What the first number contains, and why we ship it anyway

The invocation bracket is not a pure HTTP measurement. It contains the FSI compilation of the
invocation expression, and on the first Run of a companion process it contains the JIT of FsHttp's
send path. Measured, fresh session per Run, against an instant local server:

| | invocation bracket |
|---|---|
| First Run after the companion starts | **73 ms** |
| Warm Runs after that | **6.7 – 21 ms** |
| Against a 200 ms server, warm | 216 – 224 ms |

So the number overstates a real request by roughly 7 to 20 ms once warm, and by about 70 ms on the
first Run of a process. Against the 100 to 500 ms that a real API costs, warm error is under 10%.
Against the host-side bracket it replaces, which overstates by 130 ms and more, it is a large
improvement.

We ship the bracket and record the limit here, rather than timing inside the evaluated code. Timing
inside would mean returning a tuple of the duration and the `Response` from the invocation, and
reflecting over the tuple — which is precisely the shape #96 Decision 10 finished simplifying. The
accuracy bought does not pay for re-complicating the seam that the reach mechanism just cleaned.

The `total` number is deliberately kept, and not replaced. It is the only place a user or a
maintainer ever sees the session-creation cost, which the benchmarking research put at about 65% of a
warm Run. That measurement is the evidence the session-model work will argue from, and a request-only
status line would throw it away.

## Testing Decisions

### Seam 1: the companion, against `TestServer`

`BlockRunnerTests` drives `run` directly. `TestServer` already has the handlers this needs, plus one
new one.

1. **A hanging server ends the Run at the bound.** Add a handler that never answers. Run with a short
   bound, such as 1500 ms. Assert `RuntimeError`, assert the elapsed time is near the bound and far
   below `workerTimeoutMs`, and assert the message contains the bound and
   `fshttpStudio.requestTimeoutMs`. Assert it does **not** contain `A task was canceled`.
2. **A block's own timeout wins.** The block carries `config_timeoutInSeconds`, longer than the
   injected bound. Assert the Run lasts past the injected bound. This is the test for Decision 2, and
   it is the one that fails if `orElse` becomes an assignment.
3. **The body download is inside the bound.** Use the existing body-after-headers streaming handler,
   with a delay longer than the bound. Assert `RuntimeError` and not a truncated `Ok`. This records
   that `ResponseContentRead` puts the body read under the bound.
4. **A fast request is untouched.** Assert `Ok` with the bound set to its default.
5. **`timeoutMs = 0` injects nothing.** Assert `Ok` against the fast handler, and assert that a
   `Config` read at invocation time leaves `timeout` at `None`.
6. **A refused connection still reports the connection failure.** Point a block at a closed port.
   Assert the message names the connection and not the timeout. This is the test that the
   `AggregateException` unwrapping in Decision 5 does not swallow the wrong exception.
7. **`requestMs` is present, positive, and below the total.** Against the 200 ms handler, assert
   `requestMs` is at least 200 and well below the whole call's elapsed time. This is the assertion
   that the bracket is around the invocation and not around the session.
8. **The worker path carries both.** Force the worker route with a conflicting pin. Assert the bound
   applies and `requestMs` arrives. This is the test for the second half of Decision 4.

### Seam 2: the renderer

`RendererTests` drives canned envelopes. Its `envelope` helper carries `ElapsedMs = 42.0` and must
change with the record.

9. **The status line renders both numbers.** Assert `status-time` reads `142 ms` and `status-total`
   reads `380 ms total`. Assert both spans exist, so a dropped span is a failing test and not a
   silently missing number.

### Seam 3: the host's pure part

`ProtocolTests` drives `Protocol.fs`. Put the `run` abandon string there as a value. Seam 3 then
asserts its text, and the interop module in Decision 6 has nothing to get wrong but the wiring. A
pending `locate` abandons to an empty list, so it contributes no second string.

### What is not tested here

`Companion.fs` is Fable and Node interop, and no suite drives it. The FIFO flush, the closed-handle
guard, and the `error`-path flush are therefore **verified by hand**, against a Beta, in a real
VSCode window. `docs/manual-check.md` carries the steps under *The companion
stops*, and the Manual check gates the release. Do not add a test project for it.

An earlier revision of this spec asked for the check in the pull request instead. That obligation was
structurally impossible to meet, because no build existed at review time, and [#139](https://github.com/tw0po1nt/FsHttp.Studio/issues/139)
met it with a scripted proxy. [ADR-0008](../adr/0008-beta-gates-the-release.md) moved the check to
the Beta gate and built the Beta that it needs.

> **Update (2026-08-10):** The two paragraphs above are no longer true. Spec 7 of the UI test suite
> drives companion death: it kills the companion during a Run, reads the stopped message in the
> response viewer, reloads the window, and runs a block again. The UI suite gates the release,
> `docs/manual-check.md` is deleted, and [ADR-0009](../adr/0009-ui-suite-gates-the-release.md)
> supersedes ADR-0008. One gap remains open. After the companion dies, the suite asserts neither the
> presence nor the absence of the lens. See
> [`docs/release-gate.md`](../release-gate.md).

## Out of Scope

- **A bound on a user's infinite loop.** The request timeout bounds the request. A block that loops
  forever still holds the in-process Run. Bounding that means routing every Run to the killable
  worker, which costs a process spawn on top of a 145 ms warm Run. Knowingly accepted when the item
  list was fixed.
- **Restarting a crashed companion.** Decision 6 makes the failure legible and tells the user to
  reload. Restart, with its backoff and its crash-loop question, is a feature and not a repair.
- **User-invocable cancel.** The bound removes the urgency. Cancel earns its keep with the session
  model, where a Run is no longer a self-contained fresh evaluation.
- **`workerTimeoutMs`.** It stays at 120 s and keeps its job, which is a worker that stalls in a
  restore or never emits a frame. The request bound now fires first for a hung request, which is the
  case it handled worst.
- **The status line's method and URL.** The blank-URL bug for a computed URL belongs to the
  request-as-sent spec, which touches the same envelope next.
- **Any performance claim.** The numbers here justify decisions. v0.2 makes no performance claim and
  builds no benchmark harness.
- **Retries and backoff.** A timeout ends the Run. It does not try again.

## Further Notes

### Sequencing

This spec is written against the code **after the reach mechanism (#96)**, and Decision 3 depends on
it: the invocation-time `Config.update` is where the bound goes, and that call site does not exist
until #96 lands. Land #96 first. #95 and #97 are independent of this one.

This spec touches the `ok` envelope. The request-as-sent spec touches it again, for the method, the
URL, the request headers, and the request body. Land this one first, so the second is one edit to a
shape that already grew once rather than two edits racing.

### Corrections to the ticket that raised this work

Three of its statements did not survive contact with the source.

- **The default Run path is not unbounded.** It is bounded at `HttpClient`'s 100 s default, measured
  from the config's own `httpClientFactory`. Far too long, but a bound.
- **The `#r "nuget:"` restore is not the thing to size the timeout against.** It happens in the Setup
  evaluation, before the request is sent, and no request timeout covers it.
- **The `GlobalConfig.set` preamble is the wrong place** after #96. Refer to Decision 3.

One more, for the reply to #90: v0.2 does not add timing instrumentation. It repairs a number that
already ships and that currently reports mostly our own overhead.

### Measurements

All numbers here were measured while writing this spec, on FsHttp 15.0.3 and FCS 43.12.204, against a
local `HttpListener`:

- `Config.timeout` defaults to `None`, and `None` resolves to `HttpClient.Timeout = 00:01:40`.
- A 2 s bound ends a hanging request at 2017 ms and a 6 s dribbling body at 2004 ms.
- `Option.orElse` against a user's `config_timeoutInSeconds 10.0` yields 10005 ms.
- A timeout surfaces as a bare `TaskCanceledException`, `A task was canceled.`, no inner exception,
  and `:? OperationCanceledException` is true. A refused connection surfaces as `AggregateException`
  → `HttpRequestException` → `SocketException`.
- Fresh session per Run, warm process: create 92–127 ms, Setup 23–37 ms, invocation 6.7–21 ms, total
  136–152 ms. This reproduces the benchmarking research's warm Run of about 145 ms with session
  creation at about 65%.
- First Run of a process: create 398 ms, Setup 520 ms, invocation 73 ms, total 991 ms.
- FsHttp's `Response` record carries no duration field, so there is nothing to read instead of
  bracketing.

### Provenance

The item list, and the rule that these three ride free rather than spending a feature slot, come from
an earlier feature-cap ticket. The 30 s default, the decision to keep the invocation
bracket rather than time inside the evaluated code, and the decision to make a timeout a Runtime
error rather than a fifth Run outcome, were decided while writing this spec. Every measurement above
is new and was made here.


