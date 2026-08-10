// Compile Error names its source: the check breaks a known line above a reachable block, runs
// the block from the unsaved buffer, and asserts the viewer reports a compile error at that
// line's position. Spec 0010, as one check. The fixture on disk is never written; the buffer is
// restored through the workbench's revert-file command even when the body fails partway.
module CompileErrorTests

open Fable.Mocha

let private fixtureFileName = "compile-error.fsx"
/// 1-based line the fixture marks as the break-target. Must match `compile-error.fsx`.
let private brokenLine = 12
/// 1-based column of the type error on the broken line, as `formatCompileError` prints it after
/// shifting FCS's 0-based column. For `let probe : int = "not an int"`, the string starts at
/// column 19.
let private brokenColumn = 19
/// The line the check writes into the open buffer. Never saved.
let private brokenText = "let probe : int = \"not an int\""
/// Distinctive fragment of the broken line, for dirty/clean buffer tells.
let private brokenFragment = "\"not an int\""
/// Stable substring of the F# compiler's type-mismatch diagnostic. Not the full sentence — those
/// words belong to the compiler, and pinning them would make an F# upgrade look like a product
/// regression.
let private compilerMessageFragment = "expected to have type"

let private expectedPosition = sprintf "(%d,%d)" brokenLine brokenColumn

let private tryClickLens () =
    ExTester.tryClickCodeLensByTitle Checks.lensTitle

/// A compile error at the broken line's exact position, with the compiler's message present, and
/// no response structure. Absence of the status line is asserted only inside this settled state.
let private tryCompileErrorAtBrokenLine () =
    Checks.viewerSatisfies (fun dom ->
        dom.RootText.Contains Harness.compileErrorLabel
        && dom.RootText.Contains expectedPosition
        && dom.RootText.Contains compilerMessageFragment
        && dom.StatusLineText = ""
        && dom.HeadersText = ""
        && not (dom.RootText.Contains Harness.runtimeErrorLabel))

/// Restores the fixture buffer from disk and asserts it is clean. Runs after the body whether the
/// body held or failed, so a red assertion cannot leave a broken buffer for the rest of the
/// session. A failed restore fails this check.
let private revertAndAssertClean () =
    async {
        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a revert of the fixture buffer through the workbench"
                ExTester.tryRevertFixtureFile

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "the fixture buffer clean again, with the type error gone"
                (fun () -> ExTester.tryFixtureBufferLacks brokenFragment)
    }

/// Inherits the warm companion and the open viewer from the cross-block check, and takes over the
/// fixture column for its own fixture. Leaves the viewer showing the compile error and the
/// fixture buffer clean.
let private compileErrorNamesItsSource =
    async {
        let mutable needsRevert = false
        let mutable bodyError: exn option = None

        try
            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "the compile-error fixture tab to open as the fixture column's only tab"
                    (fun () -> ExTester.tryOpenAsSoleTabInFixtureColumn fixtureFileName)

            do!
                Harness.eventuallyObserved
                    Harness.LensAppearanceDeadlineMs
                    "a Run request lens above the block"
                    (fun () -> Checks.tryOnlyLensTitle Checks.lensTitle)

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "a type error on the fixture's marked line in the unsaved buffer"
                    (fun () -> ExTester.trySetFixtureLine brokenLine brokenText)

            needsRevert <- true

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "the fixture document dirty with the type error present"
                    (fun () -> ExTester.tryFixtureBufferHolds brokenFragment)

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "a click on the block's Run request lens"
                    tryClickLens

            do!
                Harness.eventually
                    Harness.ViewerUpdateDeadlineMs
                    "a compile error at the broken line in the viewer, with no status line"
                    tryCompileErrorAtBrokenLine
        with e ->
            bodyError <- Some e

        if needsRevert then
            do! revertAndAssertClean ()

        match bodyError with
        | Some e -> raise e
        | None -> ()
    }

let tests =
    testList
        "Compile Error names its source"
        [ testCaseAsync "viewer reports the compile error at the line the check broke" compileErrorNamesItsSource ]
