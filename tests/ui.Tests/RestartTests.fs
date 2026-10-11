// A Restart ends a hung Run, stops each process that the companion started, and leaves a
// companion that runs again. Runs after the companion-death check, because that check needs the
// viewer focus that its own Run gives, and it leaves a live companion after its reload.
module RestartTests

open Fable.Mocha

let private blockCount = 2
let private hangFixtureFileName = "restart-hang.fsx"
let private workerFixtureFileName = "restart-worker.fsx"
let private runInProgressLabel = "Running…"
let private recoveryUrlPath = "/json"
let private recoveryLensIndex = 1

let private tryRequestWaitingAtServer (serverBaseUrl: string) =
    async {
        match Harness.slowWaitingCount serverBaseUrl with
        | Some waiting when waiting > 0 -> return true
        | _ -> return false
    }

let private tryGone (pids: int[]) =
    async { return pids |> Array.forall (fun pid -> not (Proc.isAlive pid)) }

let private tryRunEndedRendered () =
    Checks.viewerSatisfies (fun dom ->
        dom.RootText.Contains Refusals.restartEndedRun
        && not (dom.RunInProgressLabel.Contains runInProgressLabel))

let private tryRunningInViewer () =
    Checks.viewerSatisfies (fun dom -> dom.RunInProgressLabel.Contains runInProgressLabel)

let private hangThenRestart (serverBaseUrl: string) (fixtureFileName: string) (pidsToCheck: unit -> int[]) =
    async {
        do! Checks.openFixtureAsSoleTab fixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount fixtureFileName)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the hang block's Run request lens"
                (fun () -> ExTester.tryClickCodeLensByTitle Checks.lensTitle)

        do! Harness.eventually Harness.ViewerUpdateDeadlineMs "Running… in the response viewer" tryRunningInViewer

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the hang request to be waiting at the test server"
                (fun () -> tryRequestWaitingAtServer serverBaseUrl)

        let before = pidsToCheck ()

        if Array.isEmpty before then
            Assert.fail "no process matched before the Restart"

        do! ExTester.focusFixtureEditor ()

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "Restart companion from the command palette"
                ExTester.tryRestartFromPalette

        do!
            Harness.eventually Harness.ViewerUpdateDeadlineMs "every process of the old companion to be gone" (fun () ->
                tryGone before)

        return before
    }

let private restartUnderAHang =
    async {
        let serverBaseUrl = Harness.baseUrl ()

        let releaseTheHang () =
            async { Harness.releaseHang serverBaseUrl }

        let body =
            async {
                let! before = hangThenRestart serverBaseUrl hangFixtureFileName Harness.companionPids

                do!
                    Harness.eventually
                        Harness.ViewerUpdateDeadlineMs
                        "the Run-ended sentence in the viewer, with Running… gone"
                        tryRunEndedRendered

                do!
                    Harness.eventually Harness.PostReloadRecoveryDeadlineMs "a new companion process" (fun () ->
                        async {
                            return
                                Harness.companionPids ()
                                |> Array.exists (fun pid -> not (Array.contains pid before))
                        })

                do!
                    Harness.eventually
                        Harness.PostReloadRecoveryDeadlineMs
                        "the status bar reporting the companion ready"
                        Harness.tryCompanionReady

                do!
                    Harness.eventuallyObserved
                        Harness.LensAppearanceDeadlineMs
                        "a Run request lens above each of the two blocks after the Restart"
                        (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount hangFixtureFileName)

                do!
                    Harness.eventually
                        Harness.LensAppearanceDeadlineMs
                        "a click on the recovery block's Run request lens"
                        (fun () -> ExTester.tryClickCodeLensByIndex recoveryLensIndex)

                do!
                    Harness.eventually
                        Harness.ViewerUpdateDeadlineMs
                        "status 200, the recovery URL, and the probe body in the response viewer"
                        (fun () -> Checks.tryJsonProbeResponseRendered recoveryUrlPath)
            }

        return! Harness.withTeardown "the hang release" releaseTheHang body
    }

let private workerUnderAHang =
    async {
        let serverBaseUrl = Harness.baseUrl ()

        let releaseTheHang () =
            async { Harness.releaseHang serverBaseUrl }

        let body =
            async {
                let! _ = hangThenRestart serverBaseUrl workerFixtureFileName Harness.workerPids

                do!
                    Harness.eventually
                        Harness.PostReloadRecoveryDeadlineMs
                        "the status bar reporting the companion ready"
                        Harness.tryCompanionReady
            }

        return! Harness.withTeardown "the hang release" releaseTheHang body
    }

let tests =
    testList
        "restart companion"
        [ testCaseAsync "Restart under a hang" restartUnderAHang
          testCaseAsync "A worker under a hang" workerUnderAHang ]
