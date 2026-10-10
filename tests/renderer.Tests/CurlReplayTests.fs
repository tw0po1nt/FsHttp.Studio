module Renderer.Tests.CurlReplayTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Sockets
open System.Runtime.InteropServices
open System.Text
open System.Text.Json
open Expecto

type private ReplayCase =
    { Name: string
      Curl: string
      Method: string
      Url: string
      Headers: (string * string) list
      Body: byte[] option }

/// What the echo server read from one connection.
type private Received =
    { Method: string
      Target: string
      Headers: (string * string) list
      Body: byte[] }

let private waitMs = 15_000

let private readCases () : ReplayCase list =
    let path = Path.Combine(GoldenFixture.folder, "curl", "curl-command.json")
    use document = JsonDocument.Parse(File.ReadAllBytes path)

    [ for c in document.RootElement.GetProperty("cases").EnumerateArray() do
          let curl = c.GetProperty "curl"

          if curl.ValueKind = JsonValueKind.String then
              let request = c.GetProperty "request"

              let body =
                  match request.GetProperty("bodyState").GetString() with
                  | "captured" ->
                      [| for b in request.GetProperty("bodyBytes").EnumerateArray() -> byte (b.GetInt32()) |]
                      |> Some
                  | _ -> None

              { Name = nonNull (c.GetProperty("name").GetString())
                Curl = nonNull (curl.GetString())
                Method = nonNull (request.GetProperty("method").GetString())
                Url = nonNull (request.GetProperty("url").GetString())
                Headers =
                  [ for pair in request.GetProperty("headers").EnumerateArray() ->
                        nonNull (pair[0].GetString()), nonNull (pair[1].GetString()) ]
                Body = body } ]

let private headerValue (name: string) (headers: (string * string) list) =
    headers
    |> List.tryFind (fun (n, _) -> String.Equals(n, name, StringComparison.OrdinalIgnoreCase))
    |> Option.map snd

let private indexOfHeadEnd (buffer: byte[]) (count: int) =
    let mutable found = -1
    let mutable i = 0

    while found < 0 && i + 3 < count do
        if
            buffer[i] = 13uy
            && buffer[i + 1] = 10uy
            && buffer[i + 2] = 13uy
            && buffer[i + 3] = 10uy
        then
            found <- i

        i <- i + 1

    found

/// Reads one HTTP/1.1 request with a `Content-Length` body, and answers `200` with no body.
let private serveOne (listener: TcpListener) : Received =
    use client = listener.AcceptTcpClient()
    client.ReceiveTimeout <- waitMs
    use stream = client.GetStream()
    let buffer = Array.zeroCreate<byte> (1 <<< 20)
    let mutable count = 0
    let mutable headEnd = -1

    while headEnd < 0 do
        let read = stream.Read(buffer, count, buffer.Length - count)

        if read = 0 then
            failwith "the connection closed before the end of the request head"

        count <- count + read
        headEnd <- indexOfHeadEnd buffer count

    let lines = Encoding.UTF8.GetString(buffer, 0, headEnd).Split("\r\n")
    let requestLine = lines[0].Split(' ')

    let headers =
        [ for line in Array.tail lines ->
              let colon = line.IndexOf ':'
              line.Substring(0, colon), line.Substring(colon + 1).Trim() ]

    let bodyLength =
        headerValue "Content-Length" headers |> Option.map int |> Option.defaultValue 0

    if headerValue "Expect" headers = Some "100-continue" then
        let continueLine = Encoding.ASCII.GetBytes "HTTP/1.1 100 Continue\r\n\r\n"
        stream.Write(continueLine, 0, continueLine.Length)

    let bodyStart = headEnd + 4

    while count - bodyStart < bodyLength do
        let read = stream.Read(buffer, count, buffer.Length - count)

        if read = 0 then
            failwith "the connection closed before the end of the body"

        count <- count + read

    let response =
        Encoding.ASCII.GetBytes "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"

    stream.Write(response, 0, response.Length)

    { Method = requestLine[0]
      Target = requestLine[1]
      Headers = headers
      Body = buffer[bodyStart .. bodyStart + bodyLength - 1] }

/// An empty curl configuration folder keeps the `.curlrc` of the user out of the replay.
let private runCurl (command: string) (port: int) : int * string =
    let configFolder = Directory.CreateTempSubdirectory "fshttp-curl-replay"

    try
        let script =
            command
            + sprintf " \\\n  --connect-to '::127.0.0.1:%d'" port
            + " \\\n  --noproxy '*' --silent --show-error --output /dev/null --max-time 10"

        let start =
            ProcessStartInfo("sh", RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false)

        start.ArgumentList.Add "-c"
        start.ArgumentList.Add script

        for name in [ "CURL_HOME"; "XDG_CONFIG_HOME"; "HOME" ] do
            start.Environment[name] <- configFolder.FullName

        use proc = nonNull (Process.Start start)
        let stderr = proc.StandardError.ReadToEndAsync()
        proc.StandardOutput.ReadToEndAsync() |> ignore

        if not (proc.WaitForExit waitMs) then
            proc.Kill true
            failtestf "sh and curl did not exit in %d ms" waitMs

        proc.ExitCode, stderr.Result
    finally
        configFolder.Delete true

let private replay (case: ReplayCase) =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()

    try
        let port = (listener.LocalEndpoint :?> IPEndPoint).Port
        let server = Threading.Tasks.Task.Run(fun () -> serveOne listener)
        let exitCode, stderr = runCurl case.Curl port
        Expect.equal exitCode 0 (sprintf "curl failed: %s" stderr)

        if not (server.Wait waitMs) then
            failtestf "the echo server read no request in %d ms" waitMs

        let received = server.Result
        Expect.equal received.Method case.Method "the method"

        let host = headerValue "Host" received.Headers |> Option.defaultValue ""
        Expect.equal ("http://" + host + received.Target) case.Url "the URL"

        let isNamed names (name: string, _) =
            names
            |> List.exists (fun n -> String.Equals(name, n, StringComparison.OrdinalIgnoreCase))

        Expect.equal
            (received.Headers |> List.filter (isNamed [ "Host"; "Content-Length" ] >> not))
            (case.Headers |> List.filter (isNamed [ "Content-Length" ] >> not))
            "the headers of the Run, in order, and no other header"

        match case.Body with
        | None -> Expect.isEmpty received.Body "no body"
        | Some bytes -> Expect.equal received.Body bytes "the body bytes"
    finally
        listener.Stop()

[<Tests>]
let tests =
    testList
        "Curl command replay"
        [ for case in readCases () ->
              test (sprintf "%s sends the request as sent" case.Name) {
                  if RuntimeInformation.IsOSPlatform OSPlatform.Windows then
                      skiptest "the replay runs on Linux and macOS only"

                  replay case
              } ]
