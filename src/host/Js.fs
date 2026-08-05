// Hand-rolled interop with the JavaScript language itself, as opposed to Node (`Node.fs`) or
// the VSCode API (`Vscode.fs`). It holds the shims that are about JS values rather than about
// any one host API, so the modules that need them share one definition instead of each keeping
// its own `[<Emit>]` copy.
module Js

open Fable.Core

/// JS `== null`, which matches null *or* undefined. Two callers need exactly this, and neither
/// can use a null test that Fable would compile to `=== null`: `Node.execFile` signals success
/// with a nullish error argument, and a `locate` response's omitted `refusal` property reads
/// back as `undefined` rather than as `Unchecked.defaultof<obj>`.
[<Emit("$0 == null")>]
let isNullish (_x: obj) : bool = jsNative
