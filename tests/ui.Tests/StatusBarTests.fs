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

let private pendingBody = Checks.statusBarText "looking for requests…"
let private oneBody = Checks.statusBarText "1 request"
let private manyBody = Checks.statusBarText "2 requests"
let private emptyBody = Checks.statusBarText "no requests found"
let private notScriptBody = Checks.statusBarText "not an .fsx script"

let private syntaxEmptyBody =
    Checks.statusBarText "no requests found — syntax error"

let private syntaxPartialBody =
    Checks.statusBarText "1 requests — a syntax error can hide others"

/// How long a claim that the status stayed on one text must keep holding. A background `locate`
/// for an inactive tab that wrongly overwrites the item would land inside this window.
let private statusStableMs = 3_000.0

let private tryStatusStays (expected: string) (stableUntil: float) =
    async {
        match! ExTester.tryReadFsHttpStatus () with
        | ExTester.StatusText text when text = expected && Proc.now () < stableUntil -> return Harness.DoesNotHold
        | ExTester.StatusText text when text = expected -> return Harness.Holds
        | ExTester.StatusText text -> return Harness.Observed(sprintf "status %s" text)
        | ExTester.StatusHidden -> return Harness.Observed "a hidden FsHttp.Studio status item"
        | ExTester.StatusUnreadable reason -> return Harness.Observed(sprintf "no status reading: %s" reason)
    }

let private waitForStatus (expected: string) (subject: string) =
    Harness.eventuallyObserved Harness.LensAppearanceDeadlineMs subject (fun () -> Checks.tryStatusBarText expected)

/// Count rows for a clean `.fsx` script: one, many, and zero.
let private cleanScriptCounts =
    async {
        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneBody "FsHttp.Studio: 1 request on a one-block script"

        do! Checks.openFixtureAsSoleTab manyFixture
        do! waitForStatus manyBody "FsHttp.Studio: 2 requests on a two-block script"

        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyBody "FsHttp.Studio: no requests found on a clean empty script"
    }

/// An `.fs` module is F# but not a script, so the Ready row names that boundary.
let private notAnFsxScript =
    async {
        do! Checks.openFixtureAsSoleTab moduleFixture
        do! waitForStatus notScriptBody "FsHttp.Studio: not an .fsx script on a .fs module"
    }

/// Parse-failure rows: total loss and partial loss.
let private syntaxErrorRows =
    async {
        do! Checks.openFixtureAsSoleTab aboveFixture

        do! waitForStatus syntaxEmptyBody "FsHttp.Studio: no requests found — syntax error on total loss"

        do! Checks.openFixtureAsSoleTab betweenFixture

        do! waitForStatus syntaxPartialBody "FsHttp.Studio: N requests — a syntax error can hide others on partial loss"
    }

/// Hide outside F#, then show again on an `.fsx` script.
let private hidesOutsideFSharp =
    async {
        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneBody "FsHttp.Studio: 1 request before leaving F#"

        do! Checks.openFixtureAsSoleTab otherFixture

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the FsHttp.Studio status item hidden on a Markdown document"
                Checks.tryStatusBarHidden

        do! Checks.openFixtureAsSoleTab oneFixture
        do! waitForStatus oneBody "FsHttp.Studio: 1 request after returning to an .fsx script"
    }

/// Switching the active editor to another open `.fsx` document resets to pending, then the first
/// `locate` replaces that text in place.
let private pendingOnDocumentSwitch =
    async {
        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyBody "no requests found on the empty script first"

        do! Checks.openFixtureKeepingOthers manyFixture
        do! waitForStatus manyBody "2 requests before switching back"

        do! ExTester.previousEditor ()

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "looking for requests… after switching to another open .fsx document"
                (fun () -> Checks.tryStatusBarText pendingBody)

        do! waitForStatus emptyBody "no requests found after the first locate on the switched script"
    }

/// A `locate` for an inactive tab must not change the status bar. Keep the empty script open,
/// focus the many script, and hold its count through a settle window while the empty tab can
/// still receive lens queries.
let private inactiveLocateIgnored =
    async {
        do! Checks.openFixtureAsSoleTab emptyFixture
        do! waitForStatus emptyBody "no requests found on the empty script first"

        do! Checks.openFixtureKeepingOthers manyFixture
        do! waitForStatus manyBody "2 requests while the empty script stays open in the background"

        let stableUntil = Proc.now () + statusStableMs

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "2 requests held while an inactive tab can still locate"
                (fun () -> tryStatusStays manyBody stableUntil)
    }

let tests =
    testList
        "document-aware status bar"
        [ testCaseAsync "clean .fsx scripts report one, many, and zero requests" cleanScriptCounts
          testCaseAsync "an .fs module reads not an .fsx script" notAnFsxScript
          testCaseAsync "syntax-error scripts report total loss and partial loss" syntaxErrorRows
          testCaseAsync "the item hides outside F# and returns on an .fsx script" hidesOutsideFSharp
          testCaseAsync "a document switch reads looking for requests… until locate" pendingOnDocumentSwitch
          testCaseAsync "a locate for an inactive document does not change the status" inactiveLocateIgnored ]
