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

/// Damage above every block: exactly one line-1 lens, no Run lens, and a click opens nothing.
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
/// hold for a settle window: an empty reading before locate returns is not evidence, and a lens
/// that appears mid-window fails the check.
let private emptySettleMs = 3_000.0

let private tryNoCodeLensesThroughSettle (settleUntil: float) =
    async {
        match! ExTester.tryReadCodeLensTitles () with
        | ExTester.LensReadFailed reason ->
            return Harness.Observed(sprintf "no reading at all — the CodeLens query raised: %s" reason)
        | ExTester.LensTitles titles when titles.Length > 0 ->
            let! layout = ExTester.describeLensLayout ()
            let quoted = titles |> Array.map (fun t -> sprintf "\"%s\"" t) |> String.concat ", "

            return Harness.Observed(sprintf "%i CodeLenses: %s, in %s" titles.Length quoted layout)
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

        let settleUntil = Proc.now () + emptySettleMs

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
