module Companion.Tests.RequestHandlerTests

// Exercises the envelope dispatch that sits on top of BlockLocator (see BlockLocatorTests for
// the location logic itself): a "locate" request round-trips through JSON to a "blocks"
// response carrying one range per block, mirroring the protocol from spec issue #13.

open System.Text.Json
open Xunit
open Companion.RequestHandler

let private respondTo (requestJson: string) : JsonElement =
    use doc = JsonDocument.Parse(requestJson: string)
    let response = respond doc
    JsonDocument.Parse(JsonSerializer.Serialize response).RootElement.Clone()

[<Fact>]
let ``locate request returns a blocks envelope with one range per block`` () =
    let request =
        JsonSerializer.Serialize(
            {| tag = "locate"
               source = "http {\n    GET \"https://example.com/1\"\n}\n\nhttp {\n    GET \"https://example.com/2\"\n}\n" |}
        )

    let response = respondTo request
    Assert.Equal("blocks", response.GetProperty("tag").GetString())
    let ranges = response.GetProperty("ranges")
    Assert.Equal(2, ranges.GetArrayLength())

    let first = ranges.[0]
    Assert.Equal(1, first.GetProperty("startLine").GetInt32())
    Assert.Equal(0, first.GetProperty("startCol").GetInt32())

    Assert.True(
        first.GetProperty("endLine").GetInt32()
        >= first.GetProperty("startLine").GetInt32()
    )

[<Fact>]
let ``locate request on source with no blocks returns an empty ranges array`` () =
    let request =
        JsonSerializer.Serialize
            {| tag = "locate"
               source = "let x = 1\n" |}

    let response = respondTo request
    Assert.Equal("blocks", response.GetProperty("tag").GetString())
    Assert.Equal(0, response.GetProperty("ranges").GetArrayLength())

[<Fact>]
let ``hello request still returns ready`` () =
    let response = respondTo (JsonSerializer.Serialize {| tag = "hello" |})
    Assert.Equal("ready", response.GetProperty("tag").GetString())

[<Fact>]
let ``unknown request tag returns an error envelope`` () =
    let response = respondTo (JsonSerializer.Serialize {| tag = "not-a-real-tag" |})
    Assert.Equal("error", response.GetProperty("tag").GetString())
