// Run outcomes render honestly: a real 404 travels the whole wire and renders as a successful
// Run with an HTTP error response, then a dead-port Run renders as plain runtime-error text with
// no response structure. Spec 0007, as one check — two Runs against the already-warm session and
// the already-open viewer the core-path check left behind.
module RunOutcomesTests

open Fable.Mocha

let private blockCount = 2
let private fixtureFileName = "run-outcomes.fsx"
/// Path segment the 404 block's URL ends in, as it appears in the status line. Distinguishes a
/// named `/notfound` render from a catch-all typo that also answers 404.
let private notFoundUrlPath = "/notfound"
/// The status-band class `Renderer.statusClass` writes for a 4xx status.
let private status4xxClass = "status-4xx"
/// Zero-based index of the dead-port block's lens. Both lenses share `Checks.lensTitle`.
let private deadPortLensIndex = 1

let private tryClickNotFoundLens () =
    ExTester.tryClickCodeLensByTitle Checks.lensTitle

let private tryClickDeadPortLens () =
    ExTester.tryClickCodeLensByIndex deadPortLensIndex

/// The status line shows the block's own source text, so the 404 block renders as
/// `{baseUrl}/notfound` — the URL *ends* in the path. A containment match would also accept
/// `/notfound/x`, which is a different route.
let private urlEndsInNotFound (urlText: string) =
    urlText.TrimEnd().EndsWith notFoundUrlPath

/// A successful Run with an HTTP error response: status 404, the named route's URL, the named
/// route's body rendered by content type, the 4xx status band, and no runtime-error text. The
/// absence of failure is asserted only inside this settled state — absence at a fixed time is not
/// meaningful.
let private tryNotFoundRenderedHonestly () =
    Checks.viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "404"
        && dom.StatusClass.Contains status4xxClass
        && urlEndsInNotFound dom.UrlText
        && dom.StatusLineText <> ""
        && dom.PlainBodyText.Contains Harness.notFoundBody
        && dom.HeadersText <> ""
        && not (dom.RootText.Contains Harness.runtimeErrorLabel))

/// A runtime error with no response: plain error text naming a runtime error, the 404 body's
/// distinctive content gone, and no status line or headers section. Absence of the status line is
/// asserted only inside this settled state.
let private tryDeadPortRenderedAsRuntimeError () =
    Checks.viewerSatisfies (fun dom ->
        dom.RootText.Contains Harness.runtimeErrorLabel
        && not (dom.RootText.Contains Harness.notFoundBody)
        && not (dom.PlainBodyText.Contains Harness.notFoundBody)
        && dom.StatusLineText = ""
        && dom.HeadersText = ""
        && dom.StatusCodeText = "")

/// Inherits the warm companion and the open viewer from the core-path check, and takes over the
/// fixture column for its own fixture — see `ExTester.tryOpenAsSoleTabInFixtureColumn` for what
/// that discards. Runs both outcomes and leaves the viewer showing the runtime error.
let private runOutcomesRenderHonestly =
    async {
        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "the run-outcomes fixture tab to open as the fixture column's only tab"
                (fun () -> ExTester.tryOpenAsSoleTabInFixtureColumn fixtureFileName)

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the 404 block's Run request lens"
                tryClickNotFoundLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 404, the /notfound URL and body, and no runtime-error text in the viewer"
                tryNotFoundRenderedHonestly

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the dead-port block's Run request lens"
                tryClickDeadPortLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "runtime-error text in the viewer, with the 404 render and status line gone"
                tryDeadPortRenderedAsRuntimeError
    }

let tests =
    testList
        "Run outcomes render honestly"
        [ testCaseAsync "a 404 renders as a response, a dead port as a runtime error" runOutcomesRenderHonestly ]
