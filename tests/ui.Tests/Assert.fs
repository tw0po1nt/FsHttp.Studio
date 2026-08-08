// Assertions for the UI suite. Each one throws a real JS `Error`, not an F# exception, so Mocha
// reports a stack that `--enable-source-maps` can resolve back to the `.fs` line that failed.
// `run.sh` sets `NODE_OPTIONS=--enable-source-maps` for exactly that reason; without it a failure
// points at the bundle.
module Assert

open Fable.Core

[<Emit("throw new Error($0)")>]
let private throwError (_message: string) : unit = jsNative

let fail (message: string) : unit = throwError message

let equal (actual: 'a) (expected: 'a) (message: string) : unit when 'a: equality =
    if actual <> expected then
        fail (sprintf "%s. Expected %A, got %A" message expected actual)

let isTrue (condition: bool) (message: string) : unit =
    if not condition then
        fail message
