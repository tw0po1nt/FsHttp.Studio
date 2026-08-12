// Shared harness for the UI test suite: ExTester setup, budgets, and the sanctioned wait
// combinator. Checks import this module instead of inventing their own wait loops or budgets.
module Harness

open Fable.Core
open Fable.Core.JsInterop

/// Default wait for the workbench itself to settle: a CodeLens to appear, a fixture tab to open, a
/// click to land, a buffer edit or revert to show up in the editor. Named for the lens because that
/// is the slowest of them and the one that sets the number; every check reuses it rather than
/// inventing a second workbench deadline.
let LensAppearanceDeadlineMs = 45_000

/// Default wait for the response viewer to repaint after a Run.
let ViewerUpdateDeadlineMs = 30_000

/// Default wait for a toast notification.
let ToastDeadlineMs = 15_000

/// Default wait for the editor to recover after a reload.
let PostReloadRecoveryDeadlineMs = 60_000

/// Wait for the companion to report `ready`, paid once in setup. It is the longest wait the
/// harness makes, and deliberately so: the companion's first response costs a `dotnet --list-sdks`
/// probe, a .NET process start, and FCS's first parse. On a slow runner that cold start alone has
/// been measured above 45 s. Setup absorbs it inside `SetupBudgetMs` so no product check pays it —
/// a check that waits on cold start measures the runner, not the product.
let CompanionReadyDeadlineMs = 120_000

/// How long a claim that the editor paints *no* lens must keep holding before it is believed. An
/// empty reading taken before the provider has answered is not evidence, and a lens that appears
/// mid-window fails the check that waited it out.
let LensAbsenceSettleMs = 3_000.0

/// Green-path budget for the `before` hook through proven-live.
let SetupBudgetMs = 180_000

/// Green-path budget for one check, first action through last assertion.
let PerCheckBudgetMs = 45_000

/// Green-path budget for the suite, excluding setup. Deliberately far tighter than the number of
/// checks times `PerCheckBudgetMs`: no green run comes near the per-check ceiling, and a budget
/// that summed the ceilings would catch nothing. A green run of eight checks measures around 40 s
/// locally, so this holds several times over even on a slow runner, and a check added without a
/// raise here is the intended pressure.
let SuiteBudgetMs = 240_000

/// Cross-process contract for `GET /json`. Must match `UiTestServer.Server.jsonProbeBody`. The
/// key and the value are named separately because a check reading the viewer's pretty-printed DOM
/// cannot match the one-line body — it matches these two parts, which whitespace cannot move.
let jsonProbeKey = "probe"
let jsonProbeValue = "ui-test-server"
let jsonProbeBody = sprintf """{"%s":"%s"}""" jsonProbeKey jsonProbeValue

/// Cross-process contract for `GET /status`. Must match `UiTestServer.Server.statusBody`. Only the
/// key names live here: the counters they carry depend on whether another check already hit
/// `/slow` in this session, so a check asserts the names and never the numbers.
let slowSeenKey = "slowSeen"
let slowWaitingKey = "slowWaiting"

/// Cross-process contract for `GET /notfound`. Must match `UiTestServer.Server.notFoundBody`.
/// Distinct from the `/json` probe body, so a catch-all 404 cannot pass this check by accident.
let notFoundBody = "ui-test-server:notfound"

/// Cross-process contract for `POST /echo`. Must match `UiTestServer.Server.echoAckBody`. The
/// acknowledgement is what the *response* body carries, and it repeats none of what was posted.
/// Named in parts, and never reassembled into the whole body: the server owns that one line, and
/// a copy of it here would assert equality against a literal the server is free to re-space.
let echoAckKey = "echoed"

let echoAckValue = "ui-test-server"

/// Cross-process contract for the body `request-section.fsx` posts. Named in parts for the same
/// reason the `/json` probe is: a check reads the viewer's pretty-printed DOM, which whitespace
/// has already moved, so it matches the key and the value rather than the one-line body.
///
/// The value is deliberately absent from every other fixture and from the echo acknowledgement,
/// so finding it in the viewer can only mean the Request section rendered what was sent
/// (docs/spec/0012-request-as-sent.md, Seam 4).
let postedBodyKey = "posted"

let postedBodyValue = "request-section-fixture"
let postedBody = sprintf """{"%s":"%s"}""" postedBodyKey postedBodyValue

/// The request header the fixture sets, and which the Request section must render as a row. A
/// header the user wrote is the other half of "the request as sent" — the body alone would not
/// show that the headers travelled.
let postedHeaderName = "X-Fixture"

let postedHeaderValue = "request-section"

/// Substring the host writes into every runtime-error viewer update. Present on a dead-port Run,
/// and absent on a successful HTTP error response.
let runtimeErrorLabel = "Runtime error"

/// Substring the host writes into every compile-error viewer update. Present when a Setup or
/// block does not compile, and absent on a runtime error or a successful response.
let compileErrorLabel = "Compile error"

/// The text a pending Run abandons to when the companion exits. Taken from the shipped value, as
/// `LoopLensTests` and `CrossBlockRefusedRunTests` take theirs: this suite compiles `Refusals.fs`,
/// and a check that restated the words would pin the wording in a second place
/// (docs/spec/0003-lens-tells-the-truth.md, user story 11). `RefusalsTests` is where the wording
/// itself is pinned. What this asserts is that the host put *this* sentence in the viewer, and not
/// a different one.
let companionStoppedText = Refusals.companionStopped.Detail

let private extensionStatusPrefix = "FsHttp.Studio"

/// The status bar text the extension shows once the companion reports `ready`, exactly as
/// `Extension.setCompanionStatus` composes it. Asserted as rendered — it is the only surface
/// that publishes the companion's state to a reader. The retired `ready` word is gone; this is
/// the Ready + `ScriptPending` row of Decision 5.
let private extensionReadyStatus = "FsHttp.Studio: looking for requests…"
let private fixtureTabSuffix = "setup.fsx"
let private fixtureFolderName = "fixtures"
let private setupPhaseName = "Harness setup"
let private suitePhaseName = "Suite"

/// Retry spacing between polls. Deliberately not a parameter: the deadline is what a check tunes,
/// and a per-call interval would put a magic number in every check body.
let PollIntervalMs = 250

/// Which of the four proven-live conditions setup has actually confirmed. Each field is written
/// the moment its own tell holds, so a setup that fails partway leaves a record naming the tell
/// that never arrived rather than an all-or-nothing verdict.
type ProvenLive =
    { WorkbenchReady: bool
      ServerLive: bool
      FixtureOpen: bool
      ExtensionActive: bool
      CompanionRunning: bool
      CompanionReady: bool }

let private nothingProven =
    { WorkbenchReady = false
      ServerLive = false
      FixtureOpen = false
      ExtensionActive = false
      CompanionRunning = false
      CompanionReady = false }

let mutable private provenLive = nothingProven
let mutable private setupElapsedMs = 0.0
let mutable private timingSummaryEmitted = false
let mutable private timingSummaryReachedJobSummary = false

let mutable private suiteStartMs: float option = None
let mutable private checkStartMs: float option = None
let mutable private checkRows = ResizeArray<Timing.PhaseTiming>()

let provenLiveState () = provenLive

let setupElapsed () = setupElapsedMs

/// True once a timing table has been rendered and emitted. Setup emits one as its last act, so a
/// check can observe the summary path without performing the write it is verifying.
let timingSummaryWasEmitted () = timingSummaryEmitted

/// True once a timing table reached `GITHUB_STEP_SUMMARY` itself. False outside GitHub Actions,
/// where there is no job summary file to reach.
let timingSummaryWasWrittenToJobSummary () = timingSummaryReachedJobSummary

let isProvenLive () =
    provenLive.WorkbenchReady
    && provenLive.ServerLive
    && provenLive.FixtureOpen
    && provenLive.ExtensionActive
    && provenLive.CompanionRunning
    && provenLive.CompanionReady

/// What one poll saw. `Holds` ends the wait. `DoesNotHold` is a poll with nothing to say about the
/// state it found, which is most of them. `Observed` carries the poll's own account of that state,
/// for a condition whose opposite defects fail the same way — an exact count that reads too few and
/// one that reads too many produce the same timeout, and only the observation tells them apart.
type Poll =
    | Holds
    | DoesNotHold
    | Observed of observation: string

/// The account a poll gives of the state it found, where it gives one. A poll that holds has no
/// account to give a timeout, because a wait that holds never times out.
let private observationOf (result: Poll) =
    match result with
    | Observed observation -> Some observation
    | Holds
    | DoesNotHold -> None

/// The one polling loop. `eventually` is the plain-condition face of it, so a check chooses between
/// reporting and not reporting, and not between two waits.
///
/// A timeout quotes the result of the *last* poll before the deadline. It does not read the
/// workbench again after the deadline: a fresh read would report a state the wait never failed on,
/// and the surface it reads may have changed in the meantime.
let eventuallyObserved (timeoutMs: int) (subject: string) (poll: unit -> Async<Poll>) : Async<unit> =
    let timedOut (observation: string option) : unit =
        let waitingFor = sprintf "Timed out after %i ms waiting for %s" timeoutMs subject

        match observation with
        | Some observation -> Assert.fail (sprintf "%s. The last poll saw %s" waitingFor observation)
        | None -> Assert.fail waitingFor

    let rec loop (deadline: float) =
        async {
            let! result = poll ()

            match result with
            | Holds -> return ()
            | _ when Proc.now () >= deadline -> return timedOut (observationOf result)
            | _ ->
                do! Async.Sleep PollIntervalMs
                return! loop deadline
        }

    async {
        let deadline = Proc.now () + float timeoutMs
        return! loop deadline
    }

/// Polls `predicate` until it holds or `timeoutMs` elapses. The predicate is async because every
/// observation of the running editor returns a promise; wrap a synchronous condition in
/// `async { return ... }`. `subject` names what is being waited on, so a timeout reads as the
/// surface that never arrived rather than a bare elapsed time. A predicate that can name what it
/// saw instead goes through `eventuallyObserved`, which is the same loop.
let eventually (timeoutMs: int) (subject: string) (predicate: unit -> Async<bool>) : Async<unit> =
    eventuallyObserved timeoutMs subject (fun () ->
        async {
            let! holds = predicate ()
            return if holds then Holds else DoesNotHold
        })

let private failSetup (cause: string) =
    Assert.fail (sprintf "Harness setup failed: %s" cause)

/// The test server's two URLs, read from the sidecar. The three read outcomes are worded here
/// once: every caller needing the server's address goes through this rather than walking
/// `Proc.sidecarPath` and the `SidecarRead` cases again with its own parallel messages.
let private sidecarUrls () =
    let path =
        match Proc.sidecarPath () with
        | None -> failSetup "UI_TEST_SIDECAR is not set, so the harness cannot find the test server"
        | Some path -> path

    match Proc.readSidecar path with
    | Proc.SidecarMissing -> failSetup (sprintf "the sidecar file is missing at %s" path)
    | Proc.SidecarUnreadable reason -> failSetup (sprintf "the sidecar file at %s does not parse: %s" path reason)
    | Proc.SidecarLive(baseUrl, deadUrl) -> baseUrl, deadUrl

/// The test server's base URL. A check that reaches the server directly rather than through a
/// Run — the companion-death check's arrival wait and its hang release — resolves it here.
let baseUrl () = fst (sidecarUrls ())

let private verifySidecarLive () =
    let baseUrl, deadUrl = sidecarUrls ()
    let body = Proc.httpBody (baseUrl + "/json")

    if body <> jsonProbeBody then
        failSetup (sprintf "test server healthcheck failed at %s/json (got %A, expected %s)" baseUrl body jsonProbeBody)

    if not (Proc.curlConnectionRefused deadUrl) then
        failSetup (sprintf "dead port answered at %s — sidecar may be stale" deadUrl)

/// True when the Explorer shows the fixture folder as a workspace root. A workspace folder, not
/// only an open tab, is what makes the extension's activation and every later check start from
/// the same state.
let private tryFixtureFolderOpen () =
    async {
        try
            let bar = ExTester.ActivityBar.create ()
            let! control = bar.getViewControl "Explorer" |> Async.AwaitPromise

            if isNull (box control) then
                return false
            else
                let! sideBar = control.openView () |> Async.AwaitPromise
                let! sections = sideBar.getContent().getSections () |> Async.AwaitPromise
                let mutable found = false

                for section in sections do
                    if not found then
                        let! title = section.getTitle () |> Async.AwaitPromise

                        if title.ToLowerInvariant().Contains fixtureFolderName then
                            found <- true

                return found
        with _ ->
            return false
    }

let private tryFixtureTabOpen () =
    async {
        try
            let view = ExTester.EditorView.create ()
            let! titles = view.getOpenEditorTitles () |> Async.AwaitPromise
            return titles |> Array.exists (fun t -> t.Contains fixtureTabSuffix)
        with _ ->
            return false
    }

/// True when some status bar item's text satisfies `holds`. The read is wrapped because the status
/// bar is queried while the workbench is still settling, and a stale element throws rather than
/// returning nothing.
let private anyStatusItemSatisfies (holds: string -> bool) =
    async {
        try
            let bar = ExTester.StatusBar.create ()
            let! items = bar.getItems () |> Async.AwaitPromise
            let mutable found = false

            for item in items do
                if not found then
                    let! text = item.getText () |> Async.AwaitPromise

                    if holds text then
                        found <- true

            return found
        with _ ->
            return false
    }

let private tryExtensionActive () =
    anyStatusItemSatisfies (fun text -> text.Contains extensionStatusPrefix)

/// The companion answered its first request, rather than merely having been spawned. This is the
/// tell `tryCompanionRunning` cannot give: a pid exists the moment `Companion.start` spawns it,
/// while `ready` is written only once the process is serving. Until then `CodeLensProvider` holds
/// every lens back, so a check that opens a fixture sees no lens through no fault of the product.
///
/// The companion-death check reuses this after its window reload. The reading comes from the
/// status bar, so it holds only once the page carries a workbench, the extension has activated in
/// it, and the companion is serving. A reload satisfies those three at three different moments,
/// and a check that resumes on the first of them sends a workbench command to a page that is still
/// building one.
let tryCompanionReady () =
    anyStatusItemSatisfies (fun text -> text.Contains extensionReadyStatus)

/// Matches only the companion that this run's VSCode spawned, by anchoring on the extensions
/// directory the `.vsix` was installed into. A bare `Companion.dll` would also match the
/// developer's own editor, and the tell would pass without the suite having started anything.
let private companionPattern () =
    let extensionsDir = Proc.env "UI_TEST_EXTENSIONS_DIR" ""

    if extensionsDir = "" then
        failSetup
            "UI_TEST_EXTENSIONS_DIR is not set, so the companion tell cannot tell this run's companion from any other"

    extensionsDir + ".*Companion.dll"

/// Every companion process belonging to this run. Empty when none match. The companion-death
/// check kills and confirms these, then waits for a fresh pid after reload.
let companionPids () = Proc.pidsMatching (companionPattern ())

let private tryCompanionRunning () =
    async { return companionPids () |> Array.isEmpty |> not }

/// The hang route's waiting count from `GET /status`, or `None` when the body does not parse.
/// The companion-death check kills only after this rises above zero — a kill timed to the click
/// or to `Running…` can land during the first `#r "nuget:"` restore, before any request reaches
/// the server. Reads through `slowWaitingKey` rather than the literal, so a server-side rename
/// cannot leave this polling a key nothing writes.
let slowWaitingCount (serverBaseUrl: string) : int option =
    let body = Proc.httpBody (serverBaseUrl + "/status")

    try
        let parsed: obj = JS.JSON.parse body
        Some(unbox<int> (parsed?(slowWaitingKey): obj))
    with _ ->
        None

/// Advances the hang route's release generation. Teardown calls this so a stuck `/slow` does not
/// hold a thread-pool thread for the rest of the job.
let releaseHang (serverBaseUrl: string) : unit =
    Proc.httpStatus (serverBaseUrl + "/release") |> ignore

/// Runs `body`, then `teardown` whether the body held or failed. The body's failure is the
/// diagnostic one — it carries the `.fs` frame naming the assertion that went red — so a teardown
/// that fails on top of it is logged under `teardownSubject` rather than raised, and cannot
/// displace that frame. A teardown that fails on its own is the only failure there is.
let withTeardown (teardownSubject: string) (teardown: unit -> Async<unit>) (body: Async<unit>) : Async<unit> =
    async {
        let mutable bodyError: exn option = None

        try
            do! body
        with e ->
            bodyError <- Some e

        let mutable teardownError: exn option = None

        try
            do! teardown ()
        with e ->
            teardownError <- Some e

        match bodyError, teardownError with
        | Some bodyFailure, Some teardownFailure ->
            Proc.log (sprintf "%s also failed: %s" teardownSubject teardownFailure.Message)
            raise bodyFailure
        | Some bodyFailure, None -> raise bodyFailure
        | None, Some teardownFailure -> raise teardownFailure
        | None, None -> ()
    }

let private emitTimingTable (caption: string) (rows: Timing.PhaseTiming list) =
    let reachedJobSummary = Proc.appendJobSummary (Timing.renderTable caption rows)
    timingSummaryEmitted <- true
    timingSummaryReachedJobSummary <- timingSummaryReachedJobSummary || reachedJobSummary

let private setupRow () =
    { Timing.Name = setupPhaseName
      Timing.ElapsedMs = setupElapsedMs
      Timing.BudgetMs = float SetupBudgetMs }

let private suiteRow () =
    let elapsed =
        match suiteStartMs with
        | Some start -> Proc.now () - start
        | None -> 0.0

    { Timing.Name = suitePhaseName
      Timing.ElapsedMs = elapsed
      Timing.BudgetMs = float SuiteBudgetMs }

let private runSetup () =
    async {
        let setupStart = Proc.now ()
        provenLive <- nothingProven
        let browser = ExTester.VSBrowser.instance

        do! ExTester.waitForWorkbench browser 120_000. |> Async.AwaitPromise

        provenLive <-
            { provenLive with
                WorkbenchReady = true }

        verifySidecarLive ()
        provenLive <- { provenLive with ServerLive = true }

        do! eventually PostReloadRecoveryDeadlineMs "the fixture folder to open in the Explorer" tryFixtureFolderOpen
        do! eventually PostReloadRecoveryDeadlineMs "the harness fixture tab to open" tryFixtureTabOpen
        provenLive <- { provenLive with FixtureOpen = true }

        do! eventually PostReloadRecoveryDeadlineMs "FsHttp.Studio to activate in the status bar" tryExtensionActive

        provenLive <-
            { provenLive with
                ExtensionActive = true }

        do! eventually PostReloadRecoveryDeadlineMs "the companion process to exist" tryCompanionRunning

        provenLive <-
            { provenLive with
                CompanionRunning = true }

        do! eventually CompanionReadyDeadlineMs "the companion to report ready" tryCompanionReady

        provenLive <-
            { provenLive with
                CompanionReady = true }

        setupElapsedMs <- Proc.now () - setupStart
        emitTimingTable "Harness setup" [ setupRow () ]
        return ()
    }

let private assertBudget (timing: Timing.PhaseTiming) =
    if Timing.overBudget timing then
        Assert.fail (Timing.budgetFailure timing)

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
            | Some start -> Proc.now () - start
            | None -> 0.0

        let timing =
            { Timing.Name = sprintf "Check %s" title
              Timing.ElapsedMs = elapsed
              Timing.BudgetMs = float PerCheckBudgetMs }

        checkRows.Add timing
        checkStartMs <- None
        assertBudget timing
        return ()
    }

let private onAfter () =
    async {
        // Emit before asserting. A run that drifts past a budget is the run whose timings a reader
        // most needs, and `assertBudget` throws.
        let rows = (setupRow () :: List.ofSeq checkRows) @ [ suiteRow () ]
        emitTimingTable "UI test suite timings" rows

        assertBudget (setupRow ())
        assertBudget (suiteRow ())
        return ()
    }

let private onBeforeEach () =
    async {
        if suiteStartMs.IsNone then
            suiteStartMs <- Some(Proc.now ())

        checkStartMs <- Some(Proc.now ())
        return ()
    }

[<ImportMember(from = "./harness-hooks.mjs")>]
let private registerHarnessHooks
    (_setupFn: unit -> JS.Promise<unit>)
    (_beforeEachFn: unit -> JS.Promise<unit>)
    (_afterEachFn: obj -> JS.Promise<unit>)
    (_afterFn: unit -> JS.Promise<unit>)
    : unit =
    jsNative

/// Registers Mocha hooks through ExTester so failures capture screenshots. Call once from Main
/// before `Mocha.runTests`.
let registerHooks () : unit =
    let setup: unit -> JS.Promise<unit> = fun () -> runSetup () |> Async.StartAsPromise

    let beforeEachHook: unit -> JS.Promise<unit> =
        fun () -> onBeforeEach () |> Async.StartAsPromise

    let afterEachHook (test: obj) : JS.Promise<unit> =
        let wrapped = if isNull (box test) then None else Some test

        onAfterEach wrapped |> Async.StartAsPromise

    let afterHook: unit -> JS.Promise<unit> =
        fun () -> onAfter () |> Async.StartAsPromise

    registerHarnessHooks setup beforeEachHook afterEachHook afterHook
