module Extension.Tests.CursorRuleGoldenTests

// The companion locates each Block of the script, so the Golden fixture holds the ranges that a Client gets.

open System
open System.IO
open Expecto
open Companion.Envelope
open Protocol

let private script =
    String.concat
        "\n"
        [ "#r \"nuget: FsHttp\""
          "open FsHttp"
          ""
          "let getSnorlax () ="
          "    http {"
          "        GET \"https://example.com/pokemon/snorlax\""
          "    }"
          ""
          "let postBerry ="
          "    http {"
          "        POST \"https://example.com/berry\""
          "        header \"X-Berry\" ("
          "            http {"
          "                GET \"https://example.com/berry/1\""
          "            }"
          "            |> string"
          "        )"
          "    }"
          ""
          "let oneLine = http { GET \"https://example.com/\"; header \"X-Inner\" (string (http { GET \"https://example.com/inner\" })) }"
          "" ]

let private located = Companion.BlockLocator.locateBlocks script

let private ranges: BlockRange list =
    located.Blocks
    |> List.map (fun block ->
        { StartLine = block.Block.StartLine
          StartCol = block.Block.StartCol
          EndLine = block.Block.EndLine
          EndCol = block.Block.EndCol
          Refusal = Companion.BlockLocator.refusalOf block.Route })

let private cases =
    [ "a line above every Block", 1, None
      "the let getSnorlax () = line above a Block", 4, None
      "the first line of a Block", 5, Some 0
      "a middle line of a Block", 6, Some 0
      "the last line of a Block", 7, Some 0
      "a line between two Blocks", 8, None
      "a line of an outer Block above its inner Block", 12, Some 1
      "the first line of a Block inside another Block", 13, Some 2
      "the last line of a Block inside another Block", 15, Some 2
      "a line of an outer Block below its inner Block", 16, Some 1
      "the last line of an outer Block", 18, Some 1
      "one line that holds a Block inside another Block", 20, Some 4
      "a line below every Block", 21, None ]

let private goldenFixture () =
    encode
        {| cases =
            [ for name, cursorLine, _ in cases ->
                  {| blockIndex = Option.toNullable (blockAtCursor cursorLine ranges)
                     cursorLine = cursorLine
                     name = name |} ]
           ranges =
            [ for r in ranges ->
                  {| endCol = r.EndCol
                     endLine = r.EndLine
                     startCol = r.StartCol
                     startLine = r.StartLine |} ]
           source = script |}

[<Tests>]
let tests =
    testList
        "Cursor rule Golden fixture"
        [ test "the script parses, and the companion locates five Blocks" {
              Expect.isFalse located.ParseFailed "the parse succeeds"
              Expect.hasLength ranges 5 "the script holds five Blocks"

              Expect.equal
                  (ranges |> List.map _.Refusal)
                  [ None; None; Some "insideAnotherRequest"; None; Some "insideAnotherRequest" ]
                  "each inner Block is inside another Block"
          }

          for name, cursorLine, expected in cases do
              test (sprintf "%s gives the Block index %A" name expected) {
                  Expect.equal (blockAtCursor cursorLine ranges) expected name
              }

          test "the cursor rule matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("cursor", "cursor-rule.json")) (goldenFixture ())
          } ]
