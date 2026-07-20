module Companion.Tests.TestServer

// A minimal in-process HTTP server for Seam A's "against a local test server" acceptance
// criteria — avoids depending on any external process or language runtime.

open System
open System.Net
open System.Threading

let private getFreePort () =
    let l = new Net.Sockets.TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let port = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    port

/// Serves `routes` (path -> handler, each responsible for writing and closing the response)
/// on a free loopback port until disposed.
type TestServer(routes: Map<string, HttpListenerContext -> unit>) =
    let port = getFreePort ()
    let prefix = sprintf "http://127.0.0.1:%d/" port
    let listener = new HttpListener()
    do listener.Prefixes.Add(prefix)
    do listener.Start()

    let loop () =
        let mutable running = true

        while running do
            try
                let ctx = listener.GetContext()

                let path =
                    match ctx.Request.Url with
                    | null -> ""
                    | u -> u.AbsolutePath

                match routes.TryFind path with
                | Some handler -> handler ctx
                | None ->
                    ctx.Response.StatusCode <- 404
                    ctx.Response.OutputStream.Close()
            with
            | :? ObjectDisposedException
            | :? HttpListenerException -> running <- false

    let thread = Thread(loop, IsBackground = true)
    do thread.Start()

    /// Base URL with no trailing slash, e.g. `http://127.0.0.1:54321`.
    member _.BaseUrl = prefix.TrimEnd('/')

    interface IDisposable with
        member _.Dispose() = listener.Close()

/// Writes `bytes` as the response body with the given content type and status, plus any
/// extra headers.
let bytesHandler
    (status: int)
    (contentType: string)
    (extraHeaders: (string * string) list)
    (bytes: byte[])
    (ctx: HttpListenerContext)
    =
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- contentType
    extraHeaders |> List.iter (fun (k, v) -> ctx.Response.Headers.Add(k, v))
    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
    ctx.Response.OutputStream.Close()

/// Writes `text` as a `text/plain` response body with the given status.
let textHandler (status: int) (text: string) (ctx: HttpListenerContext) =
    bytesHandler status "text/plain" [] (Text.Encoding.UTF8.GetBytes text) ctx

/// A handler that increments `counter` on every hit, then responds `200 OK`. The listener
/// loop processes one request at a time, so a plain mutation is safe here.
let countingHandler (counter: int ref) (ctx: HttpListenerContext) =
    counter.Value <- counter.Value + 1
    textHandler 200 "hit" ctx

/// A handler that never answers: it blocks on `release` until the test signals it, so the request
/// send on the other end never completes. Drives the "worker produces no frame and hangs" path —
/// the send inside the worker stalls, so the worker emits nothing at all. Self-contained against
/// throws (a dropped connection once the caller is killed, a disposed event) so a background
/// listener thread can never crash the test process.
let hangingHandler (release: Threading.ManualResetEventSlim) (ctx: HttpListenerContext) =
    try
        release.Wait()
    with _ ->
        ()

    try
        ctx.Response.OutputStream.Close()
    with _ ->
        ()

/// Streams `bytes` chunked, flushing the headers a beat *before* the body — the shape that
/// makes FsHttp's default `ResponseHeadersRead` hand back a read-once body stream still tied to
/// the live socket. This is what reproduces the "stream was already consumed" bug: a body
/// that arrives instantly (every other handler here) never exercises that path. Keep-alive is
/// left on so the connection is pooled and reused across Runs, mirroring the real server.
let streamingBytesHandler (contentType: string) (bytes: byte[]) (ctx: HttpListenerContext) =
    ctx.Response.StatusCode <- 200
    ctx.Response.ContentType <- contentType
    ctx.Response.SendChunked <- true
    ctx.Response.KeepAlive <- true
    // Force the headers onto the wire first, then dribble the body out in chunks.
    ctx.Response.OutputStream.Flush()
    Thread.Sleep(50)

    let chunk = 2048
    let mutable off = 0

    while off < bytes.Length do
        let n = min chunk (bytes.Length - off)
        ctx.Response.OutputStream.Write(bytes, off, n)
        ctx.Response.OutputStream.Flush()
        Thread.Sleep(10)
        off <- off + n

    ctx.Response.OutputStream.Close()
