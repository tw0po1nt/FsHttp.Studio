module Companion.Tests.TestServer

// A minimal in-process HTTP server for Seam A's "against a local test server" acceptance
// criteria. It depends on no external process, and on no other language runtime.

open System
open System.Net
open System.Threading

let private getFreePort () =
    let l = new Net.Sockets.TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let port = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    port

/// Serves `routes` on a free loopback port until disposal. Each route is a path and a handler,
/// and each handler writes and closes its own response.
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

    /// The base URL, with no trailing slash. One example is `http://127.0.0.1:54321`.
    member _.BaseUrl = prefix.TrimEnd('/')

    interface IDisposable with
        member _.Dispose() = listener.Close()

/// Writes `bytes` as the response body, with the given content type and status. It also writes
/// any extra headers.
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

/// Writes `text` as a `text/plain` response body, with the given status.
let textHandler (status: int) (text: string) (ctx: HttpListenerContext) =
    bytesHandler status "text/plain" [] (Text.Encoding.UTF8.GetBytes text) ctx

/// A handler that increments `counter` on every request, and then responds `200 OK`. The
/// listener loop processes one request at a time, so a plain mutation is safe here.
let countingHandler (counter: int ref) (ctx: HttpListenerContext) =
    counter.Value <- counter.Value + 1
    textHandler 200 "hit" ctx

/// A handler that never answers. It blocks on `release` until the test signals it, so the
/// request send at the other end never completes. It drives the "worker produces no frame and
/// hangs" path, because the send inside the worker stalls and the worker emits nothing. It
/// catches its own throws, such as a dropped connection after the caller is killed, or a
/// disposed event. A background listener thread can therefore never crash the test process.
let hangingHandler (release: Threading.ManualResetEventSlim) (ctx: HttpListenerContext) =
    try
        release.Wait()
    with _ ->
        ()

    try
        ctx.Response.OutputStream.Close()
    with _ ->
        ()

/// Streams `bytes` in chunks, and flushes the headers a moment *before* the body. That shape
/// makes FsHttp's default `ResponseHeadersRead` return a read-once body stream that is still
/// tied to the live socket. It reproduces the "stream was already consumed" bug. A body that
/// arrives at once, which every other handler here sends, never reaches that path. Keep-alive
/// stays on, so the connection is pooled and reused across Runs, as a real server does.
let streamingBytesHandler (contentType: string) (bytes: byte[]) (ctx: HttpListenerContext) =
    ctx.Response.StatusCode <- 200
    ctx.Response.ContentType <- contentType
    ctx.Response.SendChunked <- true
    ctx.Response.KeepAlive <- true
    // Force the headers onto the wire first, and then send the body in chunks.
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
