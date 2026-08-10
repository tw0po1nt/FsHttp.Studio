// Cross-block Refused Run: a reachable second block that depends on a value the first binds
// renders ordinary Run lenses, then a Run of the second block paints a Refused Run notice in the
// viewer and leaves the Problems view empty for the fixture. Spec 0009, as one check. The shipped
// words come from `Refusals.forRefused` with the fixture's binding name — this check does not
// restate them.
module CrossBlockRefusedRunTests

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
/// the viewer open showing the refusal. The bottom panel the no-fault assertion opens is closed
/// again, so the only session state a later check inherits from here is the open viewer.
let private crossBlockRefusedRun =
    async {
        do! Checks.openFixtureAsSoleTab fixtureFileName

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

        // Weaker evidence than it looks: no F# language service is installed in the suite's
        // VSCode, so nothing but FsHttp.Studio can contribute a `.fsx` diagnostic here. The
        // assertion's recorded softness is spelled out on `tryNoProblemsForFixture`. The sharp
        // half of this claim is the notice assertion above.
        do!
            Harness.eventually Harness.ViewerUpdateDeadlineMs "no Problems markers attributed to the fixture" (fun () ->
                ExTester.tryNoProblemsForFixture fixtureFileName)

        // Not asserted: the panel is housekeeping, and a close that fails must not redden a check
        // whose subject is the refusal render.
        do! ExTester.tryCloseBottomPanel () |> Async.Ignore
    }

let tests =
    testList
        "cross-block Refused Run"
        [ testCaseAsync "refused notice in the viewer, no fault markers in the script" crossBlockRefusedRun ]
