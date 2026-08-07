// PROTOTYPE — ticket 014: the companion-stops section, end to end.
//
//   1. click the lens on a block that hangs        -> viewer shows Running…
//   2. SIGKILL the companion mid-Run               -> viewer shows the stopped message
//   3. observe what the lens does after the death  -> reported, not assumed
//   4. reload the window                           -> does the Selenium session survive?
//   5. click a lens again                          -> a Run succeeds
//
// Every step is stamped, so RESULTS.md can carry real wall-clock numbers instead of the
// budgets 010 and 012 guessed at.
module ReloadRecover

open Fable.Core
open Fable.Mocha
open ExTester

let private lensTitle = "▶ Run request"
let private probeMarker = "reload-and-recover-014"
let private stoppedMessage = "The FsHttp.Studio companion stopped."
let private companionPattern = "Companion.dll"

let private fixturePath = Proc.env "FIXTURE" ""
let private sidecarPath = Proc.env "SIDECAR" ""
let private negativeControl = (Proc.env "NEGATIVE" "") = "1"
/// The post-death lens observation. Opt-in — see the comment at its call site.
let private lensProbe = (Proc.env "LENS_PROBE" "") = "1"

// ---------------------------------------------------------------- timing

let private marks = ResizeArray<string * float>()
let mutable private t0 = 0.0

let private mark (label: string) =
    let at = Proc.now () - t0
    marks.Add(label, at)
    Proc.log (sprintf "    [%7.1fs] %s" (at / 1000.0) label)

let private report () =
    Proc.log ""
    Proc.log "    ---- 014 wall clock (seconds from workbench) ----"

    let mutable previous = 0.0

    for (label, at) in marks do
        Proc.log (sprintf "    %-38s %7.1f  (+%.1f)" label (at / 1000.0) ((at - previous) / 1000.0))
        previous <- at

    Proc.log "    ------------------------------------------------"

// ---------------------------------------------------------------- probes

let private tryGetLensByIndex (index: int) =
    async {
        try
            let editor = TextEditor.create ()
            let! lens = TextEditor.getCodeLensByIndex editor index |> Async.AwaitPromise
            let! text = lens.getText () |> Async.AwaitPromise

            return (if text.Contains(lensTitle) then Some lens else None)
        with _ ->
            return None
    }

let private tryGetRunnableLens () =
    async {
        try
            let editor = TextEditor.create ()
            let! lens = TextEditor.getCodeLensByTitle editor lensTitle |> Async.AwaitPromise
            let! text = lens.getText () |> Async.AwaitPromise

            return (if text.Contains(lensTitle) then Some lens else None)
        with _ ->
            return None
    }

/// Find and click in one retryable step. A CodeLens handle found in one poll can be stale by
/// the next statement — Monaco re-renders lenses whenever the provider fires — and a bare
/// `find` then `click` reddened two CI attempts with StaleElementReferenceError. 005's single
/// warm miss was the same error. Every lens click in the suite goes through here.
let private tryClick (find: unit -> Async<CodeLens option>) =
    async {
        try
            let! found = find ()

            match found with
            | Some lens ->
                do! lens.click () |> Async.AwaitPromise
                return Some()
            | None -> return None
        with _ ->
            return None
    }

/// Whole-panel text, read through the webview frame. Every assertion in this slice is a
/// substring of it: `Running…`, the stopped sentence, or the rendered 200.
let private tryReadViewer () =
    async {
        let view = WebView.create ()
        let mutable switched = false

        try
            do! switchToFrameTimed view 5000. |> Async.AwaitPromise
            switched <- true
            let! root = view.findWebElement (By.css "#root") |> Async.AwaitPromise
            let! text = root.getText () |> Async.AwaitPromise
            do! view.switchBack () |> Async.AwaitPromise
            switched <- false
            return Some text
        with _ ->
            if switched then
                try
                    do! view.switchBack () |> Async.AwaitPromise
                with _ ->
                    ()

            return None
    }

let private viewerShowing (needle: string) =
    async {
        let! text = tryReadViewer ()

        match text with
        | Some t when t.Contains(needle) -> return Some t
        | _ -> return None
    }

// ---------------------------------------------------------------- the slice

let private slice =
    async {
        Assert.isTrue (fixturePath <> "") "FIXTURE env var must point at the .fsx"
        Assert.isTrue (sidecarPath <> "") "SIDECAR env var must point at sidecar.json"
        let baseUrl = Proc.readSidecarField sidecarPath "baseUrl"

        let browser = VSBrowser.instance
        do! waitForWorkbenchTimed browser 120_000. |> Async.AwaitPromise
        t0 <- Proc.now ()
        mark "workbench up"

        // The companion spawns on activation; give it a moment before the first locate.
        do! sleep 3_000 |> Async.AwaitPromise

        let! _ = eventually 90_000 1_000 tryGetRunnableLens
        mark "first lens visible"

        let companionsBefore = Proc.pidsMatching companionPattern
        Proc.log (sprintf "    companion pids: %A" companionsBefore)
        Assert.isTrue (companionsBefore.Length > 0) "the companion process is findable by `pgrep -f Companion.dll`"

        // 1. Run the hanging block.
        let! _ = eventually 60_000 1_000 (fun () -> tryClick tryGetRunnableLens)
        mark "slow lens clicked"

        let! _ = eventually 60_000 500 (fun () -> viewerShowing "Running")
        mark "viewer shows Running…"

        // The arrival tell. Without it the kill lands while FSI is still restoring
        // `#r "nuget: FsHttp"`, which is a *pending* Run, not a Run in flight. The first
        // local run of this slice killed at 0.1 s and the server never saw `/slow` at all.
        let! _ =
            eventually 180_000 500 (fun () ->
                async {
                    let body = Proc.httpBody (baseUrl + "/status")
                    return (if body.Contains("\"slowWaiting\":0") then None else Some body)
                })

        mark "the request is in flight at the server"

        // 2. Kill it mid-Run.
        companionsBefore |> Array.iter Proc.kill
        mark "companion SIGKILLed"

        let! stoppedText = eventually 60_000 500 (fun () -> viewerShowing stoppedMessage)
        mark "viewer shows the stopped message"
        Assert.stringContains stoppedText stoppedMessage "the companion-stopped sentence in the viewer DOM"

        let stillAlive = companionsBefore |> Array.filter Proc.isAlive
        Assert.isTrue (stillAlive.Length = 0) "no companion process survives the kill"

        // 3. What happens to the lens after the death? `LENS_PROBE=1` only, because the
        //    answer is "nothing you can assert": across 18 CI observations the lens usually
        //    stayed clickable past 20 s and re-reported in ~130-280 ms, but once it was gone
        //    within 10 s, and once a click on it raised StaleElementReferenceError and
        //    reddened the job. Neither presence nor absence is a stable surface, so the
        //    proposed check leaves the post-death lens alone and this block stays opt-in.
        if lensProbe then
            let! survivingLens = eventuallyOrNone 10_000 1_000 tryGetRunnableLens

            match survivingLens with
            | None -> Proc.log "    OBSERVED: no runnable lens after the companion's death"
            | Some l ->
                Proc.log "    OBSERVED: the lens is still clickable after the companion's death"
                let secondClickAt = Proc.now ()
                do! l.click () |> Async.AwaitPromise
                let! _ = eventually 30_000 250 (fun () -> viewerShowing stoppedMessage)
                Proc.log (sprintf "    OBSERVED: second click re-reported in %.0f ms" (Proc.now () - secondClickAt))

            let deathAt = Proc.now ()

            let! gone =
                eventuallyOrNone 20_000 250 (fun () ->
                    async {
                        let! present = tryGetRunnableLens ()
                        return (if present.IsNone then Some(Proc.now () - deathAt) else None)
                    })

            match gone with
            | Some ms -> Proc.log (sprintf "    OBSERVED: the runnable lens disappeared %.0f ms after the death probe" ms)
            | None -> Proc.log "    OBSERVED: the runnable lens was still there 20 s after the death"

            mark "post-death lens observed"

        // Release the hanging request so the test server is not left with a stuck handler.
        let releaseStatus = Proc.httpStatus (baseUrl + "/release")
        Proc.log (sprintf "    /release -> %s" releaseStatus)

        // 4. Reload the window. This is the step the ticket exists for.
        let workbench = Workbench.create ()
        do! workbench.executeCommand "Developer: Reload Window" |> Async.AwaitPromise
        mark "reload command issued"

        do! waitForWorkbenchTimed browser 120_000. |> Async.AwaitPromise

        // `waitForWorkbench` returns at once when the *old* workbench DOM is still up, so it
        // is not a reload tell on its own. A companion pid that is neither absent nor one of
        // the killed ones is: only a reloaded window spawns a new one.
        let! freshPids =
            eventually 120_000 500 (fun () ->
                async {
                    let pids =
                        Proc.pidsMatching companionPattern
                        |> Array.filter (fun p -> not (Array.contains p companionsBefore))

                    return (if pids.Length > 0 then Some pids else None)
                })

        Proc.log (sprintf "    companion pids after reload: %A" freshPids)
        mark "companion respawned after reload"

        do! openResource browser fixturePath |> Async.AwaitPromise
        mark "fixture reopened"

        // 5. A Run succeeds again — block index 1 is the fast JSON route.
        let! _ = eventually 120_000 1_000 (fun () -> tryGetLensByIndex 1)
        mark "lens visible after reload"

        let! _ = eventually 60_000 1_000 (fun () -> tryClick (fun () -> tryGetLensByIndex 1))
        let! recovered = eventually 120_000 1_000 (fun () -> viewerShowing "200")
        mark "viewer renders 200 after reload"

        let expectedMarker = if negativeControl then "no-such-marker-014" else probeMarker
        Assert.stringContains recovered expectedMarker "the JSON probe marker in the viewer DOM after reload"

        report ()
    }

let tests =
    testList
        "014 companion death, reload, recover"
        [ testCaseAsync "the viewer reports the stopped companion, and a reload recovers" slice ]

Mocha.runTests tests |> ignore
