// Runs after the companion-death check, which needs the viewer focus that its own Run gives.
module RestartTests

open Fable.Mocha

let private blockCount = 2
let private hangFixtureFileName = "restart-hang.fsx"
let private workerFixtureFileName = "restart-worker.fsx"
let private versionlessFixtureFileName = "core-path.fsx"
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

let private waitForANewReadyCompanion (before: int[]) =
    async {
        do!
            Harness.eventually Harness.CompanionStartDeadlineMs "a new companion process" (fun () ->
                Harness.tryNewCompanion before)

        do!
            Harness.eventually
                Harness.CompanionStartDeadlineMs
                "the Status line text reporting the companion ready"
                Harness.tryCompanionReady
    }

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

let private releaseTheHang (serverBaseUrl: string) () =
    async { Harness.releaseHang serverBaseUrl }

let private restartUnderAHang =
    async {
        let serverBaseUrl = Harness.baseUrl ()

        let body =
            async {
                let! before = hangThenRestart serverBaseUrl hangFixtureFileName Harness.companionPids

                do!
                    Harness.eventually
                        Harness.ViewerUpdateDeadlineMs
                        "the Run-ended sentence in the viewer, with Running… gone"
                        tryRunEndedRendered

                do! waitForANewReadyCompanion before

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

        return! Harness.withTeardown "the hang release" (releaseTheHang serverBaseUrl) body
    }

/// Closes the viewer first, so that the probe response cannot be a stale render of an earlier Run.
let private loadFsHttpWithNoVersion () =
    async {
        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "the response viewer to be closed"
                ExTester.tryCloseResponseViewer

        do! Checks.openFixtureAsSoleTab versionlessFixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two core-path blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount versionlessFixtureFileName)

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the first core-path block's Run request lens"
                (fun () -> ExTester.tryClickCodeLensByTitle Checks.lensTitle)

        do!
            Harness.eventually
                Harness.ViewerUpdateDeadlineMs
                "status 200, the /json URL, and the probe body in the response viewer"
                (fun () -> Checks.tryJsonProbeResponseRendered recoveryUrlPath)
    }

let private workerUnderAHang =
    async {
        let serverBaseUrl = Harness.baseUrl ()

        let body =
            async {
                do! loadFsHttpWithNoVersion ()

                let! _ = hangThenRestart serverBaseUrl workerFixtureFileName Harness.workerPids

                do!
                    Harness.eventually
                        Harness.CompanionStartDeadlineMs
                        "the Status line text reporting the companion ready"
                        Harness.tryCompanionReady
            }

        return! Harness.withTeardown "the hang release" (releaseTheHang serverBaseUrl) body
    }

let private killCompanionAndOpenFixture () =
    async {
        do! Checks.openFixtureAsSoleTab hangFixtureFileName

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "a Run request lens above each of the two blocks"
                (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount hangFixtureFileName)

        let killed = Harness.companionPids ()

        if Array.isEmpty killed then
            Assert.fail "no companion process matched before the kill"

        killed |> Array.iter Proc.kill

        do!
            Harness.eventuallyObserved
                Harness.LensAppearanceDeadlineMs
                "the stopped companion title on the lens above each of the two blocks"
                (fun () -> Checks.tryOnlyLensTitle blockCount Refusals.companionStoppedLensTitle)

        return killed
    }

let private restartThenRun (before: int[]) =
    async {
        do! waitForANewReadyCompanion before

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

let private restartFromTheStoppedState =
    async {
        let! killed = killCompanionAndOpenFixture ()

        do!
            Harness.eventually Harness.LensAppearanceDeadlineMs "a click on a stopped lens" (fun () ->
                ExTester.tryClickCodeLensByIndex 0)

        do!
            Harness.eventually Harness.ToastDeadlineMs "a click on Restart companion in the stopped toast" (fun () ->
                ExTester.tryClickWarningNotificationButton Harness.companionStoppedText Refusals.restartButtonLabel)

        do! restartThenRun killed
    }

let private clickOnTheStoppedStatusLineText =
    async {
        let! killed = killCompanionAndOpenFixture ()

        do!
            Harness.eventually
                Harness.LensAppearanceDeadlineMs
                "a click on the Status line text"
                ExTester.tryClickFsHttpStatus

        do! waitForANewReadyCompanion killed
    }

/// True once `settleUntil` passes with the companion processes of `before` unchanged.
let private tryNoRestartThroughSettle (before: int[]) (settleUntil: float) =
    async {
        let pids = Harness.companionPids ()

        if pids <> before then
            return Harness.Observed(sprintf "companion processes %A in place of %A" pids before)
        elif Proc.now () < settleUntil then
            return Harness.DoesNotHold
        else
            return Harness.Holds
    }

let private aChangeToTheDotnetPath =
    async {
        let before = Harness.companionPids ()

        let body =
            async {
                do!
                    Harness.eventually
                        Harness.LensAppearanceDeadlineMs
                        "the Settings editor to take the new dotnet path"
                        (fun () -> ExTester.trySetDotnetPath "dotnet")

                do!
                    Harness.eventually Harness.ToastDeadlineMs "the dotnet path INFO toast" (fun () ->
                        ExTester.tryInfoNotification Refusals.dotnetPathChanged)

                let settleUntil = Proc.now () + Harness.RestartAbsenceSettleMs

                do!
                    Harness.eventuallyObserved
                        Harness.LensAppearanceDeadlineMs
                        "the same companion processes once the settle window has passed"
                        (fun () -> tryNoRestartThroughSettle before settleUntil)

                do!
                    Harness.eventually Harness.ToastDeadlineMs "a dismiss of the dotnet path INFO toast" (fun () ->
                        ExTester.tryDismissInfoNotification Refusals.dotnetPathChanged)
            }

        // The reset also changes the dotnet path, so it gives its own INFO toast.
        let clearThePath () =
            async {
                do!
                    Harness.eventually
                        Harness.LensAppearanceDeadlineMs
                        "the Settings editor to clear the dotnet path"
                        (fun () -> ExTester.trySetDotnetPath "")

                do!
                    Harness.eventually
                        Harness.ToastDeadlineMs
                        "a dismiss of the INFO toast that the reset gives"
                        (fun () -> ExTester.tryDismissInfoNotification Refusals.dotnetPathChanged)
            }

        return! Harness.withTeardown "the dotnet path reset" clearThePath body
    }

let tests =
    testList
        "restart companion"
        [ testCaseAsync "Restart under a hang" restartUnderAHang
          testCaseAsync "A worker under a hang" workerUnderAHang
          testCaseAsync "Restart from the stopped state" restartFromTheStoppedState
          testCaseAsync "A click on the stopped Status line text" clickOnTheStoppedStatusLineText
          testCaseAsync "A change to the dotnet path" aChangeToTheDotnetPath ]
