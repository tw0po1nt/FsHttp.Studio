module Companion.Tests.BlockLocatorTests

// Seam A. It drives the companion's block location as a black box: feed .fsx source, then
// assert the ranges. This matches the acceptance criteria on the ticket directly.
// `BlockLocatorTests` drives `BlockLocator.locate` itself. `RequestHandlerTests` drives the
// envelope dispatch (`RequestHandler.respond`) that sits on top of it.

open Expecto
open Companion.BlockLocator

/// Asserts *what* a range covers, and not only its coordinates. That is the property that the
/// acceptance criteria care about.
let private slice = sliceRange

[<Tests>]
let tests =
    testList
        "BlockLocator"
        [ test "locates a single bare block, range covers the whole CE" {
              let source =
                  """
http {
    GET "https://example.com/one"
}
"""

              let ranges = locate source
              Expect.hasLength ranges 1 "one block expected"

              Expect.equal
                  (slice source ranges.[0])
                  "http {\n    GET \"https://example.com/one\"\n}"
                  "range should cover the whole CE"
          }

          test "range excludes an enclosing let binding" {
              let source =
                  """
let r =
    http {
        GET "https://example.com/one"
    }
"""

              let ranges = locate source
              Expect.hasLength ranges 1 "one block expected"
              let text = slice source ranges.[0]
              Expect.stringStarts text "http {" "range should start at the builder head"
              Expect.isFalse (text.Contains "let r") "range should not include the let binding"
          }

          test "locates every block in a multi-block file, in source order" {
              let source =
                  """
let a =
    http {
        GET "https://example.com/1"
    }

let b =
    http {
        GET "https://example.com/2"
    }

http {
    GET "https://example.com/3"
}
"""

              let ranges = locate source
              Expect.equal ranges.Length 3 "three blocks expected"

              let texts = ranges |> List.map (slice source)

              texts
              |> List.iter (fun t -> Expect.stringStarts t "http {" "each range starts at the builder head")

              Expect.stringContains texts.[0] "/1" "first block in source order"
              Expect.stringContains texts.[1] "/2" "second block in source order"
              Expect.stringContains texts.[2] "/3" "third block in source order"
          }

          test "ignores http-shaped text in comments and strings" {
              let source =
                  """
// http { GET "https://example.com/commented" }
(* http { GET "https://example.com/block-commented" } *)
let s = "http { GET \"https://example.com/in-a-string\" }"
http {
    GET "https://example.com/real"
}
"""

              let ranges = locate source
              Expect.hasLength ranges 1 "only the real block should match"
              Expect.stringContains (slice source ranges.[0]) "/real" "the matched block is the real one"
          }

          test "an unbalanced closing brace inside a string does not truncate the block" {
              let source =
                  """
http {
    GET "https://example.com/sixteen"
    header "X-Template" "}"
    header "X-After" "still inside the block"
}
"""

              let ranges = locate source
              Expect.hasLength ranges 1 "one block expected"
              let text = slice source ranges.[0]
              Expect.stringContains text "X-After" "content after the string brace stays inside the block"
              Expect.stringEnds text "}" "range extends to the real closing brace"
          }

          test "an unbalanced opening brace inside a string does not swallow the next block" {
              let source =
                  """
http {
    GET "https://example.com/seventeen"
    header "X-Template" "{"
}

http {
    GET "https://example.com/eighteen"
}
"""

              let ranges = locate source
              Expect.equal ranges.Length 2 "two blocks expected"
              let first = slice source ranges.[0]
              let second = slice source ranges.[1]
              Expect.stringContains first "seventeen" "first block covers the seventeen request"
              Expect.isFalse (first.Contains "eighteen") "first block does not swallow the second"
              Expect.stringContains second "eighteen" "second block covers the eighteen request"
          }

          test "matches a dotted builder head" {
              let source =
                  """
FsHttp.Dsl.http {
    GET "https://example.com/qualified"
}
"""

              let ranges = locate source
              Expect.hasLength ranges 1 "one block expected"
              Expect.stringStarts (slice source ranges.[0]) "FsHttp.Dsl.http {" "range starts at the dotted head"
          }

          test "an http-named identifier that is not a builder call is not matched" {
              let source =
                  """
let http = "not a builder"
let httpUrl = "https://example.com/http/notablock"
"""

              Expect.isEmpty (locate source) "no builder calls, so no blocks"
          }

          test "locateBlocks pairs a let-bound block with its whole declaration, including a trailing pipe" {
              let source =
                  """
let a =
    http {
        GET "https://example.com/one"
    }
    |> Request.send
    |> ignore
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              let blankText = slice source blocks.[0].Blank
              Expect.stringStarts blankText "let a =" "blank span starts at the let keyword"
              Expect.stringEnds blankText "|> ignore" "blank span extends through the trailing pipe"
          }

          test "locateBlocks pairs a bare block statement with itself" {
              let source =
                  """
http {
    GET "https://example.com/one"
}
|> Request.send
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              let blankText = slice source blocks.[0].Blank
              Expect.stringStarts blankText "http {" "blank span starts at the builder head"
              Expect.stringEnds blankText "|> Request.send" "blank span extends through the trailing pipe"
          }

          test "locateBlocks blanks only a class member's right side, not the whole type" {
              let source =
                  """
type Api() =
    member _.Get() =
        http {
            GET "https://example.com/member"
        }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              let blankText = slice source blocks.[0].Blank
              Expect.stringStarts blankText "http {" "member blank span is the right side only, not the member head"
              Expect.isFalse (blankText.Contains "member _.Get") "blank span does not reach the member head"
              Expect.isFalse (blankText.Contains "type Api") "blank span does not reach the type header"
          }

          // Decision 2: "Only a type annotation or parentheses can be between the binding and the
          // block." Parens start at their own `(`, before the block, so the boundary rule that
          // consumes an ancestor starting at the block never reaches one.
          test "a parenthesized binding value still routes R2" {
              let source =
                  """
let wrapped = (http { GET "https://example.com/paren" })
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              Expect.equal blocks.[0].Route (NamedByTheBinding "wrapped") "parentheses are transparent to R2"
          }

          test "an annotated binding value still routes R2" {
              let source =
                  """
let annotated: FsHttp.Domain.HeaderContext = http { GET "https://example.com/typed" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              Expect.equal blocks.[0].Route (NamedByTheBinding "annotated") "a type annotation is transparent to R2"
          }

          // Parens are transparent to R2 only. R1 inserts `let <name> = ` at the block's own
          // start, which parentheses around a bare statement would swallow.
          test "a parenthesized bare block is not R1" {
              let source =
                  """
(http { GET "https://example.com/paren-bare" })
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              Expect.notEqual blocks.[0].Route NamedByTheRun "parentheses are not transparent to R1"
          }

          // A head pattern that binds no single name gives the invocation nothing to call.
          test "a wildcard binding is refused, and not as an out-of-module position" {
              let source =
                  """
let _ = http { GET "https://example.com/wildcard" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"

              match blocks.[0].Route with
              | Refused code ->
                  Expect.equal code NoNameToCall "a wildcard binding gives no name to invoke"
                  Expect.isFalse ((reasonFor code).Contains "Syn") "the reason must not name an FCS type"
              | other -> failtestf "expected a refusal, got %A" other
          }

          // Decision 2 qualifies by "the enclosing nested modules", and Decision 6 blanks
          // `private` on "each enclosing module". A file's own `module M` header is one of both:
          // it puts `M.` in front of the invocation and can carry the same `private`.
          test "a file-header module contributes its name and its private keyword" {
              let source =
                  """module private Head

let inHead = http { GET "https://example.com/head" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              Expect.equal blocks.[0].Qualifier [ "Head" ] "the header module qualifies the invocation"
              Expect.hasLength blocks.[0].PrivateSpans 1 "the header module's private is on the path"
              Expect.equal (slice source blocks.[0].PrivateSpans.[0]) "private" "the span covers the keyword itself"
          }

          // A script with no header parses as an anonymous module, whose synthetic name must not
          // reach the qualifier.
          test "a headerless script has no qualifier" {
              let source =
                  """
let bare = http { GET "https://example.com/bare" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 1 "one block expected"
              Expect.isEmpty blocks.[0].Qualifier "an anonymous module contributes no name"
          }

          // A runnable route is `NamedByTheRun` or `NamedByTheBinding`: neither carries a
          // `RefusalCode`, so a block that classify does not refuse carries no code at all.
          test "a runnable block's route carries no refusal code" {
              let source =
                  """
http { GET "https://example.com/bare-run" }

let named = http { GET "https://example.com/named-run" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 2 "two blocks expected"

              blocks
              |> List.iter (fun b ->
                  match b.Route with
                  | Refused code -> failtestf "expected a runnable route, got a refusal: %A" code
                  | _ -> ())
          }

          // docs/spec/0003-lens-tells-the-truth.md, Decision 3: `insideAnotherRequest` comes
          // from range containment over `locateBlocks`'s full output, not from a syntax-tree
          // branch. The inner block here reaches classify's catch-all on its own -- there is no
          // `SynBinding`, no loop, no branch, no match, no lambda on its path -- so containment
          // is the only thing that can tell it apart from `unaddressable`.
          test "a block nested inside another block's expression is insideAnotherRequest" {
              let source =
                  """
http {
    GET "https://example.com/outer"
    header "X-Inner" (string (sprintf "%A" (http { GET "https://example.com/inner" })).Length)
}
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 2 "two blocks expected: the outer and the nested one"

              Expect.equal blocks.[0].Route NamedByTheRun "the outer block is runnable"

              match blocks.[1].Route with
              | Refused InsideAnotherRequest -> ()
              | other -> failtestf "expected InsideAnotherRequest, got %A" other
          }

          // The title is shape-grained (docs/spec/0003, Decision 2), so a try and a match must
          // not answer with each other's shape. FCS gives both a `SynMatchClause`, and the
          // handler's parent is the only thing that separates them.
          test "a try/with handler is an exceptionHandler, and a match clause is a matchClause" {
              let source =
                  """
let tried =
    try
        http { GET "https://example.com/tried" }
    with _ ->
        http { GET "https://example.com/caught" }

let matched =
    match System.DateTime.Now.Hour with
    | 0 -> http { GET "https://example.com/matched" }
    | _ -> http { GET "https://example.com/other" }
"""

              let blocks = locateBlocks source
              Expect.hasLength blocks 4 "four blocks expected"

              let routeOf i = blocks.[i].Route
              Expect.equal (routeOf 0) (Refused ExceptionHandler) "the try body is a handler position"
              Expect.equal (routeOf 1) (Refused ExceptionHandler) "the with handler is a handler position too"
              Expect.equal (routeOf 2) (Refused MatchClause) "a real match clause stays a match clause"
              Expect.equal (routeOf 3) (Refused MatchClause) "a real match clause stays a match clause"
          }

          // Decision 11 asks that a refusal's sentence never interpolate an FCS type name. One
          // code was the only one under that guard while the sentences lived at their twelve
          // construction sites; `reasonFor` makes it one table, so the guard covers all twelve.
          test "every refusal code has a plain reason that names no FCS type" {
              let codes =
                  [ LoopBody
                    IfBranch
                    MatchClause
                    ExceptionHandler
                    NeedsArguments
                    ClassMember
                    InnerBinding
                    LambdaValue
                    NoNameToCall
                    TupleBinding
                    InsideAnotherRequest
                    Unaddressable ]

              codes
              |> List.iter (fun code ->
                  let reason = reasonFor code
                  Expect.isNotEmpty reason (sprintf "%A has a reason" code)
                  Expect.isFalse (reason.Contains "Syn") (sprintf "%A must not name an FCS type" code))
          } ]
