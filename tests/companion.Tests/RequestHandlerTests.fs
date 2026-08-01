module Companion.Tests.RequestHandlerTests

// Drives the envelope dispatch that sits on top of BlockLocator. See BlockLocatorTests for the
// location logic itself. A "locate" request round-trips through JSON to a "blocks" response,
// which carries one range for each block. This mirrors the response protocol.

open System.Text.Json
open Expecto
open Companion.RequestHandler

let private respondTo (requestJson: string) : JsonElement =
    use doc = JsonDocument.Parse(requestJson: string)
    let response = respond doc
    JsonDocument.Parse(JsonSerializer.Serialize response).RootElement.Clone()

[<Tests>]
let tests =
    testList
        "RequestHandler"
        [ test "locate request returns a blocks envelope with one range per block" {
              let request =
                  JsonSerializer.Serialize(
                      {| tag = "locate"
                         source =
                          "http {\n    GET \"https://example.com/1\"\n}\n\nhttp {\n    GET \"https://example.com/2\"\n}\n" |}
                  )

              let response = respondTo request
              Expect.equal (response.GetProperty("tag").GetString()) "blocks" "tag should be blocks"
              let ranges = response.GetProperty("ranges")
              Expect.equal (ranges.GetArrayLength()) 2 "two ranges expected"

              let first = ranges.[0]
              Expect.equal (first.GetProperty("startLine").GetInt32()) 1 "first block starts on line 1"
              Expect.equal (first.GetProperty("startCol").GetInt32()) 0 "first block starts at column 0"

              Expect.isTrue
                  (first.GetProperty("endLine").GetInt32()
                   >= first.GetProperty("startLine").GetInt32())
                  "endLine is not before startLine"
          }

          test "locate request on source with no blocks returns an empty ranges array" {
              let request =
                  JsonSerializer.Serialize
                      {| tag = "locate"
                         source = "let x = 1\n" |}

              let response = respondTo request
              Expect.equal (response.GetProperty("tag").GetString()) "blocks" "tag should be blocks"
              Expect.equal (response.GetProperty("ranges").GetArrayLength()) 0 "no ranges expected"
          }

          test "hello request still returns ready" {
              let response = respondTo (JsonSerializer.Serialize {| tag = "hello" |})
              Expect.equal (response.GetProperty("tag").GetString()) "ready" "hello should be answered with ready"
          }

          test "unknown request tag returns an error envelope" {
              let response = respondTo (JsonSerializer.Serialize {| tag = "not-a-real-tag" |})
              Expect.equal (response.GetProperty("tag").GetString()) "error" "unknown tag should be an error"
          }

          test "run request on a non-compiling block returns a compileError envelope with a range" {
              let request =
                  JsonSerializer.Serialize(
                      {| tag = "run"
                         source = "http {\n    GET undefinedBaseUrl\n}\n"
                         blockIndex = 0 |}
                  )

              let response = respondTo request
              Expect.equal (response.GetProperty("tag").GetString()) "compileError" "tag should be compileError"
              let diagnostics = response.GetProperty("diagnostics")
              Expect.isTrue (diagnostics.GetArrayLength() > 0) "at least one diagnostic expected"
              let range = diagnostics.[0].GetProperty("range")
              Expect.isTrue (range.GetProperty("startLine").GetInt32() > 0) "range should point at a real line"
          }

          test "run request on an out-of-range block index returns a runtimeError envelope" {
              let request =
                  JsonSerializer.Serialize(
                      {| tag = "run"
                         source = "let x = 1\n"
                         blockIndex = 0 |}
                  )

              let response = respondTo request
              Expect.equal (response.GetProperty("tag").GetString()) "runtimeError" "tag should be runtimeError"
          } ]
