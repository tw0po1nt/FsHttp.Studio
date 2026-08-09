// Cross-block Refused Run: a reachable second block that depends on a value the first binds
// renders ordinary Run lenses, then a Run of the second block paints a Refused Run notice in the
// viewer and leaves the Problems view empty for the fixture. Spec 0009, as one check. The shipped
// words come from `Refusals.forRefused` with the fixture's binding name — this check does not
// restate them.
module CrossBlockRefusedRunTests

open Fable.Core
open Fable.Mocha

let private blockCount = 2
let private fixtureFileName = "cross-block.fsx"
/// The name the first block binds and the second reads. Must match the fixture.
let private bindingName = "dexId"
let private unboundCode = "unboundBlockValue"
/// Zero-based index of the second block's lens. Both lenses share `Checks.lensTitle`.
let private secondLensIndex = 1

let private unbound = Refusals.forRefused unboundCode (Some bindingName)

let private tryClickSecondLens () =
    ExTester.tryClickCodeLensByIndex secondLensIndex

/// The positive tell and the notice shape together: the shipped heading and detail on the
/// refusal elements, and no status line, headers section, or runtime-error text. Absence is
/// asserted only inside this settled state.
let private tryRefusedRunRenderedAsNotice () =
    Checks.viewerSatisfies (fun dom ->
        dom.RefusalTitleText = unbound.Title
        && dom.RefusalDetailText = unbound.Detail
        && dom.StatusLineText = ""
        && dom.HeadersText = ""
        && not (dom.RootText.Contains Harness.runtimeErrorLabel))

/// Inherits a session whose response viewer is closed (the loop-lens check left it closed). Opens
/// this check's fixture as the sole tab in the fixture column, runs the second block, and leaves
/// the viewer open showing the refusal.
let private crossBlockRefusedRun =
    async {
        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "the cross-block fixture tab to open as the fixture column's only tab"
                (fun () -> ExTester.tryOpenAsSoleTabInFixtureColumn fixtureFileName)

        // Opening after earlier checks have emptied the column can leave the buffer doubled.
        // Reset from disk once before waiting on lenses.
        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "the fixture editor buffer to match the on-disk fixture"
                (fun () -> ExTester.tryResetEditorFromDisk fixtureFileName)

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the second block's Run request lens"
                tryClickSecondLens

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the unboundBlockValue refusal as a notice in the viewer"
                tryRefusedRunRenderedAsNotice

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "no Problems markers attributed to the fixture"
                (fun () -> ExTester.tryNoProblemsForFixture fixtureFileName)
    }

let tests =
    testList
        "cross-block Refused Run"
        [ testCaseAsync "refused notice in the viewer, no fault markers in the script" crossBlockRefusedRun ]
