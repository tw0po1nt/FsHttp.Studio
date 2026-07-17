module Companion.Tests.BlockLocatorTests

// Seam A (issue #15): drives the companion's block location as a black box — feed .fsx
// source, assert the ranges — matching the acceptance criteria on the ticket directly.
// `BlockLocatorTests` exercises `BlockLocator.locate` itself; `RequestHandlerTests` exercises
// the envelope dispatch (`RequestHandler.respond`) that sits on top of it.

open Xunit
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

[<Fact>]
let ``locates a single bare block, range covers the whole CE`` () =
    let source =
        """
http {
    GET "https://example.com/one"
}
"""

    let ranges = locate source
    Assert.Single(ranges) |> ignore
    Assert.Equal("http {\n    GET \"https://example.com/one\"\n}", slice source ranges.[0])

[<Fact>]
let ``range excludes an enclosing let binding`` () =
    let source =
        """
let r =
    http {
        GET "https://example.com/one"
    }
"""

    let ranges = locate source
    Assert.Single(ranges) |> ignore
    let text = slice source ranges.[0]
    Assert.StartsWith("http {", text)
    Assert.DoesNotContain("let r", text)

[<Fact>]
let ``locates every block in a multi-block file, in source order`` () =
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
    Assert.Equal(3, ranges.Length)

    let texts = ranges |> List.map (slice source)
    Assert.All(texts, fun t -> Assert.StartsWith("http {", t))
    Assert.Contains("/1", texts.[0])
    Assert.Contains("/2", texts.[1])
    Assert.Contains("/3", texts.[2])

[<Fact>]
let ``ignores http-shaped text in comments and strings`` () =
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
    Assert.Single(ranges) |> ignore
    Assert.Contains("/real", slice source ranges.[0])

[<Fact>]
let ``an unbalanced closing brace inside a string does not truncate the block`` () =
    let source =
        """
http {
    GET "https://example.com/sixteen"
    header "X-Template" "}"
    header "X-After" "still inside the block"
}
"""

    let ranges = locate source
    Assert.Single(ranges) |> ignore
    let text = slice source ranges.[0]
    Assert.Contains("X-After", text)
    Assert.EndsWith("}", text)

[<Fact>]
let ``an unbalanced opening brace inside a string does not swallow the next block`` () =
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
    Assert.Equal(2, ranges.Length)
    let first = slice source ranges.[0]
    let second = slice source ranges.[1]
    Assert.Contains("seventeen", first)
    Assert.DoesNotContain("eighteen", first)
    Assert.Contains("eighteen", second)

[<Fact>]
let ``matches a dotted builder head`` () =
    let source =
        """
FsHttp.Dsl.http {
    GET "https://example.com/qualified"
}
"""

    let ranges = locate source
    Assert.Single(ranges) |> ignore
    Assert.StartsWith("FsHttp.Dsl.http {", slice source ranges.[0])

[<Fact>]
let ``an http-named identifier that is not a builder call is not matched`` () =
    let source =
        """
let http = "not a builder"
let httpUrl = "https://example.com/http/notablock"
"""

    Assert.Empty(locate source)
