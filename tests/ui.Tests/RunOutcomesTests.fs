// Run outcomes render honestly: a real 404 travels the whole wire and renders as a successful
// Run with an HTTP error response, then a dead-port Run renders as plain runtime-error text with
// no response structure. Spec 0007, as one check — two Runs against the already-warm session and
// the already-open viewer the core-path check left behind.
module RunOutcomesTests

open System.IO
open Fable.Core
open Fable.Mocha

let private lensTitle = "▶ Run request"
let private blockCount = 2
let private fixtureFileName = "run-outcomes.fsx"
/// Path segment of the 404 block's URL, as it appears in the status line. Distinguishes a named
/// `/notfound` render from a catch-all typo that also answers 404.
let private notFoundUrlPath = "/notfound"
/// The status-band class `Renderer.statusClass` writes for a 4xx status.
let private status4xxClass = "status-4xx"
/// Zero-based index of the dead-port block's lens. Both lenses share `lensTitle`.
let private deadPortLensIndex = 1

let private fixturePath () =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot locate its fixture"
    | Some sidecar -> Path.Combine(Path.GetDirectoryName sidecar, fixtureFileName)

let private describeTitles (titles: string[]) =
    if Array.isEmpty titles then
        "0 CodeLenses"
    else
        let quoted = titles |> Array.map (fun t -> sprintf "\"%s\"" t) |> String.concat ", "
        sprintf "%i CodeLenses: %s" titles.Length quoted

let private tryRunRequestLensAboveEachBlock () =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()

        if
            titles.Length = blockCount
            && titles |> Array.forall (fun t -> t.Contains lensTitle)
        then
            return Harness.Holds
        else
            return Harness.Observed(describeTitles titles)
    }

let private tryClickFirstLens () =
    ExTester.tryClickCodeLensByTitle lensTitle

let private tryClickDeadPortLens () =
    ExTester.tryClickCodeLensByIndex deadPortLensIndex

let private viewerSatisfies (holds: ExTester.ResponseViewerDom -> bool) =
    async {
        match! ExTester.tryReadResponseViewer () with
        | None -> return false
        | Some dom -> return holds dom
    }

/// A successful Run with an HTTP error response: status 404, the named route's URL, the named
/// route's body rendered by content type, the 4xx status band, and no runtime-error text. The
/// absence of failure is asserted only inside this settled state — absence at a fixed time is not
/// meaningful.
let private tryNotFoundRenderedHonestly () =
    viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "404"
        && dom.StatusClass.Contains status4xxClass
        && dom.UrlText.Contains notFoundUrlPath
        && dom.StatusLineText <> ""
        && dom.PlainBodyText.Contains Harness.notFoundBody
        && dom.HeadersText <> ""
        && not (dom.RootText.Contains Harness.runtimeErrorLabel))

/// A runtime error with no response: plain error text naming a runtime error, the 404 body's
/// distinctive content gone, and no status line or headers section. Absence of the status line is
/// asserted only inside this settled state.
let private tryDeadPortRenderedAsRuntimeError () =
    viewerSatisfies (fun dom ->
        dom.RootText.Contains Harness.runtimeErrorLabel
        && not (dom.RootText.Contains Harness.notFoundBody)
        && not (dom.PlainBodyText.Contains Harness.notFoundBody)
        && dom.StatusLineText = ""
        && dom.HeadersText = ""
        && dom.StatusCodeText = "")

/// Inherits the warm companion and open viewer from the core-path check. Opens this check's own
/// fixture, runs both outcomes, and leaves the viewer showing the runtime error.
let private runOutcomesRenderHonestly =
    async {
        let path = fixturePath ()

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "the run-outcomes fixture tab to open and take focus"
                (fun () -> ExTester.tryOpenAndFocusResource path fixtureFileName)

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                tryRunRequestLensAboveEachBlock

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the 404 block's Run request lens"
                tryClickFirstLens

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
