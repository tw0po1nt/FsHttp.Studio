// The core path: open the fixture, click a Run request lens, assert the first response, then
// click the second block's lens and assert the viewer replaces the first response. Spec 0006
// steps 1–7, as one check — the spec prices all seven steps against one per-check budget, and a
// split would also make the second half depend silently on the first half having run.
//
// Both fixture blocks compute their URL, and both URL assertions here are for the absolute URL
// that went on the wire. That is what carries spec 0012's Seam 4: `RunCommand.runOne` has no
// suite of its own, so this is where the host reading method and URL off the run result is
// claimed against the running product.
module CorePathTests

open Fable.Mocha

let private blockCount = 2
let private fixtureFileName = "core-path.fsx"

/// The first block's URL exactly as it went on the wire, scheme and port included. Both fixture
/// blocks compute their URL (`GET $"{baseUrl}/json"`), so this is the claim that the status line
/// reads the URL off the run result rather than re-deriving it from the block's own source text
/// (docs/spec/0012-request-as-sent.md, Decision 11): source text renders `{baseUrl}/json`, which
/// carries neither host nor port and cannot contain this. Built at call time, not at module load —
/// the address comes from the sidecar, which only setup has proven readable.
///
/// The `/json` path is also what distinguishes this response from `/status`.
let private firstBlockUrl () = Harness.baseUrl () + "/json"

/// The second block's URL, on the same terms. Together with the `/status` body keys from
/// `Harness`, it is the positive tell that the viewer shows the second response rather than a
/// stale first one.
let private secondBlockUrl () = Harness.baseUrl () + "/status"
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
    Checks.tryJsonProbeResponseRendered (firstBlockUrl ())

/// The second response arrived *and* replaced the first. Absence of the first body's key is
/// asserted only here, inside the same `eventually` that proves the second response is present —
/// absence at a fixed time proves nothing.
let private trySecondResponseReplacedFirst () =
    Checks.viewerSatisfies (fun dom ->
        dom.UrlText.Contains(secondBlockUrl ())
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
                "status 200, the absolute URL the first block sent, and the probe body in the response viewer"
                tryFirstResponseRendered

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the second block's Run request lens"
                tryClickSecondLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the absolute URL the second block sent and its status keys in the viewer, with the first body gone"
                trySecondResponseReplacedFirst
    }

let tests =
    testList "the core path" [ testCaseAsync "renders one Run, then replaces it with the next" theCorePath ]
