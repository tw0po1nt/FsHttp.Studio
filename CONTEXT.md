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

### Run outcomes

**HTTP error response**:
A response with a non-2xx status. The Run is still *successful*, because the server answered, so the response viewer renders the body normally and shows the status code.
_Avoid_: failure, error (reserve those for the two below).

**Runtime error**:
A Run that produced no response, because the user's code or the host failed. Examples are a refused connection, or a name that an earlier un-run block left unbound. The response viewer renders it as plain error text.
_Avoid_: exception, crash.

**Compile error**:
A Run whose block or setup did not compile. The response viewer reports it at the source location that caused it.
_Avoid_: syntax error, build error.
