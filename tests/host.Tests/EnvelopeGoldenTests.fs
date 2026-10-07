module Extension.Tests.EnvelopeGoldenTests

// The companion's own encoder writes each envelope, so each Golden fixture holds the bytes that
// cross the wire. The Lua client must decode and encode these same bytes.

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Companion.BlockLocator
open Companion.BlockRunner
open Companion.RequestCapture

let private encode = Companion.Envelope.encode

let private respond (request: byte[]) : byte[] =
    use doc = JsonDocument.Parse request
    encode (Companion.RequestHandler.respond doc)

// The text holds each character class that the encoder escapes, and UTF-8 of two, three, and four
// bytes.
let private escapes =
    "\"quoted\" \\ <tag> & 'single' + `tick` \t\n\r\b\f\u0001\u007f é → 😀"

let private script =
    "#r \"nuget: FsHttp\"\nopen FsHttp\n\nhttp {\n    GET \"https://example.com/pokemon/é\"\n}\n\nfor i in 1 .. 3 do\n    http {\n        GET \"https://example.com/\"\n    }\n    |> ignore\n"

let private hello = encode {| tag = "hello" |}

let private locate = encode {| tag = "locate"; source = script |}

// The second Block sits in a loop body, so the companion refuses it before it evaluates code.
let private run =
    encode
        {| tag = "run"
           source = script
           blockIndex = 1
           scriptFileName = "/scripts/golden.fsx"
           timeoutMs = 30000 |}

let private request method url headers body =
    { Method = method
      Url = url
      Headers = headers
      Body = body }

let private response status reason headers contentType body requestMs =
    { Status = status
      Reason = reason
      Headers = headers
      ContentType = contentType
      BodyBase64 = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(body: string))
      RequestMs = requestMs }

let private ok =
    Ok(
        request
            "POST"
            "https://example.com/pokemon?name=snorlax&limit=1"
            [ "Content-Type", "application/json; charset=utf-8"
              "Accept", "application/json" ]
            (Captured(Encoding.UTF8.GetBytes "{\"name\":\"snorlax\"}")),
        response
            201
            "Created"
            [ "Content-Type", "application/json; charset=utf-8"; "X-Escapes", escapes ]
            "application/json; charset=utf-8"
            "{\"id\":143,\"name\":\"snorlax\"}"
            12.5
    )

let private httpErrorResponse =
    Ok(
        request "GET" "https://example.com/missing" [] NoBody,
        response 404 "Not Found" [ "Content-Type", "text/plain" ] "text/plain" "no such pokemon" 3.0
    )

let private notCaptured =
    Ok(
        request
            "PUT"
            "https://example.com/upload"
            [ "Content-Type", "application/octet-stream" ]
            (NotCaptured "The body is a stream, and a read would change what goes on the wire."),
        response 204 "No Content" [] "" "" 0.30000000000000004
    )

let private compileError =
    CompileError
        [ { Message = "The value or constructor 'baseUrl' is not defined."
            Range =
              { StartLine = 5
                StartCol = 8
                EndLine = 5
                EndCol = 15 } }
          { Message = escapes
            Range =
              { StartLine = 7
                StartCol = 0
                EndLine = 9
                EndCol = 1 } } ]

/// `frames.bin` holds the frame of each envelope Golden fixture in this order.
let private goldenFixtures =
    [ "hello", hello
      "ready", encode (Companion.RequestHandler.ready "1.2.3-beta.4")
      "locate", locate
      "blocks", respond locate
      "run", run
      "refused", respond run
      "refused-unbound-block-value", encode (outcomeToWire (Refused("unboundBlockValue", Some "dexId")))
      "ok", encode (outcomeToWire ok)
      "ok-http-error-response", encode (outcomeToWire httpErrorResponse)
      "ok-not-captured", encode (outcomeToWire notCaptured)
      "compile-error", encode (outcomeToWire compileError)
      "runtime-error", encode (outcomeToWire (RuntimeError escapes))
      "error", respond (encode {| tag = "notATag" |}) ]

let private frames () =
    use stream = new MemoryStream()

    for _, payload in goldenFixtures do
        Companion.Envelope.writeFrame stream payload

    stream.ToArray()

let private tagOf (payload: byte[]) =
    use doc = JsonDocument.Parse payload
    doc.RootElement.GetProperty("tag").GetString()

[<Tests>]
let tests =
    testList
        "Envelope Golden fixtures"
        [ for name, payload in goldenFixtures do
              test (sprintf "%s matches its Golden fixture" name) {
                  GoldenFixture.verify (Path.Combine("envelope", name + ".json")) payload
              }

          test "frames.bin holds the frame of each envelope Golden fixture" {
              GoldenFixture.verify (Path.Combine("envelope", "frames.bin")) (frames ())
          }

          test "the Golden fixtures cover each envelope tag" {
              let tags = goldenFixtures |> List.map (snd >> tagOf) |> Set.ofList

              Expect.equal
                  tags
                  (set
                      [ "hello"
                        "ready"
                        "locate"
                        "blocks"
                        "run"
                        "ok"
                        "compileError"
                        "runtimeError"
                        "refused"
                        "error" ])
                  "each tag needs a Golden fixture"
          }

          test "the blocks Golden fixture holds a Block that a Run can reach and a refused Block" {
              use doc = JsonDocument.Parse(respond locate)
              let ranges = doc.RootElement.GetProperty "ranges"

              Expect.equal (ranges.GetArrayLength()) 2 "the script holds two Blocks"
              Expect.isFalse (fst (ranges.[0].TryGetProperty "refusal")) "the first Block is reachable"
              Expect.equal (ranges.[1].GetProperty("refusal").GetString()) "loopBody" "the second Block is in a loop"
          }

          test "the refused Golden fixture is the companion's answer to the run Golden fixture" {
              use doc = JsonDocument.Parse(respond run)
              Expect.equal (doc.RootElement.GetProperty("code").GetString()) "loopBody" "a loop body is refused"
          } ]
