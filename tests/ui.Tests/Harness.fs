// Shared harness for the UI test suite: ExTester setup, budgets, and the sanctioned wait
// combinator. Checks import this module instead of inventing their own wait loops or budgets.
module Harness

open Fable.Core
open Fable.Core.JsInterop

/// Default wait for a CodeLens to appear in the workbench.
let LensAppearanceDeadlineMs = 45_000

/// Default wait for the response viewer to repaint after a Run.
let ViewerUpdateDeadlineMs = 30_000

/// Default wait for a toast notification.
let ToastDeadlineMs = 15_000

/// Default wait for the editor to recover after a reload.
let PostReloadRecoveryDeadlineMs = 60_000

/// Green-path budget for the `before` hook through proven-live.
let SetupBudgetMs = 180_000

/// Green-path budget for one check, first action through last assertion.
let PerCheckBudgetMs = 45_000

/// Green-path budget for the suite, excluding setup.
let SuiteBudgetMs = 240_000

/// Cross-process contract for `GET /json`. Must match `UiTestServer.Server.jsonProbeBody`.
let jsonProbeBody = """{"probe":"ui-test-server"}"""

let private companionPattern = "Companion.dll"
let private extensionStatusPrefix = "FsHttp.Studio"
let private fixtureTabSuffix = "setup.fsx"

/// Retry spacing between polls. Deliberately not a parameter: the deadline is what a check tunes,
/// and a per-call interval would put a magic number in every check body.
let PollIntervalMs = 250

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

type ProvenLive =
    { WorkbenchReady: bool
      ServerLive: bool
      FixtureOpen: bool
      ExtensionActive: bool
      CompanionRunning: bool }

let mutable private provenLive: ProvenLive option = None
let mutable private setupElapsedMs = 0.0
let mutable private timingSummaryPrinted = false

let mutable private suiteStartMs: float option = None
let mutable private checkStartMs: float option = None
let mutable private checkRows = ResizeArray<string * float * float>()

let provenLiveState () = provenLive

let setupElapsed () = setupElapsedMs

let timingSummaryWasPrinted () = timingSummaryPrinted

let isProvenLive () =
    match provenLive with
    | Some state ->
        state.WorkbenchReady
        && state.ServerLive
        && state.FixtureOpen
        && state.ExtensionActive
        && state.CompanionRunning
    | None -> false

/// Polls `predicate` until it holds or `timeoutMs` elapses. The predicate is async because every
/// observation of the running editor returns a promise; wrap a synchronous condition in
/// `async { return ... }`. `subject` names what is being waited on, so a timeout reads as the
/// surface that never arrived rather than a bare elapsed time.
let eventually (timeoutMs: int) (subject: string) (predicate: unit -> Async<bool>) : Async<unit> =
    let rec loop (deadline: float) =
        async {
            let! holds = predicate ()

            if holds then
                return ()
            elif nowMs () >= deadline then
                Assert.fail (sprintf "Timed out after %i ms waiting for %s" timeoutMs subject)
            else
                do! Async.Sleep PollIntervalMs
                return! loop deadline
        }

    async {
        let deadline = nowMs () + float timeoutMs
        return! loop deadline
    }

let private failSetup (cause: string) =
    Assert.fail (sprintf "Harness setup failed: %s" cause)

let private verifySidecarLive () =
    let path = Proc.env "UI_TEST_SIDECAR" ""

    if path = "" then
        failSetup "UI_TEST_SIDECAR is not set"

    match Proc.tryParseSidecar path with
    | None -> failSetup (sprintf "sidecar file is missing or does not parse at %s" path)
    | Some(baseUrl, deadUrl) ->
        let body = Proc.httpBody (baseUrl + "/json")

        if body <> jsonProbeBody then
            failSetup (
                sprintf "test server healthcheck failed at %s/json (got %A, expected %s)" baseUrl body jsonProbeBody
            )

        if not (Proc.curlConnectionRefused deadUrl) then
            failSetup (sprintf "dead port answered at %s — sidecar may be stale" deadUrl)

let private tryFixtureOpen () =
    async {
        try
            let view = ExTester.EditorView.create ()
            let! titles = view.getOpenEditorTitles () |> Async.AwaitPromise
            return titles |> Array.exists (fun t -> t.Contains fixtureTabSuffix)
        with _ ->
            return false
    }

let private tryExtensionActive () =
    async {
        try
            let bar = ExTester.StatusBar.create ()
            let! items = bar.getItems () |> Async.AwaitPromise
            let mutable found = false

            for item in items do
                if not found then
                    let! text = item.getText () |> Async.AwaitPromise

                    if text.Contains extensionStatusPrefix then
                        found <- true

            return found
        with _ ->
            return false
    }

let private tryCompanionRunning () =
    async { return Proc.pidsMatching companionPattern |> Array.isEmpty |> not }

let private runSetup () =
    async {
        let setupStart = nowMs ()
        let browser = ExTester.VSBrowser.instance

        do! ExTester.waitForWorkbench browser 120_000. |> Async.AwaitPromise

        verifySidecarLive ()

        do! eventually PostReloadRecoveryDeadlineMs "the harness fixture tab to open" (fun () -> tryFixtureOpen ())

        do!
            eventually PostReloadRecoveryDeadlineMs "FsHttp.Studio to activate in the status bar" (fun () ->
                tryExtensionActive ())

        do! eventually PostReloadRecoveryDeadlineMs "the companion process to exist" (fun () -> tryCompanionRunning ())

        setupElapsedMs <- nowMs () - setupStart

        provenLive <-
            Some
                { WorkbenchReady = true
                  ServerLive = true
                  FixtureOpen = true
                  ExtensionActive = true
                  CompanionRunning = true }

        return ()
    }

let private assertBudget (label: string) (budgetMs: float) (elapsedMs: float) =
    if elapsedMs > budgetMs then
        Assert.fail (sprintf "%s exceeded the %i s budget (observed %.0f ms)" label (int (budgetMs / 1000.0)) elapsedMs)

let private printTimingTable () =
    let rows =
        ("Harness setup", setupElapsedMs, float SetupBudgetMs)
        :: (checkRows |> Seq.toList)

    let suiteElapsed =
        match suiteStartMs with
        | Some start -> nowMs () - start
        | None -> 0.0

    let bodyLines =
        rows
        |> List.map (fun (name, elapsed, budget) -> sprintf "| %s | %.0f ms | %.0f ms |" name elapsed budget)
        |> String.concat "\n"

    let md =
        "| Phase | Elapsed | Budget |\n| --- | ---: | ---: |\n"
        + bodyLines
        + "\n"
        + sprintf "| Suite total | %.0f ms | %.0f ms |\n" suiteElapsed (float SuiteBudgetMs)

    Proc.appendJobSummary md
    timingSummaryPrinted <- true

[<Emit("$0.fullTitle()")>]
let private testFullTitle (_test: obj) : string = jsNative

let private onAfterEach (test: obj option) =
    async {
        let title =
            match test with
            | None -> "unknown check"
            | Some t -> testFullTitle t

        let elapsed =
            match checkStartMs with
            | Some start -> nowMs () - start
            | None -> 0.0

        checkRows.Add(title, elapsed, float PerCheckBudgetMs)
        assertBudget (sprintf "Check %s" title) (float PerCheckBudgetMs) elapsed
        checkStartMs <- None
        return ()
    }

let private onAfter () =
    async {
        assertBudget "Harness setup" (float SetupBudgetMs) setupElapsedMs

        let suiteElapsed =
            match suiteStartMs with
            | Some start -> nowMs () - start
            | None -> 0.0

        assertBudget "Suite" (float SuiteBudgetMs) suiteElapsed
        printTimingTable ()
        return ()
    }

let private onBeforeEach () =
    async {
        if suiteStartMs.IsNone then
            suiteStartMs <- Some(nowMs ())

        checkStartMs <- Some(nowMs ())
        return ()
    }

[<ImportMember(from = "./harness-hooks.mjs")>]
let private registerHarnessHooks
    (setupFn: unit -> JS.Promise<unit>)
    (beforeEachFn: unit -> JS.Promise<unit>)
    (afterEachFn: obj -> JS.Promise<unit>)
    (afterFn: unit -> JS.Promise<unit>)
    : unit =
    jsNative

/// Registers Mocha hooks through ExTester so failures capture screenshots. Call once from Main
/// before `Mocha.runTests`.
let registerHooks () : unit =
    let setup: unit -> JS.Promise<unit> = fun () -> runSetup () |> Async.StartAsPromise

    let beforeEachHook: unit -> JS.Promise<unit> =
        fun () -> onBeforeEach () |> Async.StartAsPromise

    let afterEachHook (test: obj) : JS.Promise<unit> =
        let wrapped =
            if System.Object.ReferenceEquals(test, null) then
                None
            else
                Some test

        onAfterEach wrapped |> Async.StartAsPromise

    let afterHook: unit -> JS.Promise<unit> =
        fun () -> onAfter () |> Async.StartAsPromise

    registerHarnessHooks setup beforeEachHook afterEachHook afterHook

/// Writes the timing table to the job summary. The `after` hook calls this too; the self-check
/// calls it so a one-check run still proves the summary path before `after`.
let writeTimingSummary () = printTimingTable ()
