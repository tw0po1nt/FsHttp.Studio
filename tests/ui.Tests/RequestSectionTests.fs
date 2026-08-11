// The Request section, against the running editor. Spec 0012's Seam 4 claims the host's Fable and
// VSCode interop by product rather than by hand, and this is the half of it that the status-line
// URL cannot reach: the request *body* and *headers* travel a different path from method and URL —
// captured in the companion, carried through three `bodyState` fields on two wires, decoded in the
// webview, and rendered into a section the user has to open.
//
// It replaces the hand check the PR for issue #202 was going to carry ("run a POST with a JSON
// body and confirm the Request section shows it"). The renderer suite already proves the section
// renders from a canned envelope; what no pure suite can prove is that the bytes reaching it are
// the bytes that went on the wire.
//
// The fixture POSTs to `/echo`, which acknowledges without repeating the body, so the posted text
// appearing anywhere in the viewer can only have come through the request path.
module RequestSectionTests

open Fable.Mocha

let private fixtureFileName = "request-section.fsx"
let private blockCount = 1

/// The absolute URL the block sent, scheme and port included, on the same terms as the core path's:
/// the fixture computes it, so a status line built from source text could not contain this.
let private echoUrl () = Harness.baseUrl () + "/echo"

/// The response arrived and is the echo route's acknowledgement. Asserted before the section is
/// opened, so a later read cannot be racing a viewer that has not painted this Run yet.
let private tryEchoResponseRendered () =
    Checks.viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains(echoUrl ())
        && dom.JsonBodyText.Contains Harness.echoAckKey)

/// The section as the user first meets it: present, collapsed, and — because the fixture sent a
/// body — labelled with that body's size. `<details>` reports only its summary while collapsed,
/// so the absence of the posted text here is a second reading of the same fact as `RequestOpen`.
let private tryRequestCollapsedWithSize () =
    Checks.viewerSatisfies (fun dom ->
        dom.RequestSummaryText.StartsWith "Request ("
        && not dom.RequestOpen
        && not (dom.RequestText.Contains Harness.postedBodyValue))

/// The section, once opened, shows what was sent: the custom header as a row, and the posted body
/// rendered as JSON. Both are matched in parts, because the viewer pretty-prints the body and a
/// one-line match would fail on whitespace the renderer inserted.
///
/// The header and the body are asserted together in one `eventually`. They arrive in the same
/// repaint, so splitting them would buy nothing and would let the second read run against a state
/// the first had already left.
let private tryRequestShowsWhatWasSent () =
    Checks.viewerSatisfies (fun dom ->
        dom.RequestOpen
        && dom.RequestText.Contains Harness.postedHeaderName
        && dom.RequestText.Contains Harness.postedHeaderValue
        && dom.RequestText.Contains Harness.postedBodyKey
        && dom.RequestText.Contains Harness.postedBodyValue)

/// Opens through `Checks.openFixtureAsSoleTab` for the reason the core path documents: a second
/// tab in the column can put the lens read on a hidden editor that carries no widgets.
let private theRequestSection =
    async {
        do! Checks.openFixtureAsSoleTab fixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above the fixture's single block"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount fixtureFileName)

        do!
            Harness.eventually Harness.LensAppearanceDeadlineMs "a click on the Run request lens" (fun () ->
                ExTester.tryClickCodeLensByTitle Checks.lensTitle)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the absolute URL the block posted to, and the echo acknowledgement"
                tryEchoResponseRendered

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a collapsed Request section labelled with the sent body's size"
                tryRequestCollapsedWithSize

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a click on the Request section's summary"
                ExTester.tryExpandRequestSection

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the sent header row and the posted JSON body inside the expanded Request section"
                tryRequestShowsWhatWasSent
    }

let tests =
    testList "the request section" [ testCaseAsync "shows the headers and body a POST actually sent" theRequestSection ]
