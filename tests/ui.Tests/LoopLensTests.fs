// Loop lens refuses with toast: a block inside a `for` loop renders the refusal lens, a click
// raises the warning toast with the shipped `loopBody` detail, and no response viewer opens.
// Spec 0008, as one check. The shipped words come from `Refusals` — the host catalog — so this
// check does not restate them.
module LoopLensTests

open Fable.Mocha

let private fixtureFileName = "loop-lens.fsx"
let private loopBodyCode = "loopBody"

let private loopBody = Refusals.forCode loopBodyCode
let private refusalLensTitle = Refusals.lensTitle loopBodyCode

let private tryClickRefusalLens () =
    ExTester.tryClickCodeLensByTitle refusalLensTitle

let private tryNoResponseViewer () =
    async {
        let! openBeside = ExTester.tryViewerBesideEditor ()
        return not openBeside
    }

/// Closes any open viewer first so the post-click absence assertion is meaningful, then drives
/// the refusal lens and its toast. Leaves the viewer closed and the toast dismissed.
let private loopLensRefusesWithToast =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        // Same shape as the core-path check: take over the fixture column rather than opening
        // beside the previous check's tab.
        do! Checks.openFixtureAsSoleTab fixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the refusal lens title above the block inside the loop"
                (fun () -> Checks.tryOnlyLensTitle refusalLensTitle)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "no Run request lens on the refused block"
                Checks.tryNoRunRequestLens

        do! Harness.eventually Harness.LensAppearanceDeadlineMs "a click on the refusal lens" tryClickRefusalLens

        do!
            Harness.eventually Harness.ToastDeadlineMs "a warning toast with the shipped loopBody detail" (fun () ->
                ExTester.tryWarningNotification loopBody.Detail)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "no response viewer open after the refusal toast"
                tryNoResponseViewer

        do!
            Harness.eventually Harness.ToastDeadlineMs "the warning toast to dismiss" (fun () ->
                ExTester.tryDismissWarningNotification loopBody.Detail)
    }

let tests =
    testList
        "loop lens refuses with toast"
        [ testCaseAsync "refusal lens, warning toast, and no viewer" loopLensRefusesWithToast ]
