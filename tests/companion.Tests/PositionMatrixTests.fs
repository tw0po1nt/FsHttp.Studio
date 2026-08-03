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

let private isNamedByTheRun =
    function
    | NamedByTheRun -> true
    | _ -> false

let private isNamedByTheBinding (invocation: string) =
    function
    | NamedByTheBinding inv -> inv = invocation
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

/// Asserts block `index`'s enclosing-module qualifier, outermost first.
let private assertQualifier (blocks: LocatedBlock list) (index: int) (position: string) (expected: string list) =
    match List.tryItem index blocks with
    | None -> failtestf "%s: no block at index %d (%d blocks located)" position index blocks.Length
    | Some b -> Expect.equal b.Qualifier expected (sprintf "%s: qualifier" position)

/// Asserts how many `private` keywords sit on block `index`'s own path.
let private assertPrivateSpans (blocks: LocatedBlock list) (index: int) (position: string) (expected: int) =
    match List.tryItem index blocks with
    | None -> failtestf "%s: no block at index %d (%d blocks located)" position index blocks.Length
    | Some b -> Expect.hasLength b.PrivateSpans expected (sprintf "%s: private spans" position)

[<Tests>]
let tests =
    testList
        "PositionMatrix"
        [ test "matrix.fsx: each of the twelve original cases gets the route the spec's table names" {
              let blocks = locateBlocks (fixture "matrix.fsx")
              Expect.hasLength blocks 14 "matrix.fsx has 14 blocks (case 3 and case 11 are two blocks each)"

              let assertRoute = assertRoute blocks
              assertRoute 0 "#1 bare, top level" isNamedByTheRun
              assertRoute 1 "#2 bare, uses a preceding let" isNamedByTheRun
              assertRoute 2 "#3 two independent bare blocks (first)" isNamedByTheRun
              assertRoute 3 "#3 two independent bare blocks (second)" isNamedByTheRun
              assertRoute 4 "#4 right side of a let, block on the next line" (isNamedByTheBinding "squirtle")
              assertRoute 5 "#5 right side of a let, block on the same line" (isNamedByTheBinding "eevee")
              assertRoute 6 "#6 body of a ()-callable function" (isNamedByTheBinding "getSnorlax ()")
              assertRoute 7 "#7 single block in a module" isNamedByTheRun
              assertRoute 8 "#8 block in a module with a preceding member" isNamedByTheRun
              assertRoute 9 "#9 in a for body" (isRefused DecidedAtRunTime)
              assertRoute 10 "#10 in an if branch" (isRefused DecidedAtRunTime)
              assertRoute 11 "#11p producer, let dexId = http { }" (isNamedByTheBinding "dexId")
              assertRoute 12 "#11c consumer, uses another block's value" isNamedByTheRun
              assertRoute 13 "#12 piped to Request.send in the script" isNamedByTheRun
          }

          test "extra.fsx: the shapes the matrix does not have get the route the spec's table names" {
              let blocks = locateBlocks (fixture "extra.fsx")
              Expect.hasLength blocks 19 "extra.fsx has 19 blocks"

              let assertRoute = assertRoute blocks
              assertRoute 0 "#13 let private x = http { }" (isNamedByTheBinding "secret")

              assertRoute
                  1
                  "internal binding (Further Notes measurement, not a numbered position)"
                  (isNamedByTheBinding "semiSecret")

              assertRoute 2 "#14 module private, bare block" isNamedByTheRun
              assertRoute 3 "#15 nested modules, Outer.Inner.deep" (isNamedByTheBinding "deep")
              assertRoute 4 "#16 attributed binding, [<Obsolete>] let …" (isNamedByTheBinding "attributed")
              assertRoute 5 "#22 class member" (isRefused NeedsAnInventedValue)
              assertRoute 6 "#19 function with arguments" (isRefused NeedsAnInventedValue)
              assertRoute 7 "#20 lambda-valued binding" (isRefused NotModuleScoped)
              assertRoute 8 "#21 inner let in a function body" (isRefused NotModuleScoped)
              assertRoute 9 "#17 match clause (first)" (isRefused DecidedAtRunTime)
              assertRoute 10 "#17 match clause (second)" (isRefused DecidedAtRunTime)
              assertRoute 11 "#18 try/with handler (try body)" (isRefused DecidedAtRunTime)
              assertRoute 12 "#18 try/with handler (with handler)" (isRefused DecidedAtRunTime)
              assertRoute 13 "#23 tuple binding (first element)" (isRefused ValueIsNotTheBlock)
              assertRoute 14 "#23 tuple binding (second element)" (isRefused ValueIsNotTheBlock)
              assertRoute 15 "#24 outer binding that holds a nested block" (isNamedByTheBinding "nested")
              assertRoute 16 "#24 block inside another block's expression" (isRefused ValueIsNotTheBlock)
              assertRoute 17 "sweeper block (structural, not a numbered position)" isNamedByTheRun
              assertRoute 18 "module Tail bare block (boundary probe, not a numbered position)" isNamedByTheRun
          }

          // Positions 13 to 15 are the rows that exist to exercise `Qualifier` and
          // `PrivateSpans`, so the route alone does not cover what the spec asks of them.
          test "extra.fsx: the qualifier and the private spans match the enclosing module chain" {
              let blocks = locateBlocks (fixture "extra.fsx")

              let assertQualifier = assertQualifier blocks
              let assertPrivateSpans = assertPrivateSpans blocks

              assertQualifier 0 "#13 let private x = http { }" []
              assertPrivateSpans 0 "#13 let private x = http { }" 1

              assertPrivateSpans 1 "internal binding stays unblanked" 0

              assertQualifier 2 "#14 module private, bare block" [ "Vault" ]
              assertPrivateSpans 2 "#14 module private, bare block" 1

              assertQualifier 3 "#15 nested modules, Outer.Inner.deep" [ "Outer"; "Inner" ]
              assertPrivateSpans 3 "#15 nested modules, Outer.Inner.deep" 0

              assertQualifier 18 "module Tail bare block" [ "Tail" ]
          } ]
