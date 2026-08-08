module UiTestServer.Server

open System
open System.Net
open System.Text
open System.Threading

/// Cross-process contract for `GET /json`. Match exactly in fixtures and harness healthchecks.
let jsonProbeBody = """{"probe":"ui-test-server"}"""

let private notFoundRouteBody = "ui-test-server:notfound"
let private catchAllBody = "ui-test-server:unknown"

let private utf8 = Encoding.UTF8

type UiTestHttpServer() =
    let mutable releaseGeneration = 0
    let mutable slowSeen = 0
    let mutable slowWaiting = 0

    let getFreePort () =
        let listener = new Net.Sockets.TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        let port = (listener.LocalEndpoint :?> IPEndPoint).Port
        listener.Stop()
        port

    let deadPort = getFreePort ()

    let port = getFreePort ()
    let prefix = sprintf "http://127.0.0.1:%d/" port
    let baseUrl = prefix.TrimEnd('/')
    let deadUrl = sprintf "http://127.0.0.1:%d" deadPort

    let listener = new HttpListener()
    do listener.Prefixes.Add(prefix)
    do listener.Start()

    let writeText (ctx: HttpListenerContext) status contentType (text: string) =
        let bytes = utf8.GetBytes text
        ctx.Response.StatusCode <- status
        ctx.Response.ContentType <- contentType
        ctx.Response.ContentLength64 <- int64 bytes.Length
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length)
        ctx.Response.OutputStream.Close()

    let handleSlow (ctx: HttpListenerContext) =
        Interlocked.Increment &slowSeen |> ignore
        Interlocked.Increment &slowWaiting |> ignore

        let generationAtArrival = Volatile.Read &releaseGeneration

        try
            while Volatile.Read &releaseGeneration <= generationAtArrival do
                Thread.Sleep(5)

            writeText ctx 200 "text/plain" "slow-ok"
        finally
            Interlocked.Decrement &slowWaiting |> ignore

    let handleRelease (ctx: HttpListenerContext) =
        Interlocked.Increment &releaseGeneration |> ignore
        writeText ctx 200 "text/plain" "release-ok"

    let handleStatus (ctx: HttpListenerContext) =
        let body =
            sprintf """{"slowSeen":%d,"slowWaiting":%d}""" (Volatile.Read &slowSeen) (Volatile.Read &slowWaiting)

        writeText ctx 200 "application/json" body

    let dispatch (ctx: HttpListenerContext) =
        try
            let path =
                match ctx.Request.Url with
                | null -> ""
                | url -> url.AbsolutePath

            match ctx.Request.HttpMethod, path with
            | "GET", "/json" -> writeText ctx 200 "application/json" jsonProbeBody
            | "GET", "/notfound" -> writeText ctx 404 "text/plain" notFoundRouteBody
            | "GET", "/slow" -> handleSlow ctx
            | "GET", "/release" -> handleRelease ctx
            | "GET", "/status" -> handleStatus ctx
            | _ -> writeText ctx 404 "text/plain" catchAllBody
        with _ ->
            try
                ctx.Response.Abort()
            with _ ->
                ()

    let loop () =
        let mutable running = true

        while running do
            try
                let ctx = listener.GetContext()
                ThreadPool.QueueUserWorkItem(fun _ -> dispatch ctx) |> ignore
            with
            | :? ObjectDisposedException
            | :? HttpListenerException -> running <- false

    let thread = Thread(loop, IsBackground = true)
    do thread.Start()

    member _.BaseUrl = baseUrl
    member _.DeadUrl = deadUrl

    member _.WriteSidecar(fixturesDir: string) =
        let path = System.IO.Path.Combine(fixturesDir, "sidecar.json")

        let json = sprintf """{"baseUrl":"%s","deadUrl":"%s"}""" baseUrl deadUrl

        System.IO.File.WriteAllText(path, json, utf8)

    interface IDisposable with
        member _.Dispose() = listener.Close()
