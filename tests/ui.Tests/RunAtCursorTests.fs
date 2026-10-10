// Run request at cursor: the palette command runs the Block at the caret, refuses with the toast of
// the lens, and answers a Script with no Block. Each Check runs the command from the command
// palette, as a user does.
module RunAtCursorTests

open Fable.Mocha

let private coreFixture = "core-path.fsx"
/// The line of the first `http` Block in `core-path.fsx`. Must match the fixture.
let private coreBlockLine = 27
let private loopFixture = "loop-lens.fsx"
/// The line of the Block in the loop of `loop-lens.fsx`. Must match the fixture.
let private loopBlockLine = 10
/// A line of `core-path.fsx` and of `loop-lens.fsx` that lies outside every Block. Must match the
/// fixtures.
let private coreOutsideLine = 26
let private loopOutsideLine = 9
let private emptyFixture = "no-requests-empty.fsx"

let private loopBody = Refusals.forCode "loopBody"

let private runsTheBlockAtTheCursor =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        do! Checks.openFixtureAsSoleTab coreFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock 2 coreFixture)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "Run request at cursor from the command palette"
                (fun () -> ExTester.tryRunAtCursorFromPalette coreBlockLine)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to open beside the editor"
                ExTester.tryViewerBesideEditor

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the absolute URL the first block sent, and the probe body in the response viewer"
                (fun () -> Checks.tryJsonProbeResponseRendered (Harness.baseUrl () + "/json"))
    }

let private refusesTheLoopBlockWithTheToast =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        do! Checks.openFixtureAsSoleTab loopFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the refusal lens title above the block inside the loop"
                (fun () -> Checks.tryOnlyLensTitle 1 (Refusals.lensTitle "loopBody"))

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "Run request at cursor from the command palette"
                (fun () -> ExTester.tryRunAtCursorFromPalette loopBlockLine)

        do!
            Harness.eventually Harness.ToastDeadlineMs "a warning toast with the shipped loopBody detail" (fun () ->
                ExTester.tryWarningNotification loopBody.Detail)

        let settleUntil = Proc.now () + Harness.ViewerAbsenceSettleMs

        do!
            Harness.eventuallyObserved
                Harness.ViewerUpdateDeadlineMs
                "no response viewer open after the refusal toast, true through the settle window"
                (fun () -> Checks.tryNoResponseViewerThroughSettle settleUntil)

        do!
            Harness.eventually Harness.ToastDeadlineMs "the warning toast to dismiss" (fun () ->
                ExTester.tryDismissWarningNotification loopBody.Detail)
    }

let private answersAScriptWithNoBlock =
    async {
        do! Checks.openFixtureAsSoleTab emptyFixture

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "Run request at cursor from the command palette"
                (fun () -> ExTester.tryRunAtCursorFromPalette 1)

        do!
            Harness.eventually Harness.ToastDeadlineMs "the INFO toast for a Script with no Block" (fun () ->
                ExTester.tryInfoNotification Refusals.noBlocksEmpty)

        do!
            Harness.eventually Harness.ToastDeadlineMs "the info toast to dismiss" (fun () ->
                ExTester.tryDismissInfoNotification Refusals.noBlocksEmpty)
    }

let private quickPickIs (expected: ExTester.QuickPickEntry list) () =
    async {
        let! entries = ExTester.tryQuickPickEntries ()
        return entries = Some expected
    }

let private coreEntries: ExTester.QuickPickEntry list =
    [ { Label = "▶ 27: http { GET $\"{baseUrl}/json\" }"
        Detail = None }
      { Label = "▶ 29: http { GET $\"{baseUrl}/status\" }"
        Detail = None } ]

let private loopEntries: ExTester.QuickPickEntry list =
    [ { Label = "⊘ 10: http { GET \"http://127.0.0.1:9/\" }"
        Detail = Some(Refusals.lensTitle "loopBody") } ]

let private openQuickPickOutsideEveryBlock fixture line expected =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        do! Checks.openFixtureAsSoleTab fixture

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "Run request at cursor from the command palette, with the cursor outside every Block"
                (fun () -> ExTester.tryRunAtCursorFromPalette line)

        do!
            Harness.eventually
                Harness.ToastDeadlineMs
                "a quick pick that lists each Block with its glyph, line, and first source line"
                (quickPickIs expected)
    }

let private pickRunsTheBlock =
    async {
        do! openQuickPickOutsideEveryBlock coreFixture coreOutsideLine coreEntries

        do!
            Harness.eventually Harness.ToastDeadlineMs "the first row to be picked" (fun () ->
                ExTester.tryPickQuickPick 0)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to open beside the editor"
                ExTester.tryViewerBesideEditor

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the absolute URL the first block sent, and the probe body in the response viewer"
                (fun () -> Checks.tryJsonProbeResponseRendered (Harness.baseUrl () + "/json"))
    }

let private pickOfRefusedBlockGivesTheToast =
    async {
        do! openQuickPickOutsideEveryBlock loopFixture loopOutsideLine loopEntries

        do!
            Harness.eventually Harness.ToastDeadlineMs "the refused row to be picked" (fun () ->
                ExTester.tryPickQuickPick 0)

        do!
            Harness.eventually Harness.ToastDeadlineMs "a warning toast with the shipped loopBody detail" (fun () ->
                ExTester.tryWarningNotification loopBody.Detail)

        let settleUntil = Proc.now () + Harness.ViewerAbsenceSettleMs

        do!
            Harness.eventuallyObserved
                Harness.ViewerUpdateDeadlineMs
                "no response viewer open after the refusal toast, true through the settle window"
                (fun () -> Checks.tryNoResponseViewerThroughSettle settleUntil)

        do!
            Harness.eventually Harness.ToastDeadlineMs "the warning toast to dismiss" (fun () ->
                ExTester.tryDismissWarningNotification loopBody.Detail)
    }

let private cancelStartsNothing =
    async {
        do! openQuickPickOutsideEveryBlock coreFixture coreOutsideLine coreEntries

        do! Harness.eventually Harness.ToastDeadlineMs "the quick pick to cancel" ExTester.tryCancelQuickPick

        let settleUntil = Proc.now () + Harness.ViewerAbsenceSettleMs

        do!
            Harness.eventuallyObserved
                Harness.ViewerUpdateDeadlineMs
                "no response viewer open after the cancel, true through the settle window"
                (fun () -> Checks.tryNoResponseViewerThroughSettle settleUntil)
    }

let tests =
    testList
        "run request at cursor"
        [ testCaseAsync "a runnable Block runs and the viewer shows the result" runsTheBlockAtTheCursor
          testCaseAsync "a Block in a loop gives the loopBody toast and no viewer" refusesTheLoopBlockWithTheToast
          testCaseAsync "a Script with no Block gives the INFO toast" answersAScriptWithNoBlock
          testCaseAsync "a cursor outside every Block lists each Block and a pick runs it" pickRunsTheBlock
          testCaseAsync "a pick of a refused Block gives its toast and no viewer" pickOfRefusedBlockGivesTheToast
          testCaseAsync "a canceled quick pick starts no Run" cancelStartsNothing ]
