// The core path: open the fixture, click a Run request lens, assert the first response, then
// click the second block's lens and assert the viewer replaces the first response. Spec 0006
// steps 1–7, as one check — the spec prices all seven steps against one per-check budget, and a
// split would also make the second half depend silently on the first half having run.
module CorePathTests

open Fable.Mocha

let private blockCount = 2
let private fixtureFileName = "core-path.fsx"
/// Path segment of the first block's URL, as it appears in the status line. The line shows the
/// block's own source text (`Protocol.extractMethodAndUrl`), so `GET $"{baseUrl}/json"` renders
/// as `{baseUrl}/json` — the `/json` path is the tell that distinguishes it from `/status`.
let private firstBlockUrlPath = "/json"
/// Path segment of the second block's URL. Together with the `/status` body keys from `Harness`,
/// it is the positive tell that the viewer shows the second response rather than a stale first one.
let private secondBlockUrlPath = "/status"
let private runInProgressLabel = "Running…"
/// Zero-based index of the second block's lens. Both lenses share `lensTitle`, so a title match
/// cannot reach the second block.
let private secondLensIndex = 1

let private tryClickFirstLens () =
    ExTester.tryClickCodeLensByTitle Checks.lensTitle

let private tryClickSecondLens () =
    ExTester.tryClickCodeLensByIndex secondLensIndex

let private tryRunningInViewer () =
    Checks.viewerSatisfies (fun dom -> dom.RunInProgressLabel.Contains runInProgressLabel)

let private tryFirstResponseRendered () =
    Checks.tryJsonProbeResponseRendered firstBlockUrlPath

/// The second response arrived *and* replaced the first. Absence of the first body's key is
/// asserted only here, inside the same `eventually` that proves the second response is present —
/// absence at a fixed time proves nothing.
let private trySecondResponseReplacedFirst () =
    Checks.viewerSatisfies (fun dom ->
        dom.UrlText.Contains secondBlockUrlPath
        && dom.JsonBodyText.Contains Harness.slowSeenKey
        && dom.JsonBodyText.Contains Harness.slowWaitingKey
        && not (dom.JsonBodyText.Contains Harness.jsonProbeKey))

/// Opens through `Checks.openFixtureAsSoleTab`, which empties the fixture column first. This is
/// the suite's first check, so the column still holds the `setup.fsx` tab that setup proved live.
/// With two tabs open, the lens read can resolve a hidden `.editor-instance` that carries no
/// CodeLens widgets, and report the product as having painted nothing.
///
/// Leaves the fixture and the viewer open, showing the second response. That is the state spec 3's
/// check expects to inherit and replace.
let private theCorePath =
    async {
        do! Checks.openFixtureAsSoleTab fixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount fixtureFileName)

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
    testList "the core path" [ testCaseAsync "renders one Run, then replaces it with the next" theCorePath ]
