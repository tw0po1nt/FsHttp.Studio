module Extension.Tests.ProtocolTests

// Drives the pure logic behind the CodeLens and Run wiring as plain values, with no VSCode, no
// Fable, and no companion process. This mirrors how the renderer core's Seam-B suite isolates
// its own pure dispatch from the browser-only mounting glue.

open Expecto
open Protocol

let private range startLine startCol endLine endCol =
    { StartLine = startLine
      StartCol = startCol
      EndLine = endLine
      EndCol = endCol
      Refusal = None }

[<Tests>]
let toVscodeLineTests =
    testList
        "toVscodeLine"
        [ test "subtracts one, matching ADR-0003's FCS(1-based) -> vscode(0-based) convention" {
              Expect.equal (toVscodeLine 1) 0 "FCS line 1 is vscode line 0"
              Expect.equal (toVscodeLine 5) 4 "FCS line 5 is vscode line 4"
          } ]

[<Tests>]
let formatCompileErrorTests =
    let diag message r = { Message = message; Range = r }

    testList
        "formatCompileError"
        [ test "prefixes the message with its (line,col), shifting the 0-based column to 1-based" {
              let d = diag "The value or constructor 'auth' is not defined." (range 3 8 3 17)

              Expect.equal
                  (formatCompileError [ d ])
                  "Compile error:\n(3,9) The value or constructor 'auth' is not defined."
                  "col 8 (0-based) prints as 9, matching vscode's Ln/Col"
          }

          test "an addendum error anchored at line 1 col 0 prints as (1,1)" {
              let d = diag "The namespace or module 'FsHttp' is not defined." (range 1 0 1 0)

              Expect.equal
                  (formatCompileError [ d ])
                  "Compile error:\n(1,1) The namespace or module 'FsHttp' is not defined."
                  "top-of-script anchor prints as 1-based (1,1)"
          }

          test "lists each diagnostic on its own line under one header" {
              let d1 = diag "First error." (range 2 4 2 8)
              let d2 = diag "Second error." (range 5 0 5 6)

              Expect.equal
                  (formatCompileError [ d1; d2 ])
                  "Compile error:\n(2,5) First error.\n(5,1) Second error."
                  "one header, one line per diagnostic"
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

          test "normalizes CRLF before slicing" {
              let source = "http {\r\n    GET \"https://example.com\"\r\n}\r\n"
              let r = range 2 4 2 29

              Expect.equal
                  (sliceRange source r)
                  "GET \"https://example.com\""
                  "CRLF line endings must not shift columns"
          } ]

[<Tests>]
let refusalMessageTests =
    // The wire spellings are the companion's `BlockLocator.codeToWire` table. The two sides
    // share no assembly, so this list is the host's copy of that contract, and the drift these
    // tests guard is a code that gains a spelling here but no sentence.
    let wireCodes =
        [ "loopBody"
          "ifBranch"
          "matchClause"
          "exceptionHandler"
          "needsArguments"
          "classMember"
          "innerBinding"
          "lambdaValue"
          "noNameToCall"
          "tupleBinding"
          "insideAnotherRequest"
          "unaddressable" ]

    testList
        "refusalMessage"
        [ test "every wire code gets its own sentence" {
              let messages = wireCodes |> List.map refusalMessage

              messages
              |> List.iter (fun m -> Expect.isNotEmpty m "each code needs a sentence")

              Expect.equal
                  (messages |> List.distinct |> List.length)
                  wireCodes.Length
                  "two codes that share a sentence would hide one verdict behind the other"
          }

          test "an unrecognized code degrades to the unaddressable sentence" {
              Expect.equal
                  (refusalMessage "somethingLaterVersionsAdd")
                  (refusalMessage "unaddressable")
                  "an unknown code must not show a blank or a raw wire spelling"
          }

          test "no sentence shows the raw wire spelling" {
              wireCodes
              |> List.iter (fun code ->
                  Expect.isFalse ((refusalMessage code).Contains code) (sprintf "%s must read as prose" code))
          }

          test "a sentence blames the position, not the user's script" {
              let message = refusalMessage "loopBody"

              Expect.stringContains message "a loop body describes many requests" "it names the position's shape"
              Expect.isFalse (message.Contains "error") "a refusal is not an error"
          } ]

[<Tests>]
let extractMethodAndUrlTests =
    testList
        "extractMethodAndUrl"
        [ test "pulls the verb and URL out of a bare GET block" {
              let blockText = "http {\n    GET \"https://example.com\"\n}"
              Expect.equal (extractMethodAndUrl blockText) ("GET", "https://example.com") "verb + URL"
          }

          test "recognizes POST" {
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
