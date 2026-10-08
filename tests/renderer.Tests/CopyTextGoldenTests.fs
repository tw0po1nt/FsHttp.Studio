module Renderer.Tests.CopyTextGoldenTests

open System.IO
open System.Text
open System.Text.Json
open Expecto
open Renderer.Core

let private utf8 (s: string) = Encoding.UTF8.GetBytes s

type private Case = { Name: string; Env: ResponseEnvelope }

let private response status reason headers body request : ResponseEnvelope =
    { Request = request
      Status = status
      Reason = reason
      Headers = headers
      ContentType = ""
      Body = body
      RequestMs = 0.0
      TotalMs = 0.0 }

let private getRequest: RequestView =
    { Method = "GET"
      Url = "https://api.example.com/thing"
      Headers = [ "Accept", "application/json" ]
      ContentType = ""
      Body = NoBody }

let private binaryBytes =
    Array.init 300 (fun i -> if i % 16 = 0 then 0uy else byte (i % 256))

let private cases =
    [ { Name = "a JSON response to a GET with no request body"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "application/json; charset=utf-8"; "Server", "nginx" ]
              (utf8 """{"name":"fs","tags":["a"]}""")
              getRequest }
      { Name = "a POST with a captured JSON body"
        Env =
          response
              201
              "Created"
              [ "Location", "/items/1" ]
              (utf8 "created")
              { Method = "POST"
                Url = "https://api.example.com/items"
                Headers = [ "Content-Type", "application/json"; "Accept-Encoding", "gzip, deflate" ]
                ContentType = "application/json"
                Body = Captured(utf8 """{"name":"fs","tags":["a"]}""") } }
      { Name = "a request body that the companion did not read"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "text/plain" ]
              (utf8 "ok")
              { Method = "POST"
                Url = "https://api.example.com/upload"
                Headers = [ "Content-Type", "multipart/form-data; boundary=x" ]
                ContentType = "multipart/form-data"
                Body = NotCaptured "Body not captured: 5.2 MB exceeds the 1 MB cap" } }
      { Name = "a binary request body and a binary response body"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "application/octet-stream" ]
              binaryBytes
              { Method = "PUT"
                Url = "https://api.example.com/blob"
                Headers = [ "Content-Type", "application/octet-stream" ]
                ContentType = "application/octet-stream"
                Body = Captured binaryBytes } }
      { Name = "a 204 response with no headers and an empty body"
        Env = response 204 "No Content" [] [||] getRequest }
      { Name = "a text body with characters outside ASCII"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "text/plain; charset=utf-8" ]
              (utf8 "café → 日本\r\nsecond line\n")
              getRequest }
      { Name = "a Latin-1 request body and a Latin-1 response body"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "text/plain; charset=iso-8859-1" ]
              [| 99uy; 97uy; 102uy; 0xE9uy |]
              { Method = "POST"
                Url = "https://api.example.com/latin"
                Headers = [ "Content-Type", "text/plain; charset=iso-8859-1" ]
                ContentType = "text/plain"
                Body = Captured [| 110uy; 0xE4uy; 0x69uy; 0x76uy; 0x65uy |] } }
      { Name = "a truncated sequence, a surrogate, and a sequence that the body ends inside"
        Env =
          response
              200
              "OK"
              [ "Content-Type", "text/plain; charset=utf-8" ]
              [| 97uy
                 0xE6uy
                 0x97uy
                 98uy
                 0xEDuy
                 0xA0uy
                 0x80uy
                 99uy
                 0xF0uy
                 0x9Fuy
                 0x98uy |]
              getRequest }
      { Name = "an HTML body"
        Env =
          response
              404
              "Not Found"
              [ "Content-Type", "text/html" ]
              (utf8 "<html><body><h1>hi</h1></body></html>")
              getRequest } ]

let private encode (value: obj) =
    JsonSerializer.SerializeToUtf8Bytes value

let private pairs (headers: (string * string) list) =
    [ for name, value in headers -> [ name; value ] ]

let private fixture () =
    encode
        {| cases =
            [ for c in cases ->
                  let request = c.Env.Request

                  let bodyState, bodyBytes, bodyReason =
                      match request.Body with
                      | NoBody -> "none", [||], ""
                      | Captured bytes -> "captured", Array.map int bytes, ""
                      | NotCaptured reason -> "notCaptured", [||], reason

                  {| body = Array.map int c.Env.Body
                     headers = pairs c.Env.Headers
                     name = c.Name
                     reason = c.Env.Reason
                     request =
                      {| bodyBytes = bodyBytes
                         bodyReason = bodyReason
                         bodyState = bodyState
                         headers = pairs request.Headers
                         method = request.Method
                         url = request.Url |}
                     requestText = Option.toObj (copyText c.Env "request")
                     responseBodyText = Option.toObj (copyText c.Env "response-body")
                     responseHeadersText = Option.toObj (copyText c.Env "response-headers")
                     status = c.Env.Status |} ] |}

[<Tests>]
let tests =
    testList
        "Copy text Golden fixtures"
        [ test "the Copy text matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("copy", "copy-text.json")) (fixture ())
          }

          test "an unknown key has nothing to copy" {
              Expect.equal (copyText (List.head cases).Env "nothing") None "only the three copy keys give a Copy text"
          } ]
