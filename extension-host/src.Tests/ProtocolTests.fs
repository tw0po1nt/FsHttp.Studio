module Extension.Tests.ProtocolTests

// Drives the pure logic behind issue #18's CodeLens/Run wiring as plain values — no VSCode, no
// Fable, no companion process — mirroring how the renderer core's Seam-B suite isolates its own
// pure dispatch from the browser-only mounting glue.

open Expecto
open Protocol

let private range startLine startCol endLine endCol =
    { StartLine = startLine
      StartCol = startCol
      EndLine = endLine
      EndCol = endCol }

[<Tests>]
let toVscodeLineTests =
    testList
        "toVscodeLine"
        [ test "subtracts one, matching ADR-0003's FCS(1-based) -> vscode(0-based) convention" {
              Expect.equal (toVscodeLine 1) 0 "FCS line 1 is vscode line 0"
              Expect.equal (toVscodeLine 5) 4 "FCS line 5 is vscode line 4"
          } ]

[<Tests>]
let sliceRangeTests =
    testList
        "sliceRange"
        [ test "single-line range slices within one line" {
              let source = "http {\n    GET \"https://example.com\"\n}\n"
              let r = range 2 4 2 29
              Expect.equal (sliceRange source r) "GET \"https://example.com\"" "should slice the GET line"
          }

          test "multi-line range spans the full block" {
              let source = "http {\n    GET \"https://example.com\"\n}\n"
              let r = range 1 0 3 1

              Expect.equal
                  (sliceRange source r)
                  "http {\n    GET \"https://example.com\"\n}"
                  "should slice the whole block"
          }

          test "normalises CRLF before slicing" {
              let source = "http {\r\n    GET \"https://example.com\"\r\n}\r\n"
              let r = range 2 4 2 29

              Expect.equal
                  (sliceRange source r)
                  "GET \"https://example.com\""
                  "CRLF line endings shouldn't shift columns"
          } ]

[<Tests>]
let extractMethodAndUrlTests =
    testList
        "extractMethodAndUrl"
        [ test "pulls the verb and URL out of a bare GET block" {
              let blockText = "http {\n    GET \"https://example.com\"\n}"
              Expect.equal (extractMethodAndUrl blockText) ("GET", "https://example.com") "verb + URL"
          }

          test "recognises POST" {
              let blockText = "http {\n    POST \"https://example.com/create\"\n}"
              Expect.equal (extractMethodAndUrl blockText) ("POST", "https://example.com/create") "verb + URL"
          }

          test "falls back to blanks when no known verb is present" {
              let blockText = "http {\n    UNKNOWNVERB \"https://example.com\"\n}"
              Expect.equal (extractMethodAndUrl blockText) ("", "") "no throw, blank fallback"
          }

          test "falls back to a blank URL when the verb has no following quoted literal" {
              let blockText = "http {\n    GET undefinedBaseUrl\n}"
              Expect.equal (extractMethodAndUrl blockText) ("GET", "") "verb found, URL blank"
          } ]
