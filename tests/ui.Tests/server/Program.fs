module UiTestServer.Program

open System
open System.IO
open System.Threading
open UiTestServer.Server

let private fixturesDir () =
    let fromCwd =
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "fixtures"))

    if Directory.Exists fromCwd then
        fromCwd
    else
        Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "fixtures"))

[<EntryPoint>]
let main argv =
    let verifyMode = argv |> Array.exists (fun a -> a = "--verify")
    let fixtures = fixturesDir ()
    Directory.CreateDirectory fixtures |> ignore

    use server = new UiTestHttpServer()
    server.WriteSidecar fixtures

    printfn
        "Listening on %s (deadUrl %s). Sidecar: %s"
        server.BaseUrl
        server.DeadUrl
        (Path.Combine(fixtures, sidecarFileName))

    if verifyMode then
        Verify.run server.BaseUrl
        0
    else
        let wait = new ManualResetEventSlim(false)

        Console.CancelKeyPress.Add(fun e ->
            e.Cancel <- true
            wait.Set())

        wait.Wait()
        0
