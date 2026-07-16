# FsHttp.Explorer

A VSCode extension that runs a single [FsHttp](https://github.com/fsprojects/FsHttp) request from an F# script and renders its response richly — the rendering FSI can only ever flatten to text.

## Language

### Authoring surface

**Block**:
A single `http { }` FsHttp computation expression in a script, describing one HTTP request. The unit a user runs.
_Avoid_: request (ambiguous with the HTTP request itself), snippet.

**Script**:
A `.fsx` F# script file. The only source surface v0.1 supports; blocks in compiled `.fs` files are out of scope.
_Avoid_: file, document.

**Setup**:
The code preceding a block that it depends on — `open`s, `#r` references, pure `let`s, helpers — but never another block. Evaluated afresh before each Run.
_Avoid_: context, preamble, prelude.

**Run**:
The act of evaluating one block against a fresh evaluation of its setup and rendering the result. Fires only the clicked block, never the others.
_Avoid_: execute, send, invoke.

**Run request CodeLens**:
The `▶ Run request` affordance shown above each detected block; the sole way to start a Run in v0.1.
_Avoid_: play button, gutter action.

### Rendering

**Response viewer**:
The single editor panel that renders a Run's result.
_Avoid_: preview, output, inspector.

**Renderer core**:
The presentation-shell-agnostic routine that turns a response body into rendered DOM, dispatched on its `Content-Type`.
_Avoid_: renderer, view.

### Execution engine

**Companion**:
The long-lived .NET process that parses scripts and evaluates blocks, hosting an FCS interactive session. Distinct from VSCode's extension host — the extension's own JS side.
_Avoid_: server, backend, host.

**Envelope**:
The tagged message the companion and the extension host exchange across their process boundary — a Run request, a set of block ranges, or a run outcome.
_Avoid_: message, payload, packet.

### Run outcomes

**HTTP error response**:
A response with a non-2xx status. Still a *successful* Run — the server answered — so it renders normally with the code shown.
_Avoid_: failure, error (reserve those for the two below).

**Runtime error**:
A Run that produced no response because the user's code or the host failed — a refused connection, or a name left unbound by an un-run prior block. Rendered as plain error text.
_Avoid_: exception, crash.

**Compile error**:
A Run whose block or setup did not compile. Surfaced at the offending source location.
_Avoid_: syntax error, build error.
