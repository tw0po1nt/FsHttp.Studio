// Assertions for the UI suite. Each one throws a real JS `Error`, not an F# exception, so Mocha
// reports a stack that `--enable-source-maps` can resolve back to the `.fs` line that failed.
// `run.sh` sets `NODE_OPTIONS=--enable-source-maps` for exactly that reason; without it a failure
// points at the bundle.
module Assert

open Fable.Core

// Throws a real JS `Error`. Typed as `'a` so a failing branch can inhabit any return type the
// same way `failwith` does. The IIFE keeps the emit a value expression — a bare `throw` inside
// a `return` is not valid JavaScript, which is what Fable would emit for a polymorphic `Emit`.
[<Emit("(() => { throw new Error($0); })()")>]
let private throwError (_message: string) : 'a = jsNative

let fail (message: string) : 'a = throwError message

let equal (actual: 'a) (expected: 'a) (message: string) : unit when 'a: equality =
    if actual <> expected then
        fail (sprintf "%s. Expected %A, got %A" message expected actual)

let isTrue (condition: bool) (message: string) : unit =
    if not condition then
        fail message
