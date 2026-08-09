// The core path: open the fixture, click a Run request lens, assert the first response, then
// click the second block's lens and assert the viewer replaces the first response. Spec 0006
// steps 1–7. The second check reuses the fixture and the open viewer the first check leaves.
module CorePathTests

open System.IO
open Fable.Core
open Fable.Mocha

/// The lens's rendered title, glyph included, exactly as `CodeLensProvider.buildCodeLens` writes
/// it. Asserted as rendered, and reused as the partial title the click matches on.
let private lensTitle = "▶ Run request"
let private blockCount = 2
let private fixtureFileName = "core-path.fsx"
/// Path segment of the first block's URL, as it appears in the status line. The line shows the
/// block's own source text (`Protocol.extractMethodAndUrl`), so `GET $"{baseUrl}/json"` renders
/// as `{baseUrl}/json` — the `/json` path is the tell that distinguishes it from `/status`.
let private firstBlockUrlPath = "/json"
/// Path segment of the second block's URL. Together with the `/status` body keys, it is the
/// positive tell that the viewer shows the second response rather than a stale first one.
let private secondBlockUrlPath = "/status"
/// Key names from `GET /status`. Assert the names only — the numeric values depend on whether
/// another check already hit `/slow` in this session.
let private statusSeenKey = "slowSeen"
let private statusWaitingKey = "slowWaiting"
let private runInProgressLabel = "Running…"
/// Zero-based index of the second block's lens. Both lenses share `lensTitle`, so a title match
/// cannot reach the second block.
let private secondLensIndex = 1

let private fixturePath () =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot locate its fixture"
    | Some sidecar -> Path.Combine(Path.GetDirectoryName sidecar, fixtureFileName)

/// Exactly one lens per block, each carrying the Run request title. An exact count is the claim
/// the spec makes — a provider that over-detects and stacks a third lens is as wrong as one that
/// finds only the first block.
let private tryRunRequestLensAboveEachBlock () =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()

        return
            titles.Length = blockCount
            && titles |> Array.forall (fun t -> t.Contains lensTitle)
    }

let private tryClickFirstLens () =
    ExTester.tryClickCodeLensByTitle lensTitle

let private tryClickSecondLens () =
    ExTester.tryClickCodeLensByIndex secondLensIndex

/// Reads the viewer's DOM and applies `holds` to it. A frame that cannot be entered yet is a
/// normal poll result, so it reads as "does not hold" rather than an exception.
let private viewerSatisfies (holds: ExTester.ResponseViewerDom -> bool) =
    async {
        match! ExTester.tryReadResponseViewer () with
        | None -> return false
        | Some dom -> return holds dom
    }

let private tryRunningInViewer () =
    viewerSatisfies (fun dom -> dom.RunInProgressLabel.Contains runInProgressLabel)

/// The probe body is matched by its key *and* its value, from the cross-process contract in
/// `Harness`. The key alone would pass against an empty or wrong value.
let private tryFirstResponseRendered () =
    viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains firstBlockUrlPath
        && dom.JsonBodyText.Contains Harness.jsonProbeKey
        && dom.JsonBodyText.Contains Harness.jsonProbeValue)

/// The second response arrived *and* replaced the first. Absence of the first body's key is
/// asserted only here, inside the same `eventually` that proves the second response is present —
/// absence at a fixed time proves nothing.
let private trySecondResponseReplacedFirst () =
    viewerSatisfies (fun dom ->
        dom.UrlText.Contains secondBlockUrlPath
        && dom.JsonBodyText.Contains statusSeenKey
        && dom.JsonBodyText.Contains statusWaitingKey
        && not (dom.JsonBodyText.Contains Harness.jsonProbeKey))

let private firstRunRendersCorrectly =
    async {
        let path = fixturePath ()
        let browser = ExTester.VSBrowser.instance

        do! ExTester.openResource browser path |> Async.AwaitPromise

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                tryRunRequestLensAboveEachBlock

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the first block's Run request lens"
                tryClickFirstLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to open beside the editor"
                ExTester.tryViewerBesideEditor

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

let private secondRunReplacesTheFirst =
    async {
        // Inherits the open fixture and viewer from `firstRunRendersCorrectly`. Leaves them open
        // with the second response showing, which is the state the next check expects.
        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the second block's Run request lens"
                tryClickSecondLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the second block's URL and status keys in the viewer, with the first body gone"
                trySecondResponseReplacedFirst
    }

let tests =
    testList
        "the core path"
        [ testCaseAsync "first Run renders correctly" firstRunRendersCorrectly
          testCaseAsync "second Run replaces the first" secondRunReplacesTheFirst ]
