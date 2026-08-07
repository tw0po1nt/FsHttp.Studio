// PROTOTYPE — thin assertions that throw a real JS Error so Mocha prints .fs line numbers.
// Fable.Mocha's Expect / failwith carry no stack under Fable (research 002).
//
// NOT named `Expect`: `open Fable.Mocha` shadows a module of that name, so every call
// silently reaches Fable.Mocha's own stackless Expect instead. 014's negative control caught
// exactly that — 005 had the shim and never used it.
// IIFE form so the Emit is a valid expression (bare `throw` breaks esbuild).
module Assert

open Fable.Core

[<Emit("(() => { throw new Error($0); })()")>]
let throwJs (_message: string) : 'a = jsNative

let equal (actual: 'a) (expected: 'a) (message: string) =
    if actual <> expected then
        throwJs (sprintf "%s\nExpected: %A\nActual:   %A" message expected actual)

let isTrue (cond: bool) (message: string) =
    if not cond then
        throwJs message

let stringContains (haystack: string) (needle: string) (message: string) =
    if haystack.IndexOf(needle) < 0 then
        throwJs (sprintf "%s\nNeedle: %s\nHaystack: %s" message needle haystack)
