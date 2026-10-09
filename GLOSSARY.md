# FsHttp.Studio

FsHttp.Studio runs a single [FsHttp](https://github.com/fsprojects/FsHttp) request from an F# script, in the editor, and renders its response richly.

## Language

### Authoring surface

**Block**:
A single `http { }` FsHttp computation expression in a script. It describes one HTTP request, and it is the unit that a user runs.
_Avoid_: request (ambiguous with the HTTP request itself), snippet.

**Script**:
A `.fsx` F# script file. It is the only source surface that v0.1 supports. Blocks in compiled `.fs` files are out of scope.
_Avoid_: file, document (an **Active document** is the wider term, for a buffer that may not be a Script).

**Active document**:
The buffer that the user is looking at, which the Status line text reports on. In VSCode it is the active `TextDocument`, and there is none when a webview has focus. In Neovim it is the current buffer, which can be the Response buffer. It can be a Script, another F# file, or something else. This is the one sanctioned use of "document": the Status line text must name a buffer before it knows whether the buffer is a Script.
_Avoid_: current file, open file, active script (a Script is only one of the things it can be), current buffer.

**Status line text**:
The one line of text that reports the companion state and the script view of the Active document. The VSCode status-bar item and the Neovim statusline component both show it.
_Avoid_: status bar, lualine component, status.

**Setup**:
The code that a Run evaluates to reach the target block. It starts at the first line of the script, and stops at the end of the target block's own expression. It thus contains the target block, because a Run reaches a block where the user wrote it. It contains no other block, because FsHttp.Studio blanks each other block first. It contains nothing after the target block. FsHttp.Studio evaluates the Setup afresh for each Run.
_Avoid_: context, preamble, prelude.

**Loaded file**:
A source file that a Script brings in with `#load`. It can be a `.fsx` or a `.fs` file. Its code is part of the Setup, so a Compile error can have its position in a Loaded file. A file that a Loaded file brings in with `#load` is also a Loaded file. A loaded `.fsx` file is also a Script. That file is a Loaded file only in a Run of a Script that loads it.
_Avoid_: included file, dependency, imported script.

**Run**:
The evaluation of one block against a fresh evaluation of its setup, and the rendering of the result. A Run fires only the block that the user targets, never the other blocks.
_Avoid_: execute, send, invoke.

**Run request CodeLens**:
The `▶ Run request` affordance above each located block in VSCode. A click on it starts a Run of that block.
_Avoid_: play button, gutter action.

**Block mark**:
The virtual line and the sign that the Neovim client shows on each located block. A Block mark states whether a Run can reach the block. It starts no Run.
_Avoid_: lens, Run mark, extmark.

### Rendering

**Response viewer**:
The single editor panel that renders a Run's result in VSCode.
_Avoid_: preview, output, inspector.

**Response buffer**:
The single scratch buffer that shows a Run's result in the Neovim client.
_Avoid_: result window, output buffer, viewer.

**Body title**:
The title line of the Body section in the Response buffer. The lines of the body start below it. The hint line for `:FsHttp open` is a virtual line directly below it.
_Avoid_: Body header, which a reader can confuse with an HTTP header.

**Renderer core**:
The presentation-shell-agnostic routine that turns a response body into rendered DOM in VSCode. It dispatches on the body's `Content-Type`.
_Avoid_: renderer, view.

**Viewer update**:
The tagged object that the VSCode extension host posts to the Response viewer. An update reports a Run in progress, a Run result, an error, or a Refused Run. An Envelope crosses the companion's process boundary. A viewer update crosses the webview boundary, and the two never name the same object.
_Avoid_: message, payload, event.

### Execution engine

**Companion**:
The long-lived .NET process that parses scripts and evaluates blocks. It hosts an FCS interactive session. The companion is distinct from each Client.
_Avoid_: server, backend, host.

**Client**:
The editor-side program that starts the companion, exchanges envelopes with it, and shows the result of each Run. FsHttp.Studio has two clients: the VSCode extension host, which is the extension's own JS side, and the Neovim client.
_Avoid_: frontend, plugin, host (bare).

**Restart**:
The user's command that stops the companion with each process that the companion started, and then starts a new companion. A Run in progress ends as a Runtime error. A Client never restarts the companion by itself.
_Avoid_: reload (a VSCode window reload), respawn, recover.

**Envelope**:
The tagged message that the companion and a Client exchange across their process boundary. An envelope carries a Run request, a set of block ranges, or a Run outcome.
_Avoid_: message, payload, packet.

**Invocation**:
The F# call that a Run emits to reach its target block once the setup is loaded, qualified by the block's enclosing modules: `getSnorlax ()`, `Outer.Inner.deep`. An invocation is one step inside a Run. This is the one sanctioned use of "invoke", because the Run's own _Avoid_ list reserves that word against naming the whole cycle.
_Avoid_: call, dispatch.

**Captured body**:
The request body that the companion read at send time, while the content was still alive. A captured body has three states: no body, the captured bytes, and a written reason that the companion did not read the body. The companion reads neither a streamed body nor a body above the size cap, because a read must not change what goes on the wire. The Client shows the reason in place of the body.
_Avoid_: request payload, buffered body, recorded body.

**Curl command**:
The shell text that a Client builds from the request as sent. In a POSIX shell, it sends the same method, URL, headers, and body bytes as the Run. A Captured body that the companion did not read gives no Curl command, because that command would send a different request.
_Avoid_: curl snippet, curl export, cURL.

**Copy text**:
The text that a copy button of the Response viewer puts on the clipboard. A yank from the Response buffer puts the same text in a register. The Renderer core defines it for the Request, the Response headers, and the Body. The Lua core ports that rule, and a Golden fixture checks that both give the same bytes. A Body of zero bytes has no copy text.
_Avoid_: copy payload, clipboard text.

**Refusal code**:
The companion's verdict that neither route reaches a block, named by the block's *shape*: `loopBody`, `innerBinding`, `insideAnotherRequest`, and nine more. `BlockLocator.classify` decides it from the untyped syntax tree, and the code is all that crosses the wire. The Client owns every user-facing title and notice, keyed by the code. A code is a position's shape: nothing is wrong with a block in a loop.
_Avoid_: refusal reason, error code, refusal family (the families that group the codes stay internal to the companion).

**Parse failure**:
The companion's report that FCS's untyped parse of a script found errors. The `blocks` envelope carries it as `parseFailed`, beside the block ranges. A parse failure does not stop block location, because the parser recovers. A script with damage can still have blocks, and the damage can hide the blocks below it. A parse failure is a state of the script. A Compile error is an outcome of a Run, and a parse failure needs no Run.
_Avoid_: syntax error (the user-facing wording, which the parse-failure lens uses), broken script, invalid script.

### Run outcomes

**HTTP error response**:
A response with a non-2xx status. The Run is still *successful*, because the server answered, so the Client shows the body normally, with the status code.
_Avoid_: failure, error (reserve those for the two below).

**Runtime error**:
A Run that produced no response, because the user's code or FsHttp.Studio failed. An example is a refused connection. The Client shows it as plain error text.
_Avoid_: exception, crash.

**Compile error**:
A Run whose block or setup did not compile. The Client reports it with the source location that caused it.
_Avoid_: syntax error, build error.

**Refused Run**:
A Run that FsHttp.Studio declined, because it cannot reach the block's position, or because the block depends on a value that another block binds. No code was evaluated for a position refusal. The Client reports the reason and the workaround. A position refusal goes to a notice, and the Client shows no result. A refusal for a value that another block binds takes the place of the result. Neither one reports a fault in the user's script.
_Avoid_: unsupported, blocked, disabled.

### Shipping

**Companion archive**:
The release file that contains the companion for the Neovim client, with a `.sha256` file beside it. The Neovim client downloads it from the release that matches its own version. The VSCode extension carries the companion inside its `.vsix`.
_Avoid_: tarball, companion bundle, companion download.

**Release gate**:
The suites that must pass on the built release files before a release publishes: the UI suite, the Neovim suite, and the Lua core suite. A Beta is not part of it.
_Avoid_: CI gate (the guardrails that `ci.yml` runs), release checks.

**Beta**:
An optional pre-release channel, cut from `main` and published as a GitHub pre-release. It carries the `.vsix` and the Companion archive. Its version is the target release version with a `-beta.<n>` suffix. A Beta hands someone an installable build. It is not required for a release, and it is not part of the Release gate.
_Avoid_: nightly (nothing is scheduled), RC (implies a feature freeze that FsHttp.Studio does not declare), preview (the VSCode Marketplace's own pre-release channel, which FsHttp.Studio does not use).

**Branch build**:
A build of both clients from an arbitrary ref: the `.vsix` and the Companion archive, delivered as workflow artifacts. It carries no tag and no release, and it skips the CI gate. It exists so that a person can install a change before it merges.
_Avoid_: dev build, PR build (the ref does not have to belong to a pull request).

### Test suites

**UI suite**:
The end-to-end suite that drives a real VSCode through ExTester.
_Avoid_: e2e tests, integration tests.

**Neovim suite**:
The end-to-end suite that drives a child Neovim against a real companion, the test HTTP server, and the Sidecar.
_Avoid_: e2e tests, integration tests, Lua tests.

**Lua core suite**:
The tests of the pure core of the Neovim client. They run in headless Neovim, and each core module loads with no access to the Neovim API. They read the Golden fixtures. A test in this suite is not a Check.
_Avoid_: unit specs, core specs (a spec is a written document).

**Golden fixture**:
A committed file that an F# test writes and the Lua core suite reads, so that the Lua client must match the F# output byte for byte. Always the two words: bare "fixture" is the checked-in script that a suite opens.
_Avoid_: snapshot, golden file, fixture (bare).

**Check**:
One test in the UI suite or the Neovim suite. A Check drives a real editor and names a user-visible outcome rather than a unit of code. The word is scoped to the two end-to-end suites, in the way that Harness setup is scoped against bare "Setup": a CI step, a GitHub status check, and a test in the Lua core suite are not Checks, and "check" keeps its ordinary meaning there.
_Avoid_: test, case, scenario, spec (a spec is the written ticket that asks for the check).

**Harness**:
The shared module that every Check of one suite imports. In the UI suite it contains the ExTester page-object bindings, the wait combinator, the Budgets, and the Mocha hooks. In the Neovim suite it contains the child Neovim helpers, the wait combinator, the Budgets, and the mini.test hooks. A Check that defines its own wait or its own Budget has bypassed the Harness.
_Avoid_: framework, fixture (a fixture is the checked-in script the suite opens), helpers.

**Harness setup**:
The hook that runs before the first Check of a suite, through proven-live. In the UI suite it is the Mocha `before` hook, from the first ExTester call. In the Neovim suite it is the mini.test hook that starts the child Neovim. Always the two words, never bare "Setup", which the authoring surface already reserves for the code a Run evaluates.
_Avoid_: setup (bare), init, bootstrap.

**Proven-live**:
The state that Harness setup must reach before any Check runs: the editor answered, the test server passed its healthcheck and its Sidecar parsed, the fixture is open with the Client active, and a companion exists. In the UI suite the editor is the VSCode workbench. A workbench that merely rendered has reached *visible*, which is one state below proven-live. In the Neovim suite the editor is the child Neovim.
_Avoid_: ready, warm, healthy.

**Sidecar**:
The JSON file the test HTTP server writes to report the port it allocated and the port it deliberately left dead. It is the one channel between that server and the Harness. Harness setup deletes it before the server starts, so a stale file cannot pass as a live one.
_Avoid_: manifest, handshake file, lockfile.

**Dead port**:
The port the test server allocates and never listens on, so a Check can drive a refused connection. The Harness owns the probe, which keeps one error vocabulary for "the Sidecar is stale".
_Avoid_: closed port, bad port.

**Budget**:
The green-path time a phase is allowed, for Harness setup, for each Check, and for the suite. In the UI suite the values are 180 s, 45 s, and 300 s. The Neovim suite sets its own values. A Budget catches drift, and the Harness asserts it after a Check or after the suite, never in a Check body. The test runner's timeouts above it are the hang guard.
_Avoid_: timeout, deadline (a deadline is what one `eventually` call waits against).
