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

/// A fixture checked in beside the sidecar. The sidecar path is the only location the suite is
/// handed at run time, so every fixture is resolved from it.
let private fixturePath (fileName: string) =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot locate its fixture"
    | Some sidecar -> Path.Combine(Path.GetDirectoryName sidecar, fileName)

/// How many lines the fixture has in the workspace, or `None` when the file cannot be read. The
/// suite runs in Node, so it reads the workspace directly rather than asking the editor about it.
/// This is the size a correctly loaded buffer has, and the tell that separates a provider painting
/// twice from a file the editor loaded twice.
let private fixtureLineCountOnDisk (fileName: string) =
    try
        let path = fixturePath fileName

        if Proc.fileExists path then
            Some(Proc.readFile(path).Split('\n').Length)
        else
            None
    with _ ->
        None

let private describeFixtureOnDisk (fileName: string) =
    match fixtureLineCountOnDisk fileName with
    | Some lines -> sprintf "%i lines on disk" lines
    | None -> sprintf "a %s the suite could not read" fileName

/// One attempt to open `tabTitle` in the fixture column. Holds as soon as the Explorer item has
/// been clicked — *not* when the tab has rendered — because the click must happen once, and a poll
/// that waited for the tab here would click again while the first open was still settling. The
/// only repeated poll is the one that reached nothing and changed nothing.
let private tryOpenFixture (tabTitle: string) =
    async {
        let! alreadySoleTab = ExTester.tryFixtureColumnHoldsOnly tabTitle

        if alreadySoleTab then
            return Harness.Holds
        else
            match! ExTester.openFixtureInColumn tabTitle with
            | ExTester.FixtureOpenRequested -> return Harness.Holds
            | ExTester.FixtureOpenNotReached reason -> return Harness.Observed reason
            | ExTester.FixtureOpenRaised reason ->
                return Assert.fail (sprintf "opening %s in the fixture column failed: %s" tabTitle reason)
    }

/// Opens a fixture as the sole tab in the fixture column, and returns once the column holds it and
/// nothing else.
///
/// Two waits, because the two steps have opposite retry rules. Clicking the Explorer item is not
/// idempotent — a second click concatenates the buffer into itself, and the doubled buffer renders
/// doubled lenses — so the first wait stops at the click. Closing the column's other tabs is
/// idempotent, so the second wait polls it until the column settles.
///
/// A column that already holds exactly this tab is left alone, so a check may call this against a
/// fixture the previous check opened without paying the reopen.
let openFixtureAsSoleTab (tabTitle: string) =
    async {
        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                (sprintf "the Explorer to offer %s" tabTitle)
                (fun () -> tryOpenFixture tabTitle)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                (sprintf "the fixture column to hold %s and nothing else" tabTitle)
                (fun () -> ExTester.tryCloseOtherTabsInFixtureColumn tabTitle)

        // A check reads lenses against the fixture, so the buffer must be the fixture. VSCode has
        // been seen loading a file into its buffer twice, and every block then carries a second
        // lens the fixture does not describe.
        match fixtureLineCountOnDisk tabTitle with
        | None -> Assert.fail (sprintf "the workspace holds no readable %s to check the buffer against" tabTitle)
        | Some diskLineCount ->
            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    (sprintf "the %s buffer to hold the file once, not twice" tabTitle)
                    (fun () -> ExTester.tryFixtureBufferMatchesDisk diskLineCount)
    }

/// The titles a poll read, as one line for a failure message. Quoted individually, because a title
/// carries a glyph and a space, and an unquoted list of them cannot show where one ends.
let private describeTitles (titles: string[]) =
    if Array.isEmpty titles then
        "0 CodeLenses"
    else
        let quoted = titles |> Array.map (fun t -> sprintf "\"%s\"" t) |> String.concat ", "
        sprintf "%i CodeLenses: %s" titles.Length quoted

/// A read that raised, worded so a reader cannot mistake it for an editor that painted no lens.
let private describeReadFailure (reason: string) =
    sprintf "no reading at all — the CodeLens query raised: %s" reason

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
                // Both sizes are read only on the failing path. A count that reads double is a
                // provider that painted twice, a file the driver opened twice, or a file something
                // wrote twice, and only the buffer beside the disk separates the three.
                let! buffer = ExTester.describeFixtureBuffer ()

                return
                    Harness.Observed(
                        sprintf "%s over %s, against %s" (describeTitles titles) buffer (describeFixtureOnDisk fileName)
                    )
    }

/// At least one lens carrying `expectedTitle`, and every rendered lens title equal to it. A DOM
/// that paints the same title twice still holds; a mixed Run-request lens does not.
let tryOnlyLensTitle (expectedTitle: string) =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed reason -> return Harness.Observed(describeReadFailure reason)
        | ExTester.LensTitles titles ->
            if titles.Length >= 1 && titles |> Array.forall (fun t -> t = expectedTitle) then
                return Harness.Holds
            else
                return Harness.Observed(describeTitles titles)
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

/// A successful `/json` Run as the viewer renders it: status 200, the block's own URL path, and
/// the probe body matched by its key *and* its value from the cross-process contract in `Harness`.
/// The key alone would pass against an empty or wrong value. The core path proves this shape
/// first and the companion-death check reuses it for the recovery Run, so the two cannot drift on
/// what a Run having succeeded looks like.
let tryJsonProbeResponseRendered (urlPath: string) =
    viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains urlPath
        && dom.JsonBodyText.Contains Harness.jsonProbeKey
        && dom.JsonBodyText.Contains Harness.jsonProbeValue)
