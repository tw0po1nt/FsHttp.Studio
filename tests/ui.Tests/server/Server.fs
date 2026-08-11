module UiTestServer.Server

open System
open System.IO
open System.Net
open System.Text
open System.Text.Json
open System.Threading

/// Cross-process contract for `GET /json`. Match exactly in fixtures and harness healthchecks.
let jsonProbeBody = """{"probe":"ui-test-server"}"""

/// Cross-process contract for `GET /status`: one shape and its inverse, shared by the server and
/// by every caller that reads the arrival tell.
let statusBody slowSeen slowWaiting =
    sprintf """{"slowSeen":%d,"slowWaiting":%d}""" slowSeen slowWaiting

let tryParseStatus (body: string) =
    try
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Some(root.GetProperty("slowSeen").GetInt32(), root.GetProperty("slowWaiting").GetInt32())
    with _ ->
        None

/// Cross-process contract for the sidecar file: the name, and the one shape written into it.
let sidecarFileName = "sidecar.json"

let sidecarJson (baseUrl: string) (deadUrl: string) =
    JsonSerializer.Serialize
        {| baseUrl = baseUrl
           deadUrl = deadUrl |}

/// Ceiling on a blocked `/slow`. A caller that never sends `/release` gets a failure instead of
/// parking a thread-pool thread for the life of the process. No passing test comes near it.
let private slowCeiling = TimeSpan.FromMinutes 2.0

/// Cross-process contract for `GET /notfound`. Match exactly in fixtures and harness checks.
let notFoundBody = "ui-test-server:notfound"

/// Cross-process contract for `POST /echo`. A fixed acknowledgement that deliberately carries
/// none of the posted body: the request-section check asserts the posted body inside the viewer's
/// Request section, and a response that echoed it back would let that assertion pass against the
/// response body region instead (docs/spec/0012-request-as-sent.md, Seam 4).
let echoAckBody = """{"echoed":"ui-test-server"}"""

let private catchAllBody = "ui-test-server:unknown"

let private utf8 = Encoding.UTF8

/// The sidecar is read by a JS harness, and `JSON.parse` throws on a byte-order mark.
let private utf8NoBom = UTF8Encoding(false)

type UiTestHttpServer() =
    let mutable releaseGeneration = 0
    let mutable slowSeen = 0
    let mutable slowWaiting = 0

    // Both ephemeral ports are held bound at the same time, then released, so the OS cannot hand
    // the dead port back as the live one. A sidecar whose deadUrl points at the live server would
    // fail the harness's dead-port probe with a misleading reason.
    let allocatePorts () =
        use live = new Sockets.TcpListener(IPAddress.Loopback, 0)
        use dead = new Sockets.TcpListener(IPAddress.Loopback, 0)
        live.Start()
        dead.Start()
        let livePort = (live.LocalEndpoint :?> IPEndPoint).Port
        let deadPort = (dead.LocalEndpoint :?> IPEndPoint).Port
        live.Stop()
        dead.Stop()
        livePort, deadPort

    let port, deadPort = allocatePorts ()

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
        let deadline = DateTime.UtcNow + slowCeiling

        try
            let mutable released = false

            while not released && DateTime.UtcNow < deadline do
                if Volatile.Read &releaseGeneration > generationAtArrival then
                    released <- true
                else
                    Thread.Sleep(5)

            if released then
                writeText ctx 200 "text/plain" "slow-ok"
            else
                writeText ctx 504 "text/plain" "slow-timeout"
        finally
            Interlocked.Decrement &slowWaiting |> ignore

    let handleRelease (ctx: HttpListenerContext) =
        Interlocked.Increment &releaseGeneration |> ignore
        writeText ctx 200 "text/plain" "release-ok"

    let handleStatus (ctx: HttpListenerContext) =
        let body = statusBody (Volatile.Read &slowSeen) (Volatile.Read &slowWaiting)

        writeText ctx 200 "application/json" body

    let dispatch (ctx: HttpListenerContext) =
        try
            let path =
                match ctx.Request.Url with
                | null -> ""
                | url -> url.AbsolutePath

            match ctx.Request.HttpMethod, path with
            | "GET", "/json" -> writeText ctx 200 "application/json" jsonProbeBody
            | "GET", "/notfound" -> writeText ctx 404 "text/plain" notFoundBody
            | "GET", "/slow" -> handleSlow ctx
            | "GET", "/release" -> handleRelease ctx
            | "GET", "/status" -> handleStatus ctx
            // The posted body is read to completion and dropped. Draining it keeps the connection
            // reusable; not echoing it is what makes the request-section check a real claim.
            | "POST", "/echo" ->
                use reader = new StreamReader(ctx.Request.InputStream, utf8)
                reader.ReadToEnd() |> ignore
                writeText ctx 200 "application/json" echoAckBody
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
        let path = Path.Combine(fixturesDir, sidecarFileName)
        File.WriteAllText(path, sidecarJson baseUrl deadUrl, utf8NoBom)

    interface IDisposable with
        member _.Dispose() = listener.Close()
