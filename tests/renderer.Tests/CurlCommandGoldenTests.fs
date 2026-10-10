module Renderer.Tests.CurlCommandGoldenTests

open System
open System.IO
open System.Text
open System.Text.Json
open Expecto
open Renderer.Core

let private utf8 (s: string) = Encoding.UTF8.GetBytes s

type private Case = { Name: string; Request: RequestView }

/// The `--connect-to` of the replay test needs a URL without TLS.
let private baseUrl = "http://api.example.com"

let private request httpMethod path headers body : RequestView =
    { Method = httpMethod
      Url = baseUrl + path
      Headers = headers
      ContentType = ""
      Body = body }

let private exactly16384Bytes = String.replicate 1024 "abcdefghijklmno\n" |> utf8

let private pngSignature = Array.append [| 0x89uy |] (utf8 "PNG\r\n\u001a\n")

let private multipartBody =
    Array.concat
        [ utf8 "--fshttp-boundary\r\nContent-Disposition: form-data; name=\"note\"\r\n\r\nhello\r\n"
          utf8 "--fshttp-boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"pixel.png\"\r\n"
          utf8 "Content-Type: image/png\r\n\r\n"
          pngSignature
          [| 0x00uy; 0xFFuy |]
          utf8 "\r\n--fshttp-boundary--\r\n" ]

let private cases =
    [ { Name = "get-no-body"
        Request = request "GET" "/items" [ "Accept", "application/json" ] NoBody }
      { Name = "post-json-inline"
        Request =
          request
              "POST"
              "/items"
              [ "Accept", "application/json"; "Content-Type", "application/json" ]
              (Captured(utf8 """{"name":"snorlax"}""")) }
      { Name = "get-with-body"
        Request = request "GET" "/search" [ "Content-Type", "application/json" ] (Captured(utf8 """{"q":"fs"}""")) }
      { Name = "head"
        Request = request "HEAD" "/items/7" [ "Accept", "application/json" ] NoBody }
      { Name = "head-with-body"
        Request = request "HEAD" "/items/7" [ "Content-Type", "text/plain" ] (Captured(utf8 "hello")) }
      { Name = "put-no-body"
        Request = request "PUT" "/items/7" [ "Accept", "application/json" ] NoBody }
      { Name = "url-glob-chars"
        Request = request "GET" "/search?filter[name]=fs&tags={a,b}" [ "Accept", "application/json" ] NoBody }
      { Name = "drops-content-length"
        Request =
          request
              "POST"
              "/notes"
              [ "Content-Type", "text/plain; charset=utf-8"; "Content-Length", "5" ]
              (Captured(utf8 "hello")) }
      { Name = "body-no-content-type"
        Request = request "POST" "/notes" [ "Accept", "text/plain" ] (Captured(utf8 "hello")) }
      { Name = "value-single-quote"
        Request = request "GET" "/items" [ "X-Note", "it's a 'quoted' value" ] NoBody }
      { Name = "header-empty-value"
        Request = request "GET" "/items" [ "Accept", "application/json"; "X-Empty", "" ] NoBody }
      { Name = "body-single-quote"
        Request =
          request
              "POST"
              "/items"
              [ "Content-Type", "application/json" ]
              (Captured(utf8 """{"name":"o'brien","note":"it's"}""")) }
      { Name = "body-leading-at"
        Request = request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured(utf8 "@/etc/passwd")) }
      { Name = "body-16384-bytes"
        Request = request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured exactly16384Bytes) }
      { Name = "body-16385-bytes"
        Request =
          request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured(Array.append exactly16384Bytes (utf8 "a"))) }
      { Name = "body-crlf"
        Request = request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured(utf8 "line one\r\nline two\r\n")) }
      { Name = "body-multipart"
        Request =
          request
              "POST"
              "/upload"
              [ "Content-Type", "multipart/form-data; boundary=fshttp-boundary" ]
              (Captured multipartBody) }
      { Name = "body-nul"
        Request =
          request
              "PUT"
              "/blobs/7"
              [ "Content-Type", "application/octet-stream" ]
              (Captured [| 0x66uy; 0x00uy; 0x73uy; 0x00uy; 0x00uy |]) }
      { Name = "body-invalid-utf8"
        Request =
          request
              "POST"
              "/notes"
              [ "Content-Type", "text/plain; charset=iso-8859-1" ]
              (Captured [| 0x63uy; 0x61uy; 0x66uy; 0xE9uy |]) }
      { Name = "body-control-byte"
        Request = request "POST" "/notes" [] (Captured(utf8 "red \u001b[31mtext\u001b[0m")) }
      { Name = "not-captured"
        Request =
          request
              "POST"
              "/upload"
              [ "Content-Type", "application/octet-stream" ]
              (NotCaptured "streamed body: not captured, so that the upload is unchanged") } ]

let private envelopeFor (request: RequestView) : ResponseEnvelope =
    { Request = request
      Status = 200
      Reason = "OK"
      Headers = []
      ContentType = ""
      Body = [||]
      RequestMs = 0.0
      TotalMs = 0.0 }

let private fixture () =
    JsonSerializer.SerializeToUtf8Bytes
        {| cases =
            [ for c in cases ->
                  let bodyState, bodyBytes, bodyReason =
                      match c.Request.Body with
                      | NoBody -> "none", [||], ""
                      | Captured bytes -> "captured", Array.map int bytes, ""
                      | NotCaptured reason -> "notCaptured", [||], reason

                  {| curl = Option.toObj (copyText (envelopeFor c.Request) "curl")
                     name = c.Name
                     request =
                      {| bodyBytes = bodyBytes
                         bodyReason = bodyReason
                         bodyState = bodyState
                         headers = [ for name, value in c.Request.Headers -> [ name; value ] ]
                         method = c.Request.Method
                         url = c.Request.Url |} |} ] |}

let private curlFor (name: string) =
    let c = cases |> List.find (fun c -> c.Name = name)
    copyText (envelopeFor c.Request) "curl"

let private notesPipeCommand (base64: string) =
    "printf '%s' '"
    + base64
    + "' \\\n"
    + "  | base64 -d \\\n"
    + "  | curl 'http://api.example.com/notes' \\\n"
    + "    -H 'Content-Type: text/plain' \\\n"
    + "    -H 'User-Agent:' \\\n"
    + "    -H 'Accept:' \\\n"
    + "    --data-binary @-"

[<Tests>]
let tests =
    testList
        "Curl command Golden fixtures"
        [ test "the Curl command matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("curl", "curl-command.json")) (fixture ())
          }

          test "a POST with an inline JSON body has one argument on each line" {
              Expect.equal
                  (curlFor "post-json-inline")
                  (Some(
                      "curl 'http://api.example.com/items' \\\n"
                      + "  -H 'Accept: application/json' \\\n"
                      + "  -H 'Content-Type: application/json' \\\n"
                      + "  -H 'User-Agent:' \\\n"
                      + "  --data-raw '{\"name\":\"snorlax\"}'"
                  ))
                  "no method flag for a POST with a body, and the body goes inline"
          }

          test "a single quote in a value closes the quotes, escapes the quote, and opens them again" {
              Expect.equal
                  (curlFor "value-single-quote")
                  (Some(
                      "curl 'http://api.example.com/items' \\\n"
                      + "  -H 'X-Note: it'\\''s a '\\''quoted'\\'' value' \\\n"
                      + "  -H 'User-Agent:' \\\n"
                      + "  -H 'Accept:'"
                  ))
                  "each single quote becomes '\\''"
          }

          test "the command removes each header that curl adds and the Run did not send" {
              Expect.equal
                  (curlFor "get-no-body")
                  (Some(
                      "curl 'http://api.example.com/items' \\\n"
                      + "  -H 'Accept: application/json' \\\n"
                      + "  -H 'User-Agent:'"
                  ))
                  "the Run sent an Accept, so only User-Agent is removed"
          }

          test "a header with an empty value goes out with a semicolon" {
              Expect.stringContains
                  (curlFor "header-empty-value" |> Option.defaultValue "")
                  "  -H 'X-Empty;' \\\n"
                  "curl removes a header that has a colon and an empty value"
          }

          test "a HEAD with a body has no Curl command" {
              Expect.isNone (curlFor "head-with-body") "curl refuses --head with a data flag"
          }

          test "a method that is not a plain token is in single quotes" {
              let env = envelopeFor (request "BAD METHOD" "/items" [] NoBody)

              Expect.stringStarts
                  (copyText env "curl" |> Option.defaultValue "")
                  "curl -X 'BAD METHOD' 'http://api.example.com/items' \\\n"
                  "the shell gets the method as one argument"
          }

          test "a body that the companion did not read has no Curl command" {
              Expect.isNone (curlFor "not-captured") "a Curl command would send a different request"
          }

          test "a body that is not safe to paste goes through the base64 pipe" {
              Expect.equal
                  (curlFor "body-crlf")
                  (Some(notesPipeCommand "bGluZSBvbmUNCmxpbmUgdHdvDQo="))
                  "printf, base64 -d, and curl are on separate lines, and each curl argument is on its own line"
          }

          test "a Captured body that is not safe to paste, or above 16,384 bytes, gives the base64 pipe" {
              let unsafeBodies =
                  [ "a CR", utf8 "a\r\nb"
                    "a NUL", [| 97uy; 0uy; 98uy |]
                    "a control byte", [| 97uy; 0x1Buy; 98uy |]
                    "a DEL byte", [| 97uy; 0x7Fuy; 98uy |]
                    "the first C1 control character", [| 97uy; 0xC2uy; 0x80uy; 98uy |]
                    "the last C1 control character", [| 97uy; 0xC2uy; 0x9Fuy; 98uy |]
                    "invalid UTF-8", [| 110uy; 0xE4uy; 0x69uy |]
                    "an overlong form", [| 0xC0uy; 0xAFuy |]
                    "a surrogate", [| 0xEDuy; 0xA0uy; 0x80uy |]
                    "a sequence that the body ends inside", [| 97uy; 0xF0uy; 0x9Fuy; 0x98uy |]
                    "16,385 bytes", Array.append exactly16384Bytes [| 97uy |] ]

              for description, bytes in unsafeBodies do
                  let env =
                      envelopeFor (request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured bytes))

                  Expect.equal (copyText env "curl") (Some(notesPipeCommand (Convert.ToBase64String bytes))) description
          }

          test "a body with characters outside ASCII, U+00A0, a tab, and an LF is safe to paste" {
              let body = "café\u00A0→ 日本 😀\tsecond line\n"

              let env =
                  envelopeFor (request "POST" "/notes" [ "Content-Type", "text/plain" ] (Captured(utf8 body)))

              Expect.equal
                  (copyText env "curl")
                  (Some(
                      "curl 'http://api.example.com/notes' \\\n"
                      + "  -H 'Content-Type: text/plain' \\\n"
                      + "  -H 'User-Agent:' \\\n"
                      + "  -H 'Accept:' \\\n"
                      + "  --data-raw '"
                      + body
                      + "'"
                  ))
                  "the body goes inline as it is"
          } ]
