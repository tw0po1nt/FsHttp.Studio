module Companion.Tests.RequestCaptureTests

// Seam 1 of docs/spec/0012-request-as-sent.md (tests 1-5): the body-capture rule. Drives
// `captureRequest` with hand-built `HttpRequestMessage` values, and sends through `TestServer`
// to prove the capture never changes what the server receives.

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Text
open Expecto
open Companion.RequestCapture
open Companion.BlockRunner
open Companion.Tests.TestServer

let private utf8 (s: string) = Encoding.UTF8.GetBytes s

let private formContent (pairs: (string * string) list) =
    new FormUrlEncodedContent(pairs |> List.map KeyValuePair)

/// Sends `makeMsg url` to that URL. When `withCapture` is set, runs `captureRequest` on the
/// message first — the same transform `httpMessageTransformers` applies at send time.
let private sendOnce (withCapture: bool) (url: string) (makeMsg: string -> HttpRequestMessage) =
    use client = new HttpClient()
    use msg = makeMsg url

    if withCapture then
        captureRequest msg |> ignore

    client.SendAsync(msg).Result.EnsureSuccessStatusCode() |> ignore

/// For one body shape: send once plain, once with capture, and assert the server saw the same
/// bytes both times. Also returns the body `captureRequest` stored for a fresh message of the
/// same shape.
let private assertWireUnchanged (makeMsg: string -> HttpRequestMessage) : CapturedBody =
    let received = ResizeArray<byte[]>()

    use server = new TestServer(Map [ "/probe", recordingHandler received ])
    let url = server.BaseUrl + "/probe"

    sendOnce false url makeMsg
    sendOnce true url makeMsg

    Expect.equal received.Count 2 "plain and captured sends should each hit the server"

    Expect.equal received.[1] received.[0] "capture must not change the bytes the server receives"

    use probe = makeMsg "http://example.invalid/"
    captureRequest probe |> ignore

    match tryGetCapturedBody probe with
    | Some body -> body
    | None -> failtest "captureRequest should store an entry for the message it just saw"

[<Tests>]
let tests =
    testList
        "RequestCapture"
        [ testList
              "isStreamed"
              [ test "returns false for StringContent, ByteArrayContent, FormUrlEncodedContent, and a text multipart" {
                    Expect.isFalse (isStreamed (new StringContent("hi"))) "StringContent"
                    Expect.isFalse (isStreamed (new ByteArrayContent(utf8 "hi"))) "ByteArrayContent"

                    Expect.isFalse (isStreamed (formContent [ "a", "1" ])) "FormUrlEncodedContent"

                    use mp = new MultipartFormDataContent()
                    mp.Add(new StringContent("part"), "field")
                    Expect.isFalse (isStreamed mp) "multipart of text parts"
                }

                test "returns true for StreamContent and a multipart that contains one" {
                    use stream = new MemoryStream(utf8 "streamed")
                    Expect.isTrue (isStreamed (new StreamContent(stream))) "StreamContent"

                    use mp = new MultipartFormDataContent()
                    mp.Add(new StringContent("text"), "field")
                    mp.Add(new StreamContent(new MemoryStream(utf8 "file")), "file", "f.bin")
                    Expect.isTrue (isStreamed mp) "multipart containing StreamContent"
                } ]

          testList
              "wire identity"
              [ test "GET with no body is unchanged and stores NoBody" {
                    let body =
                        assertWireUnchanged (fun url -> new HttpRequestMessage(HttpMethod.Get, url))

                    Expect.equal body NoBody "no content means NoBody"
                }

                test "json StringContent is unchanged and captured" {
                    let payload = utf8 """{"a":1}"""

                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            m.Content <- new StringContent("""{"a":1}""", Encoding.UTF8, "application/json")
                            m)

                    match body with
                    | Captured bytes -> Expect.equal bytes payload "json bytes"
                    | other -> failtestf "expected Captured, got %A" other
                }

                test "binary ByteArrayContent is unchanged and captured" {
                    let payload = [| 0uy; 1uy; 2uy; 255uy |]

                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            m.Content <- new ByteArrayContent(payload)
                            m)

                    match body with
                    | Captured bytes -> Expect.equal bytes payload "binary bytes"
                    | other -> failtestf "expected Captured, got %A" other
                }

                test "FormUrlEncodedContent is unchanged and captured" {
                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            m.Content <- formContent [ "a", "1"; "b", "two" ]
                            m)

                    match body with
                    | Captured bytes ->
                        let text = Encoding.UTF8.GetString bytes
                        Expect.stringContains text "a=1" "form field a"
                        Expect.stringContains text "b=two" "form field b"
                    | other -> failtestf "expected Captured, got %A" other
                }

                test "multipart with a text part is unchanged and captured" {
                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            let mp = new MultipartFormDataContent("test-boundary")
                            mp.Add(new StringContent("hello"), "field")
                            m.Content <- mp
                            m)

                    match body with
                    | Captured bytes ->
                        let text = Encoding.UTF8.GetString bytes
                        Expect.stringContains text "hello" "part body"
                        Expect.stringContains text "field" "part name"
                    | other -> failtestf "expected Captured, got %A" other
                }

                test "multipart with a StreamContent file part is unchanged and not captured" {
                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            let mp = new MultipartFormDataContent("test-boundary")
                            mp.Add(new StringContent("meta"), "field")
                            mp.Add(new StreamContent(new MemoryStream(utf8 "file-bytes")), "file", "f.bin")
                            m.Content <- mp
                            m)

                    match body with
                    | NotCaptured reason -> Expect.equal reason streamedBodyReason "streamed multipart"
                    | other -> failtestf "expected NotCaptured, got %A" other
                }

                test "StreamContent body file is unchanged and not captured" {
                    let dir =
                        Path.Combine(Path.GetTempPath(), "fshttp-studio-capture-" + Guid.NewGuid().ToString("N"))

                    Directory.CreateDirectory dir |> ignore
                    let path = Path.Combine(dir, "upload.bin")

                    try
                        File.WriteAllBytes(path, utf8 "from-file")

                        let body =
                            assertWireUnchanged (fun url ->
                                let m = new HttpRequestMessage(HttpMethod.Post, url)
                                m.Content <- new StreamContent(File.OpenRead path)
                                m)

                        match body with
                        | NotCaptured reason -> Expect.equal reason streamedBodyReason "body file"
                        | other -> failtestf "expected NotCaptured, got %A" other
                    finally
                        try
                            Directory.Delete(dir, true)
                        with _ ->
                            ()
                }

                test "StreamContent body stream is unchanged and not captured" {
                    let body =
                        assertWireUnchanged (fun url ->
                            let m = new HttpRequestMessage(HttpMethod.Post, url)
                            m.Content <- new StreamContent(new MemoryStream(utf8 "from-stream"))
                            m)

                    match body with
                    | NotCaptured reason -> Expect.equal reason streamedBodyReason "body stream"
                    | other -> failtestf "expected NotCaptured, got %A" other
                } ]

          test "a body at the 1 MB cap is captured; over the cap is NotCaptured and still sent in full" {
              let atCap = Array.zeroCreate 1_048_576
              let overCap = Array.zeroCreate (1_048_576 + 1)
              atCap.[0] <- 7uy
              overCap.[0] <- 9uy

              let received = ResizeArray<byte[]>()
              use server = new TestServer(Map [ "/probe", recordingHandler received ])
              let url = server.BaseUrl + "/probe"

              let sendBytes (payload: byte[]) =
                  sendOnce true url (fun u ->
                      let m = new HttpRequestMessage(HttpMethod.Post, u)
                      m.Content <- new ByteArrayContent(payload)
                      m)

              sendBytes atCap
              sendBytes overCap

              Expect.equal received.Count 2 "both sizes should reach the server"
              Expect.equal received.[0].Length atCap.Length "at-cap body arrives in full"
              Expect.equal received.[1].Length overCap.Length "over-cap body arrives in full"
              Expect.equal received.[0].[0] 7uy "at-cap payload"
              Expect.equal received.[1].[0] 9uy "over-cap payload"

              use atMsg = new HttpRequestMessage(HttpMethod.Post, "http://example.invalid/")
              atMsg.Content <- new ByteArrayContent(atCap)
              captureRequest atMsg |> ignore

              match tryGetCapturedBody atMsg with
              | Some(Captured bytes) -> Expect.equal bytes.Length atCap.Length "at-cap captured"
              | other -> failtestf "expected Captured at the cap, got %A" other

              use overMsg = new HttpRequestMessage(HttpMethod.Post, "http://example.invalid/")
              overMsg.Content <- new ByteArrayContent(overCap)
              captureRequest overMsg |> ignore

              match tryGetCapturedBody overMsg with
              | Some(NotCaptured reason) ->
                  Expect.stringContains reason "body too large to show" "over-cap reason"
                  Expect.stringContains reason "MB" "human size in the reason"
              | other -> failtestf "expected NotCaptured over the cap, got %A" other
          }

          test "two requests correlate by requestMessage identity" {
              use m1 = new HttpRequestMessage(HttpMethod.Post, "http://example.invalid/one")
              m1.Content <- new StringContent("body-one", Encoding.UTF8, "text/plain")
              use m2 = new HttpRequestMessage(HttpMethod.Post, "http://example.invalid/two")
              m2.Content <- new StringContent("body-two", Encoding.UTF8, "text/plain")

              captureRequest m1 |> ignore
              captureRequest m2 |> ignore

              match tryGetCapturedBody m1, tryGetCapturedBody m2 with
              | Some(Captured a), Some(Captured b) ->
                  Expect.equal (Encoding.UTF8.GetString a) "body-one" "first message"
                  Expect.equal (Encoding.UTF8.GetString b) "body-two" "second message"
              | other -> failtestf "expected two Captured bodies, got %A" other
          }

          test "a lookup miss is None and does not throw" {
              use m = new HttpRequestMessage(HttpMethod.Get, "http://example.invalid/")
              let missed = tryGetCapturedBody m
              Expect.equal missed None "no store means a miss"
          }

          test "invocationConfigUpdate installs the capture transformer" {
              let src = invocationConfigUpdate 0
              Expect.stringContains src "httpMessageTransformers" "transformer field"
              Expect.stringContains src "__fsHttpStudioCaptureRequest" "capture binding"
          } ]
