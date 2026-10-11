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

/// Reacts to a fulfilled JS promise without a promise CE.
[<Emit("$0.then($1)")>]
let onResolved (_p: JS.Promise<'T>) (_onOk: 'T -> unit) : unit = jsNative

/// A pending JS promise, and the function that fulfills it. The executor runs synchronously, so
/// the function exists when this returns.
[<Emit("(() => { let fulfill; const p = new Promise(r => { fulfill = r; }); return [p, fulfill]; })()")>]
let deferred<'T> () : JS.Promise<'T> * ('T -> unit) = jsNative
