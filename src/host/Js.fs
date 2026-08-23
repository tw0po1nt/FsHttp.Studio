module Js

open Fable.Core

/// JS `== null`, which matches null *or* undefined. Two callers need exactly this, and neither
/// can use a null test that Fable would compile to `=== null`: `Node.execFile` signals success
/// with a nullish error argument, and a `locate` response's omitted `refusal` property reads
/// back as `undefined` rather than as `Unchecked.defaultof<obj>`.
[<Emit("$0 == null")>]
let isNullish (_x: obj) : bool = jsNative

/// A nullish-tolerant `unbox`: an omitted or null property reads back as `None`, and any other
/// value as `Some`. Every optional property the companion sends decodes through this, so the
/// absent case is spelled once rather than once per property.
let tryUnbox<'T> (x: obj) : 'T option =
    if isNullish x then None else Some(unbox<'T> x)
