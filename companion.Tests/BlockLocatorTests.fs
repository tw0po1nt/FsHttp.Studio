module Companion.Tests.BlockLocatorTests

// Seam A (issue #15): drives the companion's block location as a black box — feed .fsx
// source, assert the ranges — matching the acceptance criteria on the ticket directly.
// `BlockLocatorTests` exercises `BlockLocator.locate` itself; `RequestHandlerTests` exercises
// the envelope dispatch (`RequestHandler.respond`) that sits on top of it.

open Expecto
open Companion.BlockLocator

/// Reconstructs the exact source text a range covers, using FCS's own numbering (1-based
/// lines, 0-based columns). Used to assert *what* a range covers, not just its coordinates —
/// the property the acceptance criteria actually care about.
let private slice (source: string) (r: BlockRange) : string =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    if r.StartLine = r.EndLine then
        let line = lines.[r.StartLine - 1]
        line.Substring(r.StartCol, r.EndCol - r.StartCol)
    else
        let sb = System.Text.StringBuilder()
        sb.Append(lines.[r.StartLine - 1].Substring(r.StartCol)) |> ignore

        for i in r.StartLine .. r.EndLine - 2 do
            sb.Append('\n').Append(lines.[i]) |> ignore

        sb.Append('\n').Append(lines.[r.EndLine - 1].Substring(0, r.EndCol)) |> ignore

        sb.ToString()

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
          } ]
