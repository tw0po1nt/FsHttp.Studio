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

/// A canned request view with no body. Each test can override the fields.
let private requestWithNoBody (httpMethod: string) (url: string) =
    { Method = httpMethod
      Url = url
      Headers = []
      ContentType = ""
      Body = NoBody }

/// A canned envelope with a sensible request context. Each test can override the fields.
let private envelope contentType (body: byte[]) =
    { Request = requestWithNoBody "GET" "https://example.com/thing"
      Status = 200
      Reason = "OK"
      Headers = [ "Content-Type", contentType ]
      ContentType = contentType
      Body = body
      RequestMs = 42.0
      TotalMs = 100.0 }

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
        [ test "the status line carries method, URL, colored status, request time, total, and size" {
              let env =
                  { envelope "text/plain" (utf8 "hi") with
                      Request = requestWithNoBody "POST" "https://api.example.com/widgets"
                      RequestMs = 142.0
                      TotalMs = 380.0 }

              let node = render env

              Expect.stringContains (innerText (byClass "status-method" node |> List.exactlyOne)) "POST" "method shown"

              Expect.stringContains
                  (innerText (byClass "status-url" node |> List.exactlyOne))
                  "https://api.example.com/widgets"
                  "URL shown"

              Expect.equal
                  (innerText (byClass "status-time" node |> List.exactlyOne))
                  "142 ms"
                  "status-time shows the request number"

              Expect.equal
                  (innerText (byClass "status-total" node |> List.exactlyOne))
                  "· 380 ms total"
                  "status-total shows the host total, with the separator inside the span"

              Expect.isNonEmpty (byClass "status-size" node) "size shown"

              let statusCode = byClass "status-code" node |> List.exactlyOne
              Expect.isTrue (hasClass "status-2xx" statusCode) "a 200 should be colored as 2xx"
          }

          test "the status line sits above the request, the headers, and the body" {
              // The response wrapper's children are the status line, the Request section, the
              // response headers, and then the body, in that order.
              let node = render (envelope "text/plain" (utf8 "x"))

              match node with
              | Node.Element(_, _, [ statusLine; request; _headers; body ]) ->
                  Expect.isTrue (hasClass "status-line" statusLine) "first child is the status line"
                  Expect.isTrue (hasClass "request" request) "second child is the Request section"
                  Expect.isTrue (hasClass "response-body" body) "last child is the body"
              | _ -> failtest "expected status line, request, headers, and body"
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
let requestSectionTests =
    testList
        "Request section"
        [ test "a Captured JSON body renders a JSON tree inside the Request section" {
              let jsonBody = utf8 """{"name":"posted"}"""

              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { Method = "POST"
                            Url = "https://api.example.com/items"
                            Headers = [ "Content-Type", "application/json" ]
                            ContentType = "application/json"
                            Body = Captured jsonBody } }

              let node = render env
              let request = byClass "request" node |> List.exactlyOne

              Expect.isNonEmpty
                  (byClass "response-json" request)
                  "a Captured JSON request body should render as a JSON tree inside Request"

              Expect.equal
                  (innerText (byClass "headers-summary" request |> List.exactlyOne))
                  (sprintf "Request (%s)" (humanSize jsonBody.Length))
                  "the summary should show the captured body size"
          }

          test "a Captured binary body renders a hex dump" {
              let binaryBody = [| 0uy; 1uy; 2uy; 255uy; 0uy; 128uy |]

              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { Method = "POST"
                            Url = "https://api.example.com/blob"
                            Headers = [ "Content-Type", "application/octet-stream" ]
                            ContentType = "application/octet-stream"
                            Body = Captured binaryBody } }

              let node = render env
              let request = byClass "request" node |> List.exactlyOne

              Expect.isNonEmpty (byClass "hex-dump" request) "a Captured binary body should show a hex dump"
              Expect.isEmpty (byTag "iframe" request) "a request body must not become an HTML preview"
              Expect.isEmpty (byTag "img" request) "a request body must not become an image preview"
          }

          test "a NotCaptured body renders its reason string and no body element" {
              let reason = "streamed body — not captured, so that the upload is unchanged"

              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { requestWithNoBody "POST" "https://api.example.com/upload" with
                              Headers = [ "Content-Type", "application/octet-stream" ]
                              ContentType = "application/octet-stream"
                              Body = NotCaptured reason } }

              let node = render env
              let request = byClass "request" node |> List.exactlyOne
              let note = byClass "binary-note" request |> List.exactlyOne

              Expect.equal (innerText note) reason "NotCaptured should show the reason in binary-note style"
              Expect.isEmpty (byClass "response-json" request) "NotCaptured must not render a body tree"
              Expect.isEmpty (byClass "response-text" request) "NotCaptured must not render a text body"
              Expect.isEmpty (byClass "hex-dump" request) "NotCaptured must not render a hex dump"

              Expect.equal
                  (innerText (byClass "headers-summary" request |> List.exactlyOne))
                  "Request"
                  "no size when uncaptured"
          }

          test "NoBody renders the headers and no body area" {
              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { requestWithNoBody "GET" "https://api.example.com/thing" with
                              Headers = [ "Accept", "application/json"; "X-Trace", "abc" ] } }

              let node = render env
              let request = byClass "request" node |> List.exactlyOne

              Expect.equal (List.length (byClass "header-row" request)) 2 "request headers should render"
              Expect.isEmpty (byClass "binary-note" request) "NoBody must omit the body area"
              Expect.isEmpty (byClass "response-text" request) "NoBody must omit the body area"
              Expect.isEmpty (byClass "response-json" request) "NoBody must omit the body area"
              Expect.isEmpty (byClass "hex-dump" request) "NoBody must omit the body area"

              Expect.equal
                  (innerText (byClass "headers-summary" request |> List.exactlyOne))
                  "Request"
                  "no size when NoBody"
          }

          test "the status line shows the method and URL from env.Request" {
              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request = requestWithNoBody "PUT" "https://api.example.com/items?q=one%20two" }

              let node = render env

              Expect.equal
                  (innerText (byClass "status-method" node |> List.exactlyOne))
                  "PUT"
                  "status method comes from env.Request"

              Expect.equal
                  (innerText (byClass "status-url" node |> List.exactlyOne))
                  "https://api.example.com/items?q=one%20two"
                  "status URL comes from env.Request, with percent-escapes intact"
          }

          test "the Request section renders before the response headers, and is collapsed by default" {
              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { requestWithNoBody "GET" "https://example.com/" with
                              Headers = [ "Accept", "*/*" ] }
                      Headers = [ "Content-Type", "text/plain"; "Server", "kestrel" ] }

              let node = render env

              match node with
              | Node.Element(_, _, [ _; request; headers; _ ]) ->
                  Expect.isTrue (hasClass "request" request) "Request section precedes response headers"
                  Expect.isTrue (hasClass "headers" headers) "response headers follow the Request section"
                  Expect.equal (tag request) (Some "details") "Request is a collapsible <details>"
                  Expect.equal (attr "open" request) None "Request is collapsed by default"
              | _ -> failtest "expected status line, request, headers, and body"
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

/// The copy text for a key that must yield `Some`. It fails the test when the key yields `None`,
/// so a check that reads the text does not repeat the `None` case.
let private copied (env: ResponseEnvelope) (key: string) : string =
    match copyText env key with
    | Some text -> text
    | None -> failtestf "copyText %s must yield Some" key

[<Tests>]
let copyTextTests =
    testList
        "copyText"
        [ test "a JSON body copies the raw UTF-8 bytes, not the tree" {
              let json = """{"name":"fs","tags":["a"]}"""
              let text = copied (envelope "application/json" (utf8 json)) "response-body"

              Expect.equal text json "copy must equal the body decoded as UTF-8"
              Expect.isSome (Renderer.Json.tryParse text) "the result must parse as JSON"
              Expect.isFalse (text.Contains "▸") "copy must not include disclosure markers"
              Expect.isFalse (text.Contains "Object(") "copy must not include the tree summary"
          }

          test "a binary body copies the truncated hex dump as shown" {
              // Over the 256-byte hexDump cap, with NUL bytes so looksBinary wins.
              let body = Array.init 300 (fun i -> if i % 16 = 0 then 0uy else byte (i % 256))
              let env = envelope "application/octet-stream" body
              let shown = innerText (byClass "hex-dump" (renderBody env) |> List.exactlyOne)
              let text = copied env "response-body"

              Expect.equal text shown "copy must equal the rendered hexDump"
              Expect.stringEnds text "… (44 more bytes)" "the truncation note must travel with the paste"
          }

          test "an image/svg+xml body copies the SVG source, not a hex dump" {
              let svg = """<svg xmlns="http://www.w3.org/2000/svg"><circle r="1"/></svg>"""
              let text = copied (envelope "image/svg+xml" (utf8 svg)) "response-body"

              Expect.equal text svg "SVG source must copy as text"
              Expect.isFalse (text.Contains "… (") "SVG must not become a hex dump"
          }

          test "a text/html body copies the page source" {
              let html = "<html><body><h1>hi</h1></body></html>"
              let env = envelope "text/html" (utf8 html)

              Expect.equal (copyText env "response-body") (Some html) "HTML page source must copy as text"
          }

          test "a zero-byte response body yields None" {
              let env = envelope "text/plain" [||]
              Expect.equal (copyText env "response-body") None "204-style empty body has nothing to copy"
          }

          test "response-headers starts with the status line, then Name: value lines in order" {
              let env =
                  { envelope "application/json" (utf8 "{}") with
                      Status = 200
                      Reason = "OK"
                      Headers = [ "Content-Type", "application/json; charset=utf-8"; "Server", "nginx" ] }

              Expect.equal
                  (copyText env "response-headers")
                  (Some "200 OK\nContent-Type: application/json; charset=utf-8\nServer: nginx")
                  "status line, then headers in order"
          }

          test "response-headers with no headers is still Some, with just the status line" {
              let env =
                  { envelope "text/plain" (utf8 "x") with
                      Status = 204
                      Reason = "No Content"
                      Headers = [] }

              Expect.equal (copyText env "response-headers") (Some "204 No Content") "status line alone"
          }

          test "request starts with method and URL, and separates headers from body with a blank line" {
              let body = """{"name":"fs","tags":["a"]}"""

              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { Method = "POST"
                            Url = "https://api.example.com/items"
                            Headers = [ "Content-Type", "application/json"; "Accept-Encoding", "gzip, deflate" ]
                            ContentType = "application/json"
                            Body = Captured(utf8 body) } }

              Expect.equal
                  (copyText env "request")
                  (Some(
                      "POST https://api.example.com/items\n"
                      + "Content-Type: application/json\n"
                      + "Accept-Encoding: gzip, deflate\n"
                      + "\n"
                      + body
                  ))
                  "method URL, headers, blank line, then body"
          }

          test "a NoBody request copies the request line and headers, and ends after the last header" {
              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { requestWithNoBody "GET" "https://api.example.com/thing" with
                              Headers = [ "Accept", "application/json"; "X-Trace", "abc" ] } }

              Expect.equal
                  (copyText env "request")
                  (Some "GET https://api.example.com/thing\nAccept: application/json\nX-Trace: abc")
                  "NoBody ends after the last header, with no trailing blank line"
          }

          test "a NotCaptured request copies the reason in the body's position" {
              let reason = "Body not captured: 5.2 MB exceeds the 1 MB cap"

              let env =
                  { envelope "text/plain" (utf8 "ok") with
                      Request =
                          { requestWithNoBody "POST" "https://api.example.com/upload" with
                              Headers = [ "Content-Type", "multipart/form-data; boundary=abc" ]
                              ContentType = "multipart/form-data; boundary=abc"
                              Body = NotCaptured reason } }

              Expect.equal
                  (copyText env "request")
                  (Some(
                      "POST https://api.example.com/upload\n"
                      + "Content-Type: multipart/form-data; boundary=abc\n"
                      + "\n"
                      + reason
                  ))
                  "NotCaptured puts its reason where the body would be"
          }

          test "an unknown key yields None" {
              let env = envelope "text/plain" (utf8 "x")
              Expect.equal (copyText env "nope") None "only the three known keys are defined"
          } ]
