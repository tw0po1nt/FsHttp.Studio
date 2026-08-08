// The core path's first Run: open the fixture, click a Run request lens, and assert the response
// viewer opens beside the editor and renders the first block's response. Spec 0006 steps 1–5.
// The second Run (steps 6–7) lands in a follow-on check that reuses this fixture and session.
module CorePathTests

open System.IO
open Fable.Core
open Fable.Mocha

let private lensTitle = "Run request"
let private fixtureFileName = "core-path.fsx"
/// Path segment of the first block's URL, as it appears in the status line. The line shows the
/// block's own source text (`Protocol.extractMethodAndUrl`), so `GET $"{baseUrl}/json"` renders
/// as `{baseUrl}/json` — the `/json` path is the tell that distinguishes it from `/status`.
let private firstBlockUrlPath = "/json"
/// Distinctive JSON key in `GET /json`'s probe body. Shared with the second-Run check, which
/// asserts this key is gone after `/status` replaces the render.
let private jsonProbeKey = "\"probe\""
let private runInProgressNeedle = "Running"

let private fixturePath () =
    let sidecar = Proc.env "UI_TEST_SIDECAR" ""

    if sidecar = "" then
        Assert.fail "UI_TEST_SIDECAR is not set"
    else
        Path.Combine(Path.GetDirectoryName sidecar, fixtureFileName)

let private tryTwoRunRequestLenses () =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()

        return
            titles.Length >= 2
            && titles |> Array.take 2 |> Array.forall (fun t -> t.Contains lensTitle)
    }

let private tryClickFirstLens () =
    ExTester.tryClickCodeLensByTitle lensTitle

let private tryViewerBesideEditor () = ExTester.tryViewerBesideEditor ()

let private tryRunningInViewer () =
    async {
        match! ExTester.tryReadResponseViewer () with
        | Some dom when dom.RunInProgressLabel.Contains runInProgressNeedle -> return true
        | _ -> return false
    }

let private tryFirstResponseRendered () =
    async {
        match! ExTester.tryReadResponseViewer () with
        | None -> return false
        | Some dom ->
            return
                dom.StatusCodeText.Contains "200"
                && dom.UrlText.Contains firstBlockUrlPath
                && dom.JsonBodyText.Contains jsonProbeKey
    }

let private firstRunRendersCorrectly =
    async {
        let path = fixturePath ()
        let browser = ExTester.VSBrowser.instance

        do! ExTester.openResource browser path |> Async.AwaitPromise

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                tryTwoRunRequestLenses

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the first block's Run request lens"
                tryClickFirstLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to open beside the editor"
                tryViewerBesideEditor

        // Soft: `Running…` is transient. Assert it only on this cold first Run, where a
        // `#r "nuget:"` restore keeps the in-flight window open for seconds. If it flakes in
        // practice, drop the assertion — do not add a sleep or a special retry to force it.
        do! Harness.eventually Harness.ViewerUpdateDeadlineMs "Running… in the response viewer" tryRunningInViewer

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the first block's URL, and the probe body in the response viewer"
                tryFirstResponseRendered
    }

let tests =
    testList "the core path" [ testCaseAsync "first Run renders correctly" firstRunRendersCorrectly ]
