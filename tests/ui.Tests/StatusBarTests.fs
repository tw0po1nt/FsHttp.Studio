// Document-aware status bar: each Decision 5 and Decision 6 row, read from ExTester.StatusBar
// while the real editor holds the matching fixture. Spec 0014 as UI checks — not a manual pass
// over a running extension.
module StatusBarTests

open Fable.Mocha

let private oneFixture = "status-bar-one.fsx"
let private manyFixture = "status-bar-many.fsx"
let private emptyFixture = "no-requests-empty.fsx"
let private moduleFixture = "status-bar-module.fs"
let private otherFixture = "status-bar-other.md"
let private aboveFixture = "no-requests-above.fsx"
let private betweenFixture = "no-requests-between.fsx"

let private pendingStatus = Checks.statusBarText "looking for requests…"
let private oneStatus = Checks.statusBarText "1 request"
let private manyStatus = Checks.statusBarText "2 requests"
let private emptyStatus = Checks.statusBarText "no requests found"
let private notScriptStatus = Checks.statusBarText "not an .fsx script"

let private syntaxEmptyStatus =
    Checks.statusBarText "no requests found — syntax error"

let private syntaxPartialStatus =
    Checks.statusBarText "1 requests — a syntax error can hide others"

let private waitForStatus (expected: string) (subject: string) =
    Harness.eventuallyObserved Harness.LensAppearanceDeadlineMs subject (fun () -> Checks.tryStatusBarText expected)

/// Count rows for a clean `.fsx` script: one, many, and zero.
let private cleanScriptCounts =
    async {
        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneStatus "FsHttp.Studio: 1 request on a one-block script"

        do! Checks.openFixtureAsSoleTab manyFixture
        do! waitForStatus manyStatus "FsHttp.Studio: 2 requests on a two-block script"

        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyStatus "FsHttp.Studio: no requests found on a clean empty script"
    }

/// An `.fs` module is F# but not a script, so the Ready row names that boundary.
let private notAnFsxScript =
    async {
        do! Checks.openFixtureAsSoleTab moduleFixture
        do! waitForStatus notScriptStatus "FsHttp.Studio: not an .fsx script on a .fs module"
    }

/// Parse-failure rows: total loss and partial loss.
let private syntaxErrorRows =
    async {
        do! Checks.openFixtureAsSoleTab aboveFixture

        do! waitForStatus syntaxEmptyStatus "FsHttp.Studio: no requests found — syntax error on total loss"

        do! Checks.openFixtureAsSoleTab betweenFixture

        do!
            waitForStatus
                syntaxPartialStatus
                "FsHttp.Studio: N requests — a syntax error can hide others on partial loss"
    }

/// Hide outside F#, then show again on an `.fsx` script.
let private hidesOutsideFSharp =
    async {
        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneStatus "FsHttp.Studio: 1 request before leaving F#"

        do! Checks.openFixtureAsSoleTab otherFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the FsHttp.Studio status item hidden on a Markdown document"
                Checks.tryStatusBarHidden

        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneStatus "FsHttp.Studio: 1 request after returning to an .fsx script"
    }

/// Switching the active editor to another open `.fsx` document resets to pending, then the first
/// `locate` replaces that text in place.
let private pendingOnDocumentSwitch =
    async {
        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyStatus "no requests found on the empty script first"

        do! Checks.openFixtureKeepingOthers manyFixture
        do! waitForStatus manyStatus "2 requests before switching back"

        do! ExTester.previousEditor ()

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "looking for requests… after switching to another open .fsx document"
                (fun () -> Checks.tryStatusBarText pendingStatus)

        do! waitForStatus emptyStatus "no requests found after the first locate on the switched script"
    }

/// The active script's count settles and stays put while another script is open.
///
/// This is the weaker half of Decision 5's active-document guard, and it is deliberately labelled
/// as such. The rule itself — a response for another document is dropped — is pinned as a pure
/// value by `ProtocolTests.mirrorsActiveDocumentTests`, because the workbench cannot be made to
/// produce the losing response: a second script that is open but not active gets no lens query,
/// and one that is visible beside the active script does not locate again on demand. Removing the
/// guard from the product was measured against this check in both layouts, and it stayed green
/// either way. What it does claim is what a user would see — a count that arrives and then holds,
/// rather than one that flickers to another script's.
let private countHoldsWithASecondScriptOpen =
    async {
        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyStatus "no requests found on the empty script first"

        do! Checks.openFixtureKeepingOthers manyFixture
        do! waitForStatus manyStatus "2 requests while the empty script stays open beside it"

        let stableUntil = Proc.now () + Harness.StatusStabilitySettleMs

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "2 requests still on the item once the settle window has passed"
                (fun () -> Checks.tryStatusBarTextStays manyStatus stableUntil)
    }

let tests =
    testList
        "document-aware status bar"
        [ testCaseAsync "clean .fsx scripts report one, many, and zero requests" cleanScriptCounts
          testCaseAsync "an .fs module reads not an .fsx script" notAnFsxScript
          testCaseAsync "syntax-error scripts report total loss and partial loss" syntaxErrorRows
          testCaseAsync "the item hides outside F# and returns on an .fsx script" hidesOutsideFSharp
          testCaseAsync "a document switch reads looking for requests… until locate" pendingOnDocumentSwitch
          testCaseAsync "the active script's count holds with a second script open" countHoldsWithASecondScriptOpen ]
