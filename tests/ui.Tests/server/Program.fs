module UiTestServer.Program

open System
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open UiTestServer.Server

let private fixturesDir () =
    let fromCwd =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "fixtures"))

    if Directory.Exists fromCwd then
        fromCwd
    else
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "fixtures"))

let private getString (client: HttpClient) (url: string) =
    task {
        use! resp = client.GetAsync(url)
        let! body = resp.Content.ReadAsStringAsync()
        return resp.StatusCode, body
    }

let private verify (baseUrl: string) =
    use client = new HttpClient()
    client.Timeout <- TimeSpan.FromSeconds 30.0

    let get path = getString client (baseUrl + path)

    task {
        let! code, body = get "/json"

        if int code <> 200 || body <> jsonProbeBody then
            failwithf "GET /json: expected 200 with probe body, got %A %A" code body

        let! code, body = get "/notfound"

        if int code <> 404 || body = jsonProbeBody then
            failwithf "GET /notfound: expected 404 with a body distinct from /json, got %A %A" code body

        let! code, _ = get "/nope"

        if int code <> 404 then
            failwithf "GET /nope: expected catch-all 404, got %A" code

        let! _, catchBody = get "/typo-route"

        if catchBody = jsonProbeBody then
            failwith "catch-all body must differ from /json"

        let! _, statusBefore = get "/status"

        if statusBefore <> """{"slowSeen":0,"slowWaiting":0}""" then
            failwithf "expected idle /status before /slow, got %s" statusBefore

        let slow1 = getString client (baseUrl + "/slow")

        let waitSlowWaiting atLeast =
            task {
                let mutable done' = false

                while not done' do
                    let! _, status = get "/status"

                    if status.Contains(sprintf "\"slowWaiting\":%d" atLeast) then
                        done' <- true
                    else
                        do! Task.Delay 10
            }

        do! waitSlowWaiting 1

        let! _, _ = get "/release"
        let! codeSlow1, _ = slow1

        if int codeSlow1 <> 200 then
            failwithf "first /slow: expected 200 after release, got %A" codeSlow1

        let slow2 = getString client (baseUrl + "/slow")
        do! waitSlowWaiting 1

        if slow2.IsCompleted then
            failwith "second /slow must block until its own /release (generation counter, not a latch)"

        let! _, _ = get "/release"
        let! codeSlow2, _ = slow2

        if int codeSlow2 <> 200 then
            failwithf "second /slow: expected 200 after second release, got %A" codeSlow2

        printfn "UiTestServer --verify: all checks passed."
    }
    |> fun t -> t.GetAwaiter().GetResult()

[<EntryPoint>]
let main argv =
    let verifyMode = argv |> Array.exists (fun a -> a = "--verify")
    let fixtures = fixturesDir ()
    Directory.CreateDirectory fixtures |> ignore

    let sidecarPath = Path.Combine(fixtures, "sidecar.json")

    if File.Exists sidecarPath then
        File.Delete sidecarPath

    use server = new UiTestHttpServer()
    server.WriteSidecar fixtures
    printfn "Listening on %s (deadUrl %s). Sidecar: %s" server.BaseUrl server.DeadUrl sidecarPath

    if verifyMode then
        verify server.BaseUrl
        0
    else
        let wait = new ManualResetEventSlim(false)

        Console.CancelKeyPress.Add(fun e ->
            e.Cancel <- true
            wait.Set())

        wait.Wait()
        0
