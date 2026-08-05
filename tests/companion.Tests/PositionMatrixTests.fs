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

let private isRefused (code: RefusalCode) =
    function
    | Refused(c, _) -> c = code
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

/// The source text a span covers, newlines included.
let private textOf (source: string) (r: BlockRange) =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    if r.StartLine = r.EndLine then
        lines.[r.StartLine - 1].Substring(r.StartCol, r.EndCol - r.StartCol)
    else
        [ yield lines.[r.StartLine - 1].Substring(r.StartCol)
          for i in r.StartLine .. r.EndLine - 2 -> lines.[i]
          yield lines.[r.EndLine - 1].Substring(0, r.EndCol) ]
        |> String.concat "\n"

/// Asserts the source text of block `index`'s type annotation span, colon included. The text is
/// what Decision 7 asks for, and the range alone does not show it: a span that swallowed the
/// bound name, or that dropped the colon and left it behind, both hold a plausible-looking range.
let private assertTypeAnnotation
    (source: string)
    (blocks: LocatedBlock list)
    (index: int)
    (position: string)
    (expected: string option)
    =
    match List.tryItem index blocks with
    | None -> failtestf "%s: no block at index %d (%d blocks located)" position index blocks.Length
    | Some b ->
        Expect.equal (b.TypeAnnotation |> Option.map (textOf source)) expected (sprintf "%s: type annotation" position)

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
              assertRoute 9 "#9 in a for body" (isRefused LoopBody)
              assertRoute 10 "#10 in an if branch" (isRefused IfBranch)
              assertRoute 11 "#11p producer, let dexId = http { }" (isNamedByTheBinding "dexId")
              assertRoute 12 "#11c consumer, uses another block's value" isNamedByTheRun
              assertRoute 13 "#12 piped to Request.send in the script" isNamedByTheRun
          }

          test "extra.fsx: the shapes the matrix does not have get the route the spec's table names" {
              let blocks = locateBlocks (fixture "extra.fsx")
              Expect.hasLength blocks 22 "extra.fsx has 22 blocks"

              let assertRoute = assertRoute blocks
              assertRoute 0 "#13 let private x = http { }" (isNamedByTheBinding "secret")

              assertRoute
                  1
                  "internal binding (Further Notes measurement, not a numbered position)"
                  (isNamedByTheBinding "semiSecret")

              assertRoute 2 "#14 module private, bare block" isNamedByTheRun
              assertRoute 3 "#15 nested modules, Outer.Inner.deep" (isNamedByTheBinding "deep")
              assertRoute 4 "#16 attributed binding, [<Obsolete>] let …" (isNamedByTheBinding "attributed")
              assertRoute 5 "#22 class member" (isRefused ClassMember)
              assertRoute 6 "#19 function with arguments" (isRefused NeedsArguments)
              assertRoute 7 "#20 lambda-valued binding" (isRefused LambdaValue)
              assertRoute 8 "#21 inner let in a function body" (isRefused InnerBinding)
              assertRoute 9 "#17 match clause (first)" (isRefused MatchClause)
              assertRoute 10 "#17 match clause (second)" (isRefused MatchClause)
              assertRoute 11 "#18 try/with handler (try body)" (isRefused ExceptionHandler)

              // FCS represents a `with` case as a `SynMatchClause`, the same node a `match`
              // expression's own clauses use, and `classify` tests `SynMatchClause` before
              // `TryWith` (Decision 2's own table lists `SynMatchClause` under `matchClause`).
              // The handler's own clause therefore takes that branch, not `exceptionHandler`,
              // which stays reachable through the try body alone (the row just above).
              assertRoute 12 "#18 try/with handler (with handler)" (isRefused MatchClause)
              assertRoute 13 "#23 tuple binding (first element)" (isRefused TupleBinding)
              assertRoute 14 "#23 tuple binding (second element)" (isRefused TupleBinding)
              assertRoute 15 "#24 outer binding that holds a nested block" (isNamedByTheBinding "nested")

              assertRoute 16 "#24 block inside another block's expression" (isRefused InsideAnotherRequest)

              assertRoute
                  17
                  "wildcard binding gives no name to invoke (0003, not a numbered reach-spec position)"
                  (isRefused NoNameToCall)

              assertRoute
                  18
                  "bare outer block holding a nested bare block (0003, not a numbered reach-spec position)"
                  isNamedByTheRun

              assertRoute
                  19
                  "bare block nested inside another bare block's expression (0003, not a numbered reach-spec position)"
                  (isRefused InsideAnotherRequest)

              assertRoute 20 "sweeper block (structural, not a numbered position)" isNamedByTheRun
              assertRoute 21 "module Tail bare block (boundary probe, not a numbered position)" isNamedByTheRun
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

              assertPrivateSpans 4 "#16 attributed binding, private below the attribute line" 1

              assertQualifier 21 "module Tail bare block" [ "Tail" ]
          }

          // Decision 7 asks for two things the route cannot show: the span covers the colon, and
          // it exists on the R2 route alone. Position 16 is the row that carries an annotation.
          test "extra.fsx: the type annotation span covers the colon and the type, on R2 alone" {
              let source = fixture "extra.fsx"
              let blocks = locateBlocks source
              let assertTypeAnnotation = assertTypeAnnotation source blocks

              assertTypeAnnotation 4 "#16 attributed binding, let private attributed: Response" (Some ": Response")

              // An R2 binding with no annotation has no span to blank.
              assertTypeAnnotation 0 "#13 let private x = http { }" None

              // A refused binding keeps its annotation, and an argument's own annotation is not
              // the binding's return type.
              assertTypeAnnotation 6 "#19 function with arguments" None

              // R1 names nothing, so there is no binding to read an annotation off.
              assertTypeAnnotation 20 "sweeper block, bare and top level" None
          } ]
