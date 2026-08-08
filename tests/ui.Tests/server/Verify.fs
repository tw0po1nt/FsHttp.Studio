module UiTestServer.Verify

open System
open System.Net.Http
open System.Threading.Tasks
open UiTestServer.Server

/// Every poll here is bounded. This is a CI gate, so a misbehaving server must fail it with a
/// reason rather than hang it.
let private pollTimeout = TimeSpan.FromSeconds 30.0

let private get (client: HttpClient) (url: string) =
    task {
        use! resp = client.GetAsync(url)
        let! body = resp.Content.ReadAsStringAsync()

        let contentType =
            match resp.Content.Headers.ContentType with
            | null -> ""
            | ct ->
                match ct.MediaType with
                | null -> ""
                | mediaType -> mediaType

        return int resp.StatusCode, contentType, body
    }

let run (baseUrl: string) =
    use client = new HttpClient()
    client.Timeout <- pollTimeout

    let get path = get client (baseUrl + path)

    task {
        let! code, contentType, body = get "/json"

        if code <> 200 || contentType <> "application/json" || body <> jsonProbeBody then
            failwithf "GET /json: expected 200 application/json with the probe body, got %d %s %s" code contentType body

        let! code, _, namedNotFoundBody = get "/notfound"

        if code <> 404 || namedNotFoundBody = jsonProbeBody then
            failwithf "GET /notfound: expected 404 with a body distinct from /json, got %d %s" code namedNotFoundBody

        let! code, _, catchAllBody = get "/typo-route"

        if code <> 404 then
            failwithf "GET /typo-route: expected catch-all 404, got %d" code

        if catchAllBody = jsonProbeBody then
            failwith "catch-all body must differ from /json"

        if catchAllBody = namedNotFoundBody then
            failwith "/notfound must be a named route, distinguishable from the catch-all"

        let readStatus () =
            task {
                let! _, _, body = get "/status"

                match tryParseStatus body with
                | Some counts -> return counts
                | None -> return failwith (sprintf "GET /status: unparseable body %s" body)
            }

        let! idle = readStatus ()

        if idle <> (0, 0) then
            failwithf "expected an idle /status before /slow, got %A" idle

        // slowSeen is the arrival count and only ever climbs. Polling slowWaiting instead would
        // race a departing /slow, which still reads as waiting while its response is written.
        let waitSlowSeen target =
            task {
                let deadline = DateTime.UtcNow + pollTimeout
                let mutable seen = 0

                while seen < target do
                    let! arrived, _ = readStatus ()
                    seen <- arrived

                    if seen < target then
                        if DateTime.UtcNow > deadline then
                            failwithf "timed out waiting for slowSeen to reach %d, last saw %d" target seen

                        do! Task.Delay 10
            }

        // After arrival, a short settle window lets a wrongly-unblocked /slow finish, so the
        // IsCompleted assertions below fail the latch rather than racing the client.
        let settle () = Task.Delay 50

        let slow1 = get "/slow"
        do! waitSlowSeen 1
        do! settle ()

        if slow1.IsCompleted then
            failwith "first /slow must block until its /release"

        let! _ = get "/release"
        let! codeSlow1, _, _ = slow1

        if codeSlow1 <> 200 then
            failwithf "first /slow: expected 200 after release, got %d" codeSlow1

        let slow2 = get "/slow"
        do! waitSlowSeen 2
        do! settle ()

        if slow2.IsCompleted then
            failwith "second /slow must block until its own /release (generation counter, not a latch)"

        let! _ = get "/release"
        let! codeSlow2, _, _ = slow2

        if codeSlow2 <> 200 then
            failwithf "second /slow: expected 200 after second release, got %d" codeSlow2

        printfn "UiTestServer --verify: all checks passed."
    }
    |> fun t -> t.GetAwaiter().GetResult()
