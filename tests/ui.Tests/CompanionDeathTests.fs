// Companion death is visible and recoverable: hang a Run, wait for the request to arrive at the
// server, kill every companion process, assert the viewer leaves `Running…` for the stopped
// message, reload the window, and assert a Run succeeds again. Spec 0011's check half, as one
// check. Runs last — it kills the companion and reloads the window, so no later check can inherit
// the session. Teardown releases the hang even when the body fails partway.
module CompanionDeathTests

open Fable.Mocha

let private blockCount = 2
let private fixtureFileName = "companion-death.fsx"
let private runInProgressLabel = "Running…"
/// Path segment of the recovery block's URL, as it appears in the status line.
let private recoveryUrlPath = "/json"
/// Zero-based index of the recovery block's lens. Both lenses share `Checks.lensTitle`.
let private recoveryLensIndex = 1

let private tryClickHangLens () =
    ExTester.tryClickCodeLensByTitle Checks.lensTitle

let private tryClickRecoveryLens () =
    ExTester.tryClickCodeLensByIndex recoveryLensIndex

let private tryRunningInViewer () =
    Checks.viewerSatisfies (fun dom -> dom.RunInProgressLabel.Contains runInProgressLabel)

/// The hang route has at least one request waiting. Parsed from `/status`, not matched as a
/// substring — a count of ten would also contain the characters of a zero count.
let private tryRequestWaitingAtServer (baseUrl: string) =
    async {
        match Harness.trySlowWaiting baseUrl with
        | Some waiting when waiting > 0 -> return true
        | _ -> return false
    }

let private tryKilledCompanionsGone (killed: int[]) =
    async { return killed |> Array.forall (fun pid -> not (Proc.isAlive pid)) }

/// The shipped stopped message is present, and `Running…` is gone. Exact wording — a deliberate
/// change to `Protocol.companionStoppedText` must redden this check.
let private tryStoppedMessageRendered () =
    Checks.viewerSatisfies (fun dom ->
        dom.RootText.Contains Harness.companionStoppedText
        && not (dom.RunInProgressLabel.Contains runInProgressLabel))

/// A companion process that is neither absent nor one of the killed ones. The workbench-ready
/// wait returns while the pre-reload DOM is still up; only a fresh pid proves the reload happened.
let private tryFreshCompanion (killed: int[]) =
    async {
        let pids = Harness.companionPids ()
        return pids |> Array.exists (fun pid -> not (Array.contains pid killed))
    }

/// Same status-code-and-body channels the core-path check uses for a successful `/json` Run.
let private tryRecoveryResponseRendered () =
    Checks.viewerSatisfies (fun dom ->
        dom.StatusCodeText.Contains "200"
        && dom.UrlText.Contains recoveryUrlPath
        && dom.JsonBodyText.Contains Harness.jsonProbeKey
        && dom.JsonBodyText.Contains Harness.jsonProbeValue)

let private baseUrlFromSidecar () =
    match Proc.sidecarPath () with
    | None -> Assert.fail "UI_TEST_SIDECAR is not set, so the check cannot reach the test server"
    | Some path ->
        match Proc.readSidecar path with
        | Proc.SidecarLive(baseUrl, _) -> baseUrl
        | Proc.SidecarMissing -> Assert.fail (sprintf "the sidecar file is missing at %s" path)
        | Proc.SidecarUnreadable reason -> Assert.fail (sprintf "the sidecar file at %s does not parse: %s" path reason)

/// Inherits the warm companion and open viewer from the compile-error check, takes over the
/// fixture column, and leaves the session after a reload — no later check should inherit it.
let private companionDeathIsVisibleAndRecoverable =
    async {
        let baseUrl = baseUrlFromSidecar ()
        let mutable bodyError: exn option = None

        try
            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "the companion-death fixture tab to open as the fixture column's only tab"
                    (fun () -> ExTester.tryOpenAsSoleTabInFixtureColumn fixtureFileName)

            do!
                Harness.eventuallyObserved
                    Harness.LensAppearanceDeadlineMs
                    "a Run request lens above each of the two blocks"
                    (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount)

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "a click on the hang block's Run request lens"
                    tryClickHangLens

            do!
                Harness.eventually
                    Harness.ViewerUpdateDeadlineMs
                    "the response viewer to open beside the editor"
                    ExTester.tryViewerBesideEditor

            do! Harness.eventually Harness.ViewerUpdateDeadlineMs "Running… in the response viewer" tryRunningInViewer

            do!
                Harness.eventually
                    Harness.ViewerUpdateDeadlineMs
                    "the hang request to be waiting at the test server"
                    (fun () -> tryRequestWaitingAtServer baseUrl)

            let killed = Harness.companionPids ()

            if Array.isEmpty killed then
                Assert.fail "no companion process matched before the kill"

            killed |> Array.iter Proc.kill

            do!
                Harness.eventually Harness.ViewerUpdateDeadlineMs "every killed companion process to be gone" (fun () ->
                    tryKilledCompanionsGone killed)

            do!
                Harness.eventually
                    Harness.ViewerUpdateDeadlineMs
                    "the stopped companion message in the viewer, with Running… gone"
                    tryStoppedMessageRendered

            do! ExTester.reloadWindow ()

            do!
                Harness.eventually
                    Harness.PostReloadRecoveryDeadlineMs
                    "a fresh companion process after the reload"
                    (fun () -> tryFreshCompanion killed)

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "the companion-death fixture tab to reopen as the fixture column's only tab"
                    (fun () -> ExTester.tryOpenAsSoleTabInFixtureColumn fixtureFileName)

            do!
                Harness.eventuallyObserved
                    Harness.LensAppearanceDeadlineMs
                    "a Run request lens above each of the two blocks after the reload"
                    (fun () -> Checks.tryRunRequestLensAboveEachBlock blockCount)

            do!
                Harness.eventually
                    Harness.LensAppearanceDeadlineMs
                    "a click on the recovery block's Run request lens"
                    tryClickRecoveryLens

            do!
                Harness.eventually
                    Harness.ViewerUpdateDeadlineMs
                    "status 200, the recovery URL, and the probe body in the response viewer"
                    tryRecoveryResponseRendered
        with e ->
            bodyError <- Some e

        let mutable releaseError: exn option = None

        try
            Harness.releaseHang baseUrl
        with e ->
            releaseError <- Some e

        match bodyError, releaseError with
        | Some body, Some release ->
            Proc.log (sprintf "the hang release also failed: %s" release.Message)
            raise body
        | Some body, None -> raise body
        | None, Some release -> raise release
        | None, None -> ()
    }

let tests =
    testList
        "companion death is visible and recoverable"
        [ testCaseAsync
              "viewer leaves Running… for the stopped message, and a reload recovers"
              companionDeathIsVisibleAndRecoverable ]
