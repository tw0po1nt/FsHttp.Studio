// The copy buttons, against the running editor. Spec 0013's UI-suite section replaces the manual
// smoke: a click puts the section's payload on the clipboard, the button's own label reports the
// result, and a click never toggles the section. The Seam-B suite already proves `copyText` and
// the DOM shape; this module claims the delegated listener, the clipboard write, and the flash.
module CopyButtonsTests

open Fable.Mocha

let private fixtureFileName = "request-section.fsx"
let private blockCount = 1

let private echoUrl () = Harness.baseUrl () + "/echo"

/// The response arrived and is the echo route's acknowledgement. Asserted before any copy click,
/// so a later clipboard read cannot race a viewer that has not painted this Run yet.
let private tryEchoResponseRendered () =
    Checks.viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains(echoUrl ())
        && dom.JsonBodyText.Contains Harness.echoAckKey)

/// Both header sections collapsed, each copy button laid out, and the three shell margins as
/// Decision 12 requires: 12px, 12px, then 0 on the last shell (the body had no bottom margin).
let private tryCollapsedButtonsAndSpacing () =
    Checks.viewerSatisfies (fun dom ->
        not dom.RequestOpen
        && not dom.HeadersOpen
        && dom.CopyButtons.Length = 3
        && (dom.CopyButtons
            |> Array.forall (fun b ->
                (b.Key = "request" || b.Key = "response-headers" || b.Key = "response-body")
                && b.Displayed
                && b.Label = "Copy"))
        && dom.ShellMarginsPx.Length = 3
        && dom.ShellMarginsPx[0] = 12.
        && dom.ShellMarginsPx[1] = 12.
        && dom.ShellMarginsPx[2] = 0.)

/// A successful copy click: label `Copied`, the witness holds `holds` against the written text,
/// and neither collapsible section changed its open state from the values taken before the click.
let private tryCopySucceeded (key: string) (requestWasOpen: bool) (headersWereOpen: bool) (holds: string -> bool) =
    async {
        match! ExTester.tryClickCopyButton key with
        | None -> return false
        | Some reading ->
            return
                reading.Label = "Copied"
                && reading.RequestOpen = requestWasOpen
                && reading.HeadersOpen = headersWereOpen
                && (match reading.CopiedText with
                    | Some text -> holds text
                    | None -> false)
    }

let private requestPayloadHolds (text: string) =
    text.StartsWith("POST " + echoUrl ())
    && text.Contains(Harness.postedHeaderName + ": " + Harness.postedHeaderValue)
    && text.Contains("\n\n" + Harness.postedBody)

let private headersPayloadHolds (text: string) =
    text.StartsWith "200 " && text.Contains "Content-Type:"

let private bodyPayloadHolds (text: string) = text = Harness.echoAckBody

/// Opens through `Checks.openFixtureAsSoleTab` for the reason the core path documents: a second
/// tab in the column can put the lens read on a hidden editor that carries no widgets.
let private theCopyButtons =
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
                "three laid-out Copy buttons beside collapsed sections, with shell margins 12/12/0"
                tryCollapsedButtonsAndSpacing

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a click on the request copy button puts the sent request on the clipboard"
                (fun () -> tryCopySucceeded "request" false false requestPayloadHolds)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a click on the response-headers copy button puts the status and headers on the clipboard"
                (fun () -> tryCopySucceeded "response-headers" false false headersPayloadHolds)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a click on the response-body copy button puts the raw JSON body on the clipboard"
                (fun () -> tryCopySucceeded "response-body" false false bodyPayloadHolds)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response-body copy button label reverts to Copy after the flash"
                (fun () -> ExTester.tryCopyButtonLabel "response-body" "Copy")
    }

let tests =
    testList
        "the copy buttons"
        [ testCaseAsync "copy each section, flash the label, and leave sections collapsed" theCopyButtons ]
