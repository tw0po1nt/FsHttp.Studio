// Plumbing every product check shares: the CodeLens vocabulary each fixture renders, the fixture
// lookup each check does, and the viewer read each viewer assertion starts from. A check file
// keeps only its own tells, so two checks cannot drift apart on the parts that are not their
// subject. Lands here rather than in `Harness`, which owns setup, budgets, and the wait
// combinator — this module owns nothing but what a check reuses.
module Checks

open System.IO

/// The lens's rendered title, glyph included, exactly as `CodeLensProvider.buildCodeLens` writes
/// it. Asserted as rendered, and reused as the partial title a click matches on.
let lensTitle = "▶ Run request"

/// The line-1 lens when the script failed to parse and holds no block
/// (docs/spec/0014-explain-missing-lenses.md, Decision 2). Named here beside `lensTitle` so a
/// check reads the two together, and derived from the product in `ExTester`, whose lens harvest
/// has to recognize the same span. Not retyped: two copies of a shipped sentence drift apart.
let noRequestsLensTitle = ExTester.noRequestsLensTitle

/// A fixture checked in beside the sidecar. The sidecar path is the only location the suite is
/// handed at run time, so every fixture is resolved from it.
let private fixturePath (fileName: string) =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot locate its fixture"
    | Some sidecar -> Path.Combine(Path.GetDirectoryName sidecar, fileName)

/// The one way this suite counts the lines of a document, so a reading taken from disk and a
/// reading taken from the editor are comparable. Line endings are normalized and a trailing
/// newline is dropped: a file that ends in a newline and the editor's copy of that file hold the
/// same lines, and an off-by-one here would fail every fixture.
let private lineCount (text: string) =
    text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length

/// The fixture's size as the workspace holds it, or `None` when the file cannot be read. The suite
/// runs in Node, so it reads the workspace directly rather than asking the editor about it. This is
/// the size a correctly loaded buffer has.
let private fixtureLineCountOnDisk (fileName: string) =
    try
        let path = fixturePath fileName

        if Proc.fileExists path then
            Some(lineCount (Proc.readFile path))
        else
            None
    with _ ->
        None

let private describeFixtureOnDisk (fileName: string) =
    match fixtureLineCountOnDisk fileName with
    | Some lines -> sprintf "%i lines on disk" lines
    | None -> sprintf "a %s the suite could not read" fileName

/// The titles a poll read, as one line for a failure message. Quoted individually, because a title
/// carries a glyph and a space, and an unquoted list of them cannot show where one ends.
let describeTitles (titles: string[]) =
    if Array.isEmpty titles then
        "0 CodeLenses"
    else
        let quoted = titles |> Array.map (fun t -> sprintf "\"%s\"" t) |> String.concat ", "
        sprintf "%i CodeLenses: %s" titles.Length quoted

/// A read that raised, worded so a reader cannot mistake it for an editor that painted no lens.
let describeReadFailure (reason: string) =
    sprintf "no reading at all — the CodeLens query raised: %s" reason

/// True when the editor holds the fixture once, measured in lines against the file on disk.
///
/// VSCode from 1.123.0 loads every file of a folder workspace twice, and `extester.config.json`
/// pins the editor below that version for exactly this reason. The tab of a doubled document
/// reports clean and the file on disk is unchanged, so the size is the only tell. Every later
/// reading answers a doubled document the same way a correct one answers it, up to the point where
/// a lens count reads like a provider that paints twice — which is a defect in a different
/// component. This claim separates the two, at the open, and it is what makes a pin that stops
/// working visible on the run that raises it rather than three checks later.
let private tryFixtureLoadedOnce (fileName: string) =
    async {
        match! ExTester.tryFixtureBufferText () with
        | None -> return Harness.Observed "no reading at all — the fixture editor could not be reached"
        | Some text ->
            match fixtureLineCountOnDisk fileName with
            | None -> return Harness.Observed(sprintf "a %s the suite could not read" fileName)
            | Some onDisk ->
                let held = lineCount text

                if held = onDisk then
                    return Harness.Holds
                else
                    let! gutter = ExTester.describeGutterExtent ()

                    return
                        Harness.Observed(
                            sprintf
                                "a document of %i lines, against %s, with %s"
                                held
                                (describeFixtureOnDisk fileName)
                                gutter
                        )
    }

/// Opens a fixture as the sole tab in the fixture column, and returns once the column holds it and
/// nothing else.
///
/// The open runs once and is not polled. Every wait after it is a read or an idempotent command,
/// so a poll cannot open the same fixture a second time.
///
/// A column that already holds exactly this tab is left alone, so a check may call this against a
/// fixture the previous check opened without paying the reopen.
let openFixtureAsSoleTab (tabTitle: string) =
    async {
        let! alreadySoleTab = ExTester.tryFixtureColumnHoldsOnly tabTitle

        if not alreadySoleTab then
            match! ExTester.openFixtureInColumn (fixturePath tabTitle) with
            | ExTester.FixtureOpenRequested -> ()
            | ExTester.FixtureOpenRaised reason ->
                Assert.fail (sprintf "opening %s in the fixture column failed: %s" tabTitle reason)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                (sprintf "the fixture column to hold %s and nothing else" tabTitle)
                (fun () -> ExTester.tryCloseOtherTabsInFixtureColumn tabTitle)

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                (sprintf "the fixture column's document to be %s, loaded once" tabTitle)
                (fun () -> tryFixtureLoadedOnce tabTitle)
    }

/// Exactly one lens per block, each carrying the Run request title. An exact count is the claim the
/// spec makes — a provider that over-detects and stacks an extra lens is as wrong as one that finds
/// only the first block. Those two defects time out identically, so a poll that does not hold
/// reports the titles it read and the log names which one occurred without a screenshot.
let tryRunRequestLensAboveEachBlock (blockCount: int) (fileName: string) =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed reason -> return Harness.Observed(describeReadFailure reason)
        | ExTester.LensTitles titles ->
            if
                titles.Length = blockCount
                && titles |> Array.forall (fun t -> t.Contains lensTitle)
            then
                return Harness.Holds
            else
                // The disk size and the editor layout are read only on the failing path. Together
                // they say whether a doubled count comes from a document that holds the file twice
                // or from lens elements the editor kept from the tab it showed before.
                let! layout = ExTester.describeLensLayout ()

                return
                    Harness.Observed(
                        sprintf "%s, against %s, in %s" (describeTitles titles) (describeFixtureOnDisk fileName) layout
                    )
    }

/// Exactly `blockCount` lenses, each title equal to `expectedTitle`. The count is exact for the
/// same reason `tryRunRequestLensAboveEachBlock` makes it exact: a provider that paints the right
/// title twice is a defect, and a check that accepts any number above zero cannot see it.
let tryOnlyLensTitle (blockCount: int) (expectedTitle: string) =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed reason -> return Harness.Observed(describeReadFailure reason)
        | ExTester.LensTitles titles ->
            if
                titles.Length = blockCount
                && titles |> Array.forall (fun t -> t = expectedTitle)
            then
                return Harness.Holds
            else
                let! layout = ExTester.describeLensLayout ()
                return Harness.Observed(sprintf "%s, in %s" (describeTitles titles) layout)
    }

/// True when no rendered lens title contains the Run request title. Pair with a prior tell that
/// the provider has already painted lenses on this block — absence alone is not meaningful.
/// A read that failed reports `false` rather than absence. This tell claims that no Run request
/// lens is painted, and a query that never ran is no evidence for that claim.
let tryNoRunRequestLens () =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed _ -> return false
        | ExTester.LensTitles titles -> return titles |> Array.forall (fun t -> not (t.Contains lensTitle))
    }

/// Reads the viewer's DOM and applies `holds` to it. A frame that cannot be entered yet is a
/// normal poll result, so it reads as "does not hold" rather than an exception.
let viewerSatisfies (holds: ExTester.ResponseViewerDom -> bool) =
    async {
        match! ExTester.tryReadResponseViewer () with
        | None -> return false
        | Some dom -> return holds dom
    }

// --- the echo fixture ---------------------------------------------------------------------

/// The fixture that POSTs a header and a body to `/echo`, and the single block it holds. Two
/// checks drive it: the Request section reads what was sent, and the copy buttons put it on the
/// clipboard. Named here so neither can drift onto a different fixture than the other.
let echoFixtureFileName = "request-section.fsx"

let private echoBlockCount = 1

/// The absolute URL the block sent, scheme and port included, on the same terms as the core
/// path's: the fixture computes it, so a status line built from source text could not contain it.
let echoUrl () = Harness.baseUrl () + "/echo"

/// The response arrived and is the echo route's acknowledgement. Asserted before a check reaches
/// its own subject, so a later read cannot race a viewer that has not painted this Run yet.
let tryEchoResponseRendered () =
    viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains(echoUrl ())
        && dom.JsonBodyText.Contains Harness.echoAckKey)

/// Opens the echo fixture as the sole tab, runs its one block, and returns once the viewer has
/// painted the acknowledgement.
///
/// Opens through `openFixtureAsSoleTab` for the reason the core path documents: a second tab in
/// the column can put the lens read on a hidden editor that carries no widgets.
let runEchoFixture () =
    async {
        do! openFixtureAsSoleTab echoFixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above the fixture's single block"
                (fun () -> tryRunRequestLensAboveEachBlock echoBlockCount echoFixtureFileName)

        do!
            Harness.eventually Harness.LensAppearanceDeadlineMs "a click on the Run request lens" (fun () ->
                ExTester.tryClickCodeLensByTitle lensTitle)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the absolute URL the block posted to, and the echo acknowledgement"
                tryEchoResponseRendered
    }

/// A successful `/json` Run as the viewer renders it: status 200, `urlTell` somewhere in the
/// status line's URL, and the probe body matched by its key *and* its value from the
/// cross-process contract in `Harness`. The key alone would pass against an empty or wrong value.
/// The core path proves this shape first and the companion-death check reuses it for the recovery
/// Run, so the two cannot drift on what a Run having succeeded looks like.
///
/// `urlTell` is whatever the calling check needs the URL to carry: the core path passes the whole
/// absolute URL, because proving the URL is the one the block *sent* is its business; a check that
/// only needs to tell two routes apart passes a path segment.
let tryJsonProbeResponseRendered (urlTell: string) =
    viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains urlTell
        && dom.JsonBodyText.Contains Harness.jsonProbeKey
        && dom.JsonBodyText.Contains Harness.jsonProbeValue)
