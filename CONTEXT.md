# FsHttp.Studio

A VSCode extension that runs a single [FsHttp](https://github.com/fsprojects/FsHttp) request from an F# script and renders its response richly. FSI can only flatten that response to text.

## Language

### Authoring surface

**Block**:
A single `http { }` FsHttp computation expression in a script. It describes one HTTP request, and it is the unit that a user runs.
_Avoid_: request (ambiguous with the HTTP request itself), snippet.

**Script**:
A `.fsx` F# script file. It is the only source surface that v0.1 supports. Blocks in compiled `.fs` files are out of scope.
_Avoid_: file, document.

**Setup**:
The code that a Run evaluates to reach the target block. It starts at the first line of the script, and stops at the end of the target block's own expression. It thus contains the target block, because a Run reaches a block where the user wrote it. It contains no other block, because FsHttp.Studio blanks each other block first. It contains nothing after the target block. FsHttp.Studio evaluates the Setup afresh for each Run.
_Avoid_: context, preamble, prelude.

**Run**:
The evaluation of one block against a fresh evaluation of its setup, and the rendering of the result. A Run fires only the block that the user clicked, never the other blocks.
_Avoid_: execute, send, invoke.

**Run request CodeLens**:
The `▶ Run request` affordance above each detected block. It is the only way to start a Run in v0.1.
_Avoid_: play button, gutter action.

### Rendering

**Response viewer**:
The single editor panel that renders a Run's result.
_Avoid_: preview, output, inspector.

**Renderer core**:
The presentation-shell-agnostic routine that turns a response body into rendered DOM. It dispatches on the body's `Content-Type`.
_Avoid_: renderer, view.

**Viewer update**:
The tagged object that the extension host posts to the response viewer. An update reports a Run in progress, a Run result, an error, or a Refused Run. An Envelope crosses the companion's process boundary. A viewer update crosses the webview boundary, and the two never name the same object.
_Avoid_: message, payload, event.

### Execution engine

**Companion**:
The long-lived .NET process that parses scripts and evaluates blocks. It hosts an FCS interactive session. The companion is distinct from VSCode's extension host, which is the extension's own JS side.
_Avoid_: server, backend, host.

**Envelope**:
The tagged message that the companion and the extension host exchange across their process boundary. An envelope carries a Run request, a set of block ranges, or a Run outcome.
_Avoid_: message, payload, packet.

**Invocation**:
The F# call that a Run emits to reach its target block once the setup is loaded, qualified by the block's enclosing modules: `getSnorlax ()`, `Outer.Inner.deep`. An invocation is one step inside a Run, not a synonym for it. This is the one sanctioned use of "invoke", because the Run's own _Avoid_ list reserves that word against naming the whole cycle.
_Avoid_: call, dispatch.

**Refusal code**:
The companion's verdict that neither route reaches a block, named by the block's *shape*: `loopBody`, `innerBinding`, `insideAnotherRequest`, and nine more. `BlockLocator.classify` decides it from the untyped syntax tree, and the code is all that crosses the wire — the host owns every user-facing title and toast, keyed by the code. A code is a position's shape, and not a diagnostic about the user's script: nothing is wrong with a block in a loop.
_Avoid_: refusal reason, error code, refusal family (the families that group the codes stay internal to the companion).

### Run outcomes

**HTTP error response**:
A response with a non-2xx status. The Run is still *successful*, because the server answered, so the response viewer renders the body normally and shows the status code.
_Avoid_: failure, error (reserve those for the two below).

**Runtime error**:
A Run that produced no response, because the user's code or the host failed. An example is a refused connection. The response viewer renders it as plain error text.
_Avoid_: exception, crash.

**Compile error**:
A Run whose block or setup did not compile. The response viewer reports it at the source location that caused it.
_Avoid_: syntax error, build error.

**Refused Run**:
A Run that FsHttp.Studio declined, because it cannot reach the block's position, or because the block depends on a value that another block binds. No code was evaluated for a position refusal. The response viewer reports the reason and the workaround, and it does not report a fault in the user's script.
_Avoid_: unsupported, blocked, disabled.

### Shipping

**Beta**:
A candidate build of the extension, cut from `main` and published as a GitHub pre-release. Its version is the target release version with a `-beta.<n>` suffix. A Beta is the thing a Manual check is walked against, and a release requires one.
_Avoid_: nightly (nothing is scheduled), RC (implies a feature freeze that FsHttp.Studio does not declare), preview (the VSCode Marketplace's own pre-release channel, which FsHttp.Studio does not use).

**Branch build**:
A build of the extension from an arbitrary ref, delivered as a workflow artifact. It carries no tag and no release, and it skips the CI gate. It exists so that a person can install a change before it merges.
_Avoid_: dev build, PR build (the ref does not have to belong to a pull request).

**Manual check**:
The by-hand walk of `docs/manual-check.md` against a Beta, in a real VSCode window. It covers the interop surfaces that no suite drives: the lens, the toast, the response viewer, and the companion's process handling.
_Avoid_: smoke (`npm run smoke` already names running the bundled renderer under node), QA, regression test.

### UI test suite

**Check**:
One test in the UI suite, driving a real VSCode through ExTester. A check is what the Manual check's steps become, so it names a user-visible outcome rather than a unit of code.
_Avoid_: test, case, scenario, spec (a spec is the written ticket that asks for the check).

**Harness**:
The shared module every check imports: the ExTester page-object bindings, the wait combinator, the budgets, and the Mocha hooks. A check that defines its own wait or its own budget has bypassed the harness.
_Avoid_: framework, fixture (a fixture is the checked-in script the suite opens), helpers.

**Harness setup**:
The Mocha `before` hook, from the first ExTester call through proven-live. Always the two words, never bare "Setup", which the authoring surface already reserves for the code a Run evaluates.
_Avoid_: setup (bare), init, bootstrap.

**Proven-live**:
The state that Harness setup must reach before any check runs: the workbench answered, the test server passed its healthcheck and its sidecar parsed, the fixture folder is open with the extension active, and a companion exists. A workbench that merely rendered is *visible*, not proven-live.
_Avoid_: ready, warm, healthy.

**Sidecar**:
The JSON file the test HTTP server writes to report the port it allocated and the port it deliberately left dead. It is the one channel between that server and the harness. Harness setup deletes it before the server starts, so a stale file cannot pass as a live one.
_Avoid_: manifest, handshake file, lockfile.

**Dead port**:
The port the test server allocates and never listens on, so a check can drive a refused connection. The harness, not the server, probes it, which keeps one error vocabulary for "the sidecar is stale".
_Avoid_: closed port, bad port.

**Budget**:
The green-path time a phase is allowed: 180 s for Harness setup, 45 s per check, 240 s for the suite. A budget catches drift and is asserted in `afterEach`/`after`, never in a check body. It is not a hang guard — the Mocha timeouts above it are.
_Avoid_: timeout, deadline (a deadline is what one `eventually` call waits against).
