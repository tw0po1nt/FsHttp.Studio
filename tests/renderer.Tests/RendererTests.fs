module Renderer.Tests.RendererTests

// Seam B. It drives canned response envelopes through the renderer core, and asserts the shape
// of the resulting `Node` tree, with no VSCode, no browser, and no DOM. Each Content-Type must
// dispatch to the expected element. A non-2xx response must render its body honestly and show
// the code. A body that does not decode must reach the size-and-hex fallback, and must not throw.

open System.Text
open Expecto
open Renderer.Core
open Renderer.NodeQuery

let private utf8 (s: string) = Encoding.UTF8.GetBytes s

/// A canned envelope with a sensible request context. Each test can override the fields.
let private envelope contentType (body: byte[]) =
    { Method = "GET"
      Url = "https://example.com/thing"
      Status = 200
      Reason = "OK"
      Headers = [ "Content-Type", contentType ]
      ContentType = contentType
      Body = body
      ElapsedMs = 42.0 }

let private pngBytes =
    Array.append [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |] [| 1uy; 2uy; 3uy |]

[<Tests>]
let dispatchTests =
    testList
        "renderBody Content-Type dispatch"
        [ test "an image dispatches to an <img> with a data: URI carrying the content type and base64 body" {
              let node = renderBody (envelope "image/png" pngBytes)

              match byTag "img" node with
              | [ img ] ->
                  let src = attr "src" img |> Option.defaultValue ""
                  Expect.stringStarts src "data:image/png;base64," "src should be a data URI for the image type"

                  let expectedBase64 = System.Convert.ToBase64String pngBytes
                  Expect.stringContains src expectedBase64 "the data URI should carry the body's base64"
              | other -> failtestf "expected exactly one <img>, got %d" (List.length other)
          }

          test "an image content type with parameters still yields a clean data: media type" {
              let node = renderBody (envelope "image/svg+xml; charset=utf-8" (utf8 "<svg/>"))
              let img = byTag "img" node |> List.exactlyOne
              let src = attr "src" img |> Option.defaultValue ""
              Expect.stringStarts src "data:image/svg+xml;base64," "parameters should be stripped from the media type"
          }

          test "JSON dispatches to a collapsible, highlighted tree" {
              let node =
                  renderBody (envelope "application/json" (utf8 """{"name":"fs","tags":[true,null,"x"],"n":42}"""))

              Expect.isNonEmpty (byClass "response-json" node) "should be a JSON render, not the text fallback"
              Expect.isNonEmpty (byTag "details" node) "objects/arrays should be collapsible <details> nodes"

              let keyTexts = byClass "json-key" node |> List.map innerText
              Expect.contains keyTexts "\"name\"" "object keys should be highlighted json-key spans"
              Expect.contains keyTexts "\"tags\"" "nested keys should be present"

              Expect.isNonEmpty (byClass "json-number" node) "numbers should be highlighted"
              Expect.isNonEmpty (byClass "json-bool" node) "booleans should be highlighted"
              Expect.isNonEmpty (byClass "json-null" node) "nulls should be highlighted"
              Expect.isNonEmpty (byClass "json-string" node) "strings should be highlighted"
          }

          test "a JSON string value with special characters is re-escaped, not shown raw" {
              let node =
                  renderBody (envelope "application/json" (utf8 """{"msg":"say \"hi\"\n"}"""))

              let strings = byClass "json-string" node |> List.map innerText

              Expect.contains
                  strings
                  "\"say \\\"hi\\\"\\n\""
                  "embedded quotes and newlines should re-escape into a JSON literal"
          }

          test "a +json suffix content type is treated as JSON" {
              let node = renderBody (envelope "application/vnd.api+json" (utf8 """{"ok":true}"""))
              Expect.isNonEmpty (byClass "response-json" node) "+json suffix should dispatch to the JSON tree"
          }

          test "malformed JSON falls back to text rather than throwing" {
              let node = renderBody (envelope "application/json" (utf8 "{not valid json"))
              Expect.isEmpty (byClass "response-json" node) "malformed JSON should not produce a tree"
              Expect.isNonEmpty (byClass "response-text" node) "malformed JSON should render honestly as text"
          }

          test "HTML dispatches to a sandboxed iframe carrying the source in srcdoc" {
              let html = "<h1>Hello</h1>"
              let node = renderBody (envelope "text/html; charset=utf-8" (utf8 html))

              match byTag "iframe" node with
              | [ iframe ] ->
                  Expect.equal
                      (attr "sandbox" iframe)
                      (Some "")
                      "the iframe must be sandboxed (empty sandbox = no scripts)"

                  Expect.equal (attr "srcdoc" iframe) (Some html) "the HTML source should render via srcdoc"
              | other -> failtestf "expected exactly one <iframe>, got %d" (List.length other)
          }

          test "plain text dispatches to wrapped monospace <pre>" {
              let node = renderBody (envelope "text/plain" (utf8 "just some text"))
              let pre = byClass "response-text" node |> List.exactlyOne
              Expect.equal (tag pre) (Some "pre") "text should render in a <pre>"
              Expect.equal (innerText pre) "just some text" "the text body should be shown verbatim"
              Expect.isEmpty (byTag "iframe" node) "text must not become an iframe"
              Expect.isEmpty (byTag "img" node) "text must not become an image"
          }

          test "XML falls through to the text fallback (no XML pretty-rendering in v0.1)" {
              let node = renderBody (envelope "application/xml" (utf8 "<root><child/></root>"))
              Expect.isNonEmpty (byClass "response-text" node) "XML should use the text fallback"
          }

          test "an unknown text-like content type uses the text fallback" {
              let node = renderBody (envelope "application/x-made-up" (utf8 "readable content"))
              Expect.isNonEmpty (byClass "response-text" node) "unknown-but-decodable should render as text"
          } ]

[<Tests>]
let binaryTests =
    testList
        "binary / undecodable fallback"
        [ test "a body with NUL bytes hits the non-crashing size/hex fallback" {
              let body = [| 0uy; 1uy; 2uy; 255uy; 0uy; 128uy |]
              let node = renderBody (envelope "application/octet-stream" body)

              Expect.isNonEmpty (byClass "response-binary" node) "undecodable bytes should hit the binary fallback"
              Expect.isNonEmpty (byClass "hex-dump" node) "the fallback should show a hex dump"
              Expect.isEmpty (byClass "response-text" node) "binary must not be forced through the text renderer"
          }

          test "the binary fallback shows the size" {
              let body = Array.append [| 0uy |] (Array.replicate 2048 7uy)
              let node = renderBody (envelope "application/octet-stream" body)
              let note = byClass "binary-note" node |> List.exactlyOne

              Expect.stringContains
                  (innerText note)
                  (humanSize body.Length)
                  "the size should be shown on the binary fallback"
          }

          test "an unknown content type over genuinely binary bytes does not throw and falls back" {
              // For example, a wrong content type, or no content type, on image-like bytes.
              let body = Array.append pngBytes [| 0uy; 0uy; 200uy; 3uy |]
              let node = renderBody (envelope "" body)

              Expect.isNonEmpty
                  (byClass "response-binary" node)
                  "binary bytes with no content type should hit the fallback"
          } ]

[<Tests>]
let httpErrorTests =
    testList
        "non-2xx HTTP error responses"
        [ test "a non-2xx response renders its body honestly with the code shown" {
              let env =
                  { envelope "application/json" (utf8 """{"error":"not found"}""") with
                      Status = 404
                      Reason = "Not Found" }

              let node = render env

              let statusCode = byClass "status-code" node |> List.exactlyOne
              Expect.stringContains (innerText statusCode) "404" "the status code must be shown"

              Expect.isNonEmpty
                  (byClass "response-json" node)
                  "the error body should still render as its Content-Type (JSON)"
          }

          test "a 500 with a text body renders the body, not an error wrapper" {
              let env =
                  { envelope "text/plain" (utf8 "internal boom") with
                      Status = 500
                      Reason = "Internal Server Error" }

              let node = render env

              Expect.stringContains
                  (innerText (byClass "status-code" node |> List.exactlyOne))
                  "500"
                  "the code should show"

              Expect.stringContains
                  (innerText (byClass "response-text" node |> List.exactlyOne))
                  "internal boom"
                  "the body should show verbatim"
          } ]

[<Tests>]
let statusLineTests =
    testList
        "status line + headers"
        [ test "the status line carries method, URL, colored status, round-trip time, and size" {
              let env =
                  { envelope "text/plain" (utf8 "hi") with
                      Method = "POST"
                      Url = "https://api.example.com/widgets" }

              let node = render env

              Expect.stringContains (innerText (byClass "status-method" node |> List.exactlyOne)) "POST" "method shown"

              Expect.stringContains
                  (innerText (byClass "status-url" node |> List.exactlyOne))
                  "https://api.example.com/widgets"
                  "URL shown"

              Expect.stringContains
                  (innerText (byClass "status-time" node |> List.exactlyOne))
                  "ms"
                  "round-trip time shown"

              Expect.isNonEmpty (byClass "status-size" node) "size shown"

              let statusCode = byClass "status-code" node |> List.exactlyOne
              Expect.isTrue (hasClass "status-2xx" statusCode) "a 200 should be colored as 2xx"
          }

          test "the status line sits above the body" {
              // The response wrapper's children are the status line, the headers, and then the
              // body, in that order.
              let node = render (envelope "text/plain" (utf8 "x"))

              match node with
              | Node.Element(_, _, [ statusLine; _headers; body ]) ->
                  Expect.isTrue (hasClass "status-line" statusLine) "first child is the status line"
                  Expect.isTrue (hasClass "response-body" body) "last child is the body"
              | _ -> failtest "expected the response wrapper to hold status line, headers, and body"
          }

          test "headers live in a collapsible section, one row per header" {
              let env =
                  { envelope "text/plain" (utf8 "x") with
                      Headers = [ "Content-Type", "text/plain"; "X-Custom", "hello"; "Server", "kestrel" ] }

              let node = render env

              let headers = byClass "headers" node |> List.exactlyOne
              Expect.equal (tag headers) (Some "details") "headers should be a collapsible <details>"
              Expect.equal (List.length (byClass "header-row" node)) 3 "one row per header"

              let names = byClass "header-name" node |> List.map innerText
              Expect.contains names "X-Custom" "each header's name should be shown"
          } ]

[<Tests>]
let statusClassTests =
    testList
        "statusClass binning"
        [ test "status codes bin to their color class" {
              Expect.equal (statusClass 200) "status-2xx" "2xx"
              Expect.equal (statusClass 301) "status-3xx" "3xx"
              Expect.equal (statusClass 404) "status-4xx" "4xx"
              Expect.equal (statusClass 500) "status-5xx" "5xx"
              Expect.equal (statusClass 100) "status-other" "1xx falls to other"
          } ]
