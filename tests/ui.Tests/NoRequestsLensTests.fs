// No-requests lens: when a script's parse fails and the locator finds no block, one CodeLens at
// line 1 states why. Spec 0014 Decisions 1-2, as four checks that drive the real editor through
// ExTester.tryReadCodeLensTitles. Partial loss and damage below the blocks keep their Run
// lenses; only total loss with a failed parse takes the line-1 lens.
module NoRequestsLensTests

open Fable.Mocha

let private aboveFixture = "no-requests-above.fsx"
let private belowFixture = "no-requests-below.fsx"
let private betweenFixture = "no-requests-between.fsx"
let private emptyFixture = "no-requests-empty.fsx"

/// Blocks that survive in `no-requests-below.fsx`. Must match the fixture.
let private belowBlockCount = 2
/// Blocks that survive in `no-requests-between.fsx`. Must match the fixture.
let private betweenBlockCount = 1

let private tryClickNoRequestsLens () =
    ExTester.tryClickCodeLensByTitle Checks.noRequestsLensTitle

let private tryNoResponseViewer () =
    async {
        let! openBeside = ExTester.tryViewerBesideEditor ()
        return not openBeside
    }

/// The lens carries no command, so VSCode paints its title as plain text: the decoration holds a
/// `<span>` and no `<a id>` (spec 0014, Decision 2). Read from the DOM rather than from a click:
/// this fixture locates no block, so nothing this lens could have carried would open a viewer,
/// and a click that opens nothing is no evidence about the command.
let private tryNoRequestsLensIsPlainText () =
    async {
        match! ExTester.tryReadLensRendering Checks.noRequestsLensTitle with
        | ExTester.RenderedAsPlainText -> return Harness.Holds
        | ExTester.RenderedAsCommandLink ->
            return Harness.Observed "a command link (`<a id>`), so the lens carries a command"
        | ExTester.TitleNotRendered ->
            let! layout = ExTester.describeLensLayout ()
            return Harness.Observed(sprintf "no visible lens carrying the no-requests title, in %s" layout)
        | ExTester.RenderingUnreadable reason -> return Harness.Observed(Checks.describeReadFailure reason)
    }

/// Damage above every block: exactly one line-1 lens, no Run lens, plain text rather than a
/// command, and a click that opens nothing.
let private syntaxErrorAbovePaintsLine1Lens =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        do! Checks.openFixtureAsSoleTab aboveFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "exactly one no-requests lens at line 1"
                (fun () -> Checks.tryOnlyLensTitle 1 Checks.noRequestsLensTitle)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "no Run request lens beside the no-requests lens"
                Checks.tryNoRunRequestLens

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the no-requests lens rendered as plain text, with no command link"
                tryNoRequestsLensIsPlainText

        do! Harness.eventually Harness.LensAppearanceDeadlineMs "a click on the no-requests lens" tryClickNoRequestsLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "no response viewer open after a click on the plain-text lens"
                tryNoResponseViewer
    }

/// Damage below every block: each Run lens stays, and the line-1 lens does not appear.
let private syntaxErrorBelowKeepsRunLenses =
    async {
        do! Checks.openFixtureAsSoleTab belowFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each surviving block"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock belowBlockCount belowFixture)
    }

/// Damage between two blocks: one Run lens stays, and the line-1 lens does not appear.
let private syntaxErrorBetweenKeepsOneRunLens =
    async {
        do! Checks.openFixtureAsSoleTab betweenFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "exactly one Run request lens for the surviving block"
                (fun () -> Checks.tryOnlyLensTitle betweenBlockCount Checks.lensTitle)
    }

/// A clean script with no `http { }` block paints nothing. Opens after the above-damage fixture
/// so the provider has already answered once. After the empty fixture loads, empty titles must
/// hold through `Harness.LensAbsenceSettleMs`, which is where the suite states why an absence
/// needs a window at all.
let private tryNoCodeLensesThroughSettle (settleUntil: float) =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed reason -> return Harness.Observed(Checks.describeReadFailure reason)
        | ExTester.LensTitles titles when titles.Length > 0 ->
            let! layout = ExTester.describeLensLayout ()
            return Harness.Observed(sprintf "%s, in %s" (Checks.describeTitles titles) layout)
        | ExTester.LensTitles _ when Proc.now () < settleUntil -> return Harness.DoesNotHold
        | ExTester.LensTitles _ -> return Harness.Holds
    }

let private cleanEmptyScriptPaintsNoLens =
    async {
        do! Checks.openFixtureAsSoleTab aboveFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the no-requests lens on the broken fixture first"
                (fun () -> Checks.tryOnlyLensTitle 1 Checks.noRequestsLensTitle)

        do! Checks.openFixtureAsSoleTab emptyFixture

        let settleUntil = Proc.now () + Harness.LensAbsenceSettleMs

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "no CodeLens on a clean script with zero http blocks, held through the settle window"
                (fun () -> tryNoCodeLensesThroughSettle settleUntil)
    }

let tests =
    testList
        "no-requests lens"
        [ testCaseAsync "syntax error above the blocks paints the line-1 lens only" syntaxErrorAbovePaintsLine1Lens
          testCaseAsync "syntax error below the last block keeps Run lenses" syntaxErrorBelowKeepsRunLenses
          testCaseAsync "syntax error between two blocks keeps one Run lens" syntaxErrorBetweenKeepsOneRunLens
          testCaseAsync "clean script with no blocks paints no lens" cleanEmptyScriptPaintsNoLens ]
