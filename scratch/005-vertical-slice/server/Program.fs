// PROTOTYPE throwaway — minimal loopback server for ticket 005.
// Writes a sidecar JSON next to the fixtures, then serves GET /json until killed.

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

    let sidecarDir = Path.GetDirectoryName(sidecarPath)

    match sidecarDir with
    | null
    | "" -> ()
    | d -> Directory.CreateDirectory(d) |> ignore

    let sidecarBody = sprintf """{"baseUrl":"%s"}""" baseUrl
    File.WriteAllText(sidecarPath, sidecarBody)
    printfn "SIDECAR %s" sidecarPath
    printfn "BASEURL %s" baseUrl
    System.Console.Out.Flush()

    let listener = new HttpListener()
    listener.Prefixes.Add(prefix)
    listener.Start()

    let jsonBytes =
        Encoding.UTF8.GetBytes("""{"probe":"vertical-slice-005","ok":true}""")

    let loop () =
        let mutable running = true

        while running do
            try
                let ctx = listener.GetContext()
                let path =
                    match ctx.Request.Url with
                    | null -> ""
                    | u -> u.AbsolutePath

                if path = "/json" then
                    printfn "HIT /json"
                    Console.Out.Flush()
                    ctx.Response.StatusCode <- 200
                    ctx.Response.ContentType <- "application/json"
                    ctx.Response.OutputStream.Write(jsonBytes, 0, jsonBytes.Length)
                else
                    printfn "HIT %s -> 404" path
                    Console.Out.Flush()
                    ctx.Response.StatusCode <- 404

                ctx.Response.OutputStream.Close()
            with
            | :? ObjectDisposedException
            | :? HttpListenerException -> running <- false

    let thread = Thread(ThreadStart(loop), IsBackground = true)
    thread.Start()

    use exit = new ManualResetEventSlim(false)

    Console.CancelKeyPress.Add(fun args ->
        args.Cancel <- true
        exit.Set())

    exit.Wait()
    listener.Close()
    0
