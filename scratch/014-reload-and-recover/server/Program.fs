// PROTOTYPE throwaway — loopback server for ticket 014.
// Routes:
//   GET /json     200 JSON probe (the recover-after-reload assertion reads this)
//   GET /slow     blocks until /release is hit, then 200 JSON
//   GET /status   control endpoint: how many /slow requests have arrived and are waiting
//   GET /release  control endpoint: releases every waiter on /slow, 200
// Writes a sidecar JSON next to the fixtures, then serves until killed.
//
// Unlike 005's server this handles each context on the thread pool: a hanging /slow must not
// block /release or /json. That is the "control endpoint across processes" 003 asked for —
// the release travels over HTTP, not through an in-process ManualResetEventSlim.

module Program

open System
open System.IO
open System.Net
open System.Text
open System.Threading

let private getFreePort () =
    let l = new Sockets.TcpListener(IPAddress.Loopback, 0)
    l.Start()
    let port = (l.LocalEndpoint :?> IPEndPoint).Port
    l.Stop()
    port

[<EntryPoint>]
let main argv =
    let sidecarPath =
        match Array.tryHead argv with
        | Some p -> p
        | None ->
            eprintfn "usage: TestHttpServer <sidecar.json path>"
            exit 2

    let port = getFreePort ()
    let baseUrl = sprintf "http://127.0.0.1:%d" port
    let prefix = baseUrl + "/"

    // A port that nothing listens on: bind, read the number, close. 003's dead-port recipe.
    let deadPort = getFreePort ()

    let sidecarDir = Path.GetDirectoryName(sidecarPath)

    match sidecarDir with
    | null
    | "" -> ()
    | d -> Directory.CreateDirectory(d) |> ignore

    let sidecarBody =
        sprintf """{"baseUrl":"%s","deadUrl":"http://127.0.0.1:%d"}""" baseUrl deadPort

    File.WriteAllText(sidecarPath, sidecarBody)
    printfn "SIDECAR %s" sidecarPath
    printfn "BASEURL %s" baseUrl
    Console.Out.Flush()

    let listener = new HttpListener()
    listener.Prefixes.Add(prefix)
    listener.Start()

    let jsonBytes =
        Encoding.UTF8.GetBytes("""{"probe":"reload-and-recover-014","ok":true}""")

    let slowBytes =
        Encoding.UTF8.GetBytes("""{"probe":"reload-and-recover-014","slow":true}""")

    // Set by /release. Every /slow waits on it, so a suite that never releases still ends when
    // the process is killed.
    let released = new ManualResetEventSlim(false)

    // The arrival tell. A test that kills the companion before the request leaves is testing
    // a pending Run, not a Run in flight — /status is how the suite knows the difference.
    let mutable slowSeen = 0
    let mutable slowWaiting = 0

    let write (ctx: HttpListenerContext) (status: int) (body: byte[]) =
        try
            ctx.Response.StatusCode <- status
            ctx.Response.ContentType <- "application/json"
            ctx.Response.OutputStream.Write(body, 0, body.Length)
            ctx.Response.OutputStream.Close()
        with _ ->
            ()

    let handle (ctx: HttpListenerContext) =
        let path =
            match ctx.Request.Url with
            | null -> ""
            | u -> u.AbsolutePath

        match path with
        | "/json" ->
            printfn "HIT /json"
            Console.Out.Flush()
            write ctx 200 jsonBytes
        | "/slow" ->
            Interlocked.Increment(&slowSeen) |> ignore
            Interlocked.Increment(&slowWaiting) |> ignore
            printfn "HIT /slow (waiting for /release)"
            Console.Out.Flush()
            released.Wait()
            Interlocked.Decrement(&slowWaiting) |> ignore
            printfn "HIT /slow released"
            Console.Out.Flush()
            write ctx 200 slowBytes
        | "/status" ->
            let body =
                sprintf """{"slowSeen":%d,"slowWaiting":%d}""" (Volatile.Read(&slowSeen)) (Volatile.Read(&slowWaiting))

            write ctx 200 (Encoding.UTF8.GetBytes(body))
        | "/release" ->
            printfn "HIT /release"
            Console.Out.Flush()
            released.Set()
            write ctx 200 (Encoding.UTF8.GetBytes("""{"released":true}"""))
        | other ->
            printfn "HIT %s -> 404" other
            Console.Out.Flush()
            write ctx 404 (Encoding.UTF8.GetBytes("""{"error":"not found"}"""))

    let loop () =
        let mutable running = true

        while running do
            try
                let ctx = listener.GetContext()
                ThreadPool.QueueUserWorkItem(fun _ -> handle ctx) |> ignore
            with
            | :? ObjectDisposedException
            | :? HttpListenerException -> running <- false

    let thread = Thread(ThreadStart(loop), IsBackground = true)
    thread.Start()

    use quit = new ManualResetEventSlim(false)

    Console.CancelKeyPress.Add(fun args ->
        args.Cancel <- true
        quit.Set())

    quit.Wait()
    listener.Close()
    0
