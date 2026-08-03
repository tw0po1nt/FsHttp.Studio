module Companion.Tests.PositionMatrixTests

// Seam 1 of docs/spec/0002-reach-a-block-anywhere.md: the position matrix, with no FSI and no
// server. Drives `BlockLocator.locateBlocks` against the two corpora the spec names —
// `fixtures/matrix.fsx` (SchlenkR's 12 cases) and `fixtures/extra.fsx` (the shapes the matrix
// does not have) — and asserts each block's `Route` against the spec's position table. This is
// a parse and a fold, so it needs no FSI session and no counting server.
//
// The fixtures are the spec's own corpora, copied in verbatim (docs/spec, Further Notes: "Copy
// them into the test project before you start"). The spec's position table is the authority for
// what each index below asserts.

open System.IO
open Expecto
open Companion.BlockLocator

let private fixture name =
    Path.Combine(__SOURCE_DIRECTORY__, "fixtures", name) |> File.ReadAllText

let private isR1 =
    function
    | R1 -> true
    | _ -> false

let private isR2 (invocation: string) =
    function
    | R2 inv -> inv = invocation
    | _ -> false

let private isRefused (family: RefusalFamily) =
    function
    | Refused(f, _) -> f = family
    | _ -> false

/// Asserts block `index`'s route against `expected`, naming the spec position in the failure.
let private assertRoute (blocks: LocatedBlock list) (index: int) (position: string) (expected: Route -> bool) =
    match List.tryItem index blocks with
    | None -> failtestf "%s: no block at index %d (%d blocks located)" position index blocks.Length
    | Some b -> Expect.isTrue (expected b.Route) (sprintf "%s: got %A" position b.Route)

[<Tests>]
let tests =
    testList
        "PositionMatrix"
        [ test "matrix.fsx: each of #90's twelve cases gets the route the spec's table names" {
              let blocks = locateBlocks (fixture "matrix.fsx")
              Expect.hasLength blocks 14 "matrix.fsx has 14 blocks (case 3 and case 11 are two blocks each)"

              let assertRoute = assertRoute blocks
              assertRoute 0 "#1 bare, top level" isR1
              assertRoute 1 "#2 bare, uses a preceding let" isR1
              assertRoute 2 "#3 two independent bare blocks (first)" isR1
              assertRoute 3 "#3 two independent bare blocks (second)" isR1
              assertRoute 4 "#4 right side of a let, block on the next line" (isR2 "squirtle")
              assertRoute 5 "#5 right side of a let, block on the same line" (isR2 "eevee")
              assertRoute 6 "#6 body of a ()-callable function" (isR2 "getSnorlax ()")
              assertRoute 7 "#7 single block in a module" isR1
              assertRoute 8 "#8 block in a module with a preceding member" isR1
              assertRoute 9 "#9 in a for body" (isRefused F1)
              assertRoute 10 "#10 in an if branch" (isRefused F1)
              assertRoute 11 "#11p producer, let dexId = http { }" (isR2 "dexId")
              assertRoute 12 "#11c consumer, uses another block's value" isR1
              assertRoute 13 "#12 piped to Request.send in the script" isR1
          }

          test "extra.fsx: the shapes the matrix does not have get the route the spec's table names" {
              let blocks = locateBlocks (fixture "extra.fsx")
              Expect.hasLength blocks 19 "extra.fsx has 19 blocks"

              let assertRoute = assertRoute blocks
              assertRoute 0 "#13 let private x = http { }" (isR2 "secret")
              assertRoute 1 "internal binding (Further Notes measurement, not a numbered position)" (isR2 "semiSecret")
              assertRoute 2 "#14 module private, bare block" isR1
              assertRoute 3 "#15 nested modules, Outer.Inner.deep" (isR2 "deep")
              assertRoute 4 "#16 attributed binding, [<Obsolete>] let …" (isR2 "attributed")
              assertRoute 5 "#22 class member" (isRefused F2)
              assertRoute 6 "#19 function with arguments" (isRefused F2)
              assertRoute 7 "#20 lambda-valued binding" (isRefused F3)
              assertRoute 8 "#21 inner let in a function body" (isRefused F3)
              assertRoute 9 "#17 match clause (first)" (isRefused F1)
              assertRoute 10 "#17 match clause (second)" (isRefused F1)
              assertRoute 11 "#18 try/with handler (try body)" (isRefused F1)
              assertRoute 12 "#18 try/with handler (with handler)" (isRefused F1)
              assertRoute 13 "#23 tuple binding (first element)" (isRefused F5)
              assertRoute 14 "#23 tuple binding (second element)" (isRefused F5)
              assertRoute 15 "nested-module runnable block (C3 hazard fixture, not a numbered position)" (isR2 "nested")
              assertRoute 16 "#24 block inside another block's expression" (isRefused F5)
              assertRoute 17 "sweeper block (structural, not a numbered position)" isR1
              assertRoute 18 "module Tail bare block (C1 boundary probe, not a numbered position)" isR1
          } ]
