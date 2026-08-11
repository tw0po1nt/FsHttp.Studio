// The copy buttons, against the running editor. Spec 0013's UI-suite section replaces the manual
// smoke: a click puts the section's payload on the clipboard, the button's own label reports the
// result, and a click never toggles the section. The Seam-B suite already proves `copyText` and
// the DOM shape; this module claims the delegated listener, the clipboard write, and the flash.
module CopyButtonsTests

open Fable.Mocha

/// Both header sections collapsed, each copy button laid out, and the three shell margins as
/// Decision 12 requires: 12px, 12px, then 0 on the last shell (the body had no bottom margin).
///
/// `Displayed` claims the button is present and has a size. Decision 2's defect — a button that
/// the browser reports as visible while it paints nothing inside a closed `<details>` — is not
/// what this measures; the spec says only a screenshot shows that, and the PR carries one.
let private tryCollapsedButtonsAndSpacing () =
    async {
        match! ExTester.tryReadCopySurface () with
        | None -> return false
        | Some surface ->
            return
                not surface.RequestOpen
                && not surface.HeadersOpen
                && surface.Buttons.Length = 3
                && (surface.Buttons
                    |> Array.forall (fun b ->
                        (b.Key = "request" || b.Key = "response-headers" || b.Key = "response-body")
                        && b.Displayed
                        && b.Label = ExTester.copyButtonRestingLabel))
                && surface.ShellMarginsPx.Length = 3
                && surface.ShellMarginsPx[0] = 12.
                && surface.ShellMarginsPx[1] = 12.
                && surface.ShellMarginsPx[2] = 0.
    }

/// A successful copy click: label `Copied`, the write was granted and `holds` against the text it
/// was handed, and neither collapsible section changed its open state from before the click.
let private tryCopySucceeded (key: string) (requestWasOpen: bool) (headersWereOpen: bool) (holds: string -> bool) =
    async {
        match! ExTester.tryClickCopyButton ExTester.Granted key with
        | None -> return false
        | Some reading ->
            return
                reading.Label = "Copied"
                && reading.Granted
                && reading.RequestOpen = requestWasOpen
                && reading.HeadersOpen = headersWereOpen
                && (match reading.WrittenText with
                    | Some text -> holds text
                    | None -> false)
    }

/// The refused write, which no platform in this suite produces on its own: the witness rejects,
/// and the button has to say so rather than leave the user to paste whatever was there before
/// (Decision 8, user story 8).
let private tryCopyReportedFailure (key: string) =
    async {
        match! ExTester.tryClickCopyButton ExTester.Refused key with
        | None -> return false
        | Some reading -> return reading.Label = "Copy failed" && not reading.Granted
    }

let private requestPayloadHolds (text: string) =
    text.StartsWith("POST " + Checks.echoUrl ())
    && text.Contains(Harness.postedHeaderName + ": " + Harness.postedHeaderValue)
    && text.Contains("\n\n" + Harness.postedBody)

let private headersPayloadHolds (text: string) =
    text.StartsWith "200 " && text.Contains "Content-Type:"

/// The echo acknowledgement as it went on the wire. Matched in parts for the reason `Harness`
/// names that contract in parts, plus the absence of a newline: the viewer pretty-prints this
/// body across several lines, so a one-line paste is the tell that the copy read the response
/// bytes and not the rendered tree (Decision 3, and user story 1).
let private bodyPayloadHolds (text: string) =
    text.Contains("\"" + Harness.echoAckKey + "\"")
    && text.Contains("\"" + Harness.echoAckValue + "\"")
    && not (text.Contains "\n")

/// Every copy click and its flash, against one Run of the echo fixture.
///
/// Each click is followed by the wait for its own label to return to `Copy`. Leaving a button
/// mid-flash would let the next `eventually` retry land inside a running flash, which is the
/// state a non-re-entrant flash breaks on.
let private clickAndAwaitRevert (key: string) (description: string) (holds: string -> bool) =
    async {
        do!
            Harness.eventually Harness.ViewerUpdateDeadlineMs description (fun () ->
                tryCopySucceeded key false false holds)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                (sprintf "the %s copy button label to return to %s" key ExTester.copyButtonRestingLabel)
                (fun () -> ExTester.tryCopyButtonLabel key ExTester.copyButtonRestingLabel)
    }

let private theCopyButtons =
    async {
        do! Checks.runEchoFixture ()

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "three laid-out Copy buttons beside collapsed sections, with shell margins 12/12/0"
                tryCollapsedButtonsAndSpacing

        do!
            clickAndAwaitRevert
                "request"
                "a click on the request copy button puts the sent request on the clipboard"
                requestPayloadHolds

        do!
            clickAndAwaitRevert
                "response-headers"
                "a click on the response-headers copy button puts the status and headers on the clipboard"
                headersPayloadHolds

        do!
            clickAndAwaitRevert
                "response-body"
                "a click on the response-body copy button puts the raw JSON body on the clipboard"
                bodyPayloadHolds

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "a refused clipboard write reported as Copy failed on the button"
                (fun () -> tryCopyReportedFailure "response-body")

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                (sprintf "the Copy failed label to return to %s" ExTester.copyButtonRestingLabel)
                (fun () -> ExTester.tryCopyButtonLabel "response-body" ExTester.copyButtonRestingLabel)
    }

let tests =
    testList
        "the copy buttons"
        [ testCaseAsync "copy each section, flash the label, and leave sections collapsed" theCopyButtons ]
