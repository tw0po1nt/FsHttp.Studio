// UI suite assertions throw a real JS Error so Mocha stack traces name the .fs file and line.
// Fable-compiled F# exceptions carry no stack. Set NODE_OPTIONS=--enable-source-maps when you run
// the bundle so the trace maps back to F# source.
module Assert

open Fable.Core

[<Emit("throw new Error($0)")>]
let private throwError (_message: string) : unit = jsNative

let fail (message: string) : unit = throwError message

let equal (actual: 'a) (expected: 'a) (message: string) : unit when 'a: equality =
    if actual <> expected then
        fail (sprintf "%s. Expected %A, got %A" message expected actual)
