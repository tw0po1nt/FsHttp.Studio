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
let fixturePath (fileName: string) =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot locate its fixture"
    | Some sidecar -> Path.Combine(Path.GetDirectoryName sidecar, fileName)

/// The titles a poll read, as one line for a failure message. Quoted individually, because a title
/// carries a glyph and a space, and an unquoted list of them cannot show where one ends.
let private describeTitles (titles: string[]) =
    if Array.isEmpty titles then
        "0 CodeLenses"
    else
        let quoted = titles |> Array.map (fun t -> sprintf "\"%s\"" t) |> String.concat ", "
        sprintf "%i CodeLenses: %s" titles.Length quoted

/// Exactly one lens per block, each carrying the Run request title. An exact count is the claim the
/// spec makes — a provider that over-detects and stacks an extra lens is as wrong as one that finds
/// only the first block. Those two defects time out identically, so a poll that does not hold
/// reports the titles it read and the log names which one occurred without a screenshot.
let tryRunRequestLensAboveEachBlock (blockCount: int) =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()
        let! lineCount = ExTester.tryReadEditorLineCount ()

        if
            titles.Length = blockCount
            && titles |> Array.forall (fun t -> t.Contains lensTitle)
        then
            return Harness.Holds
        else
            let lines =
                match lineCount with
                | Some n -> sprintf ", editor has %i lines" n
                | None -> ""

            return Harness.Observed(sprintf "%s%s" (describeTitles titles) lines)
    }

/// At least one lens carrying `expectedTitle`, and every rendered lens title equal to it. A DOM
/// that paints the same title twice still holds; a mixed Run-request lens does not.
let tryOnlyLensTitle (expectedTitle: string) =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()

        if titles.Length >= 1 && titles |> Array.forall (fun t -> t = expectedTitle) then
            return Harness.Holds
        else
            return Harness.Observed(describeTitles titles)
    }

/// True when no rendered lens title contains the Run request title. Pair with a prior tell that
/// the provider has already painted lenses on this block — absence alone is not meaningful.
let tryNoRunRequestLens () =
    async {
        let! titles = ExTester.tryReadCodeLensTitles ()
        return titles |> Array.forall (fun t -> not (t.Contains lensTitle))
    }

/// Reads the viewer's DOM and applies `holds` to it. A frame that cannot be entered yet is a
/// normal poll result, so it reads as "does not hold" rather than an exception.
let viewerSatisfies (holds: ExTester.ResponseViewerDom -> bool) =
    async {
        match! ExTester.tryReadResponseViewer () with
        | None -> return false
        | Some dom -> return holds dom
    }
