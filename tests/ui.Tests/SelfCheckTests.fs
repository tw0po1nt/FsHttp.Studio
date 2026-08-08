module SelfCheckTests

open Fable.Mocha

let tests =
    testList
        "setup self-check"
        [ testCase "proven-live workbench and timing summary" (fun () ->
              let state = Harness.provenLiveState ()

              Assert.isTrue state.WorkbenchReady "waitForWorkbench returned"
              Assert.isTrue state.ServerLive "the test server healthcheck passed and the sidecar parsed"
              Assert.isTrue state.FixtureOpen "the fixture folder is open"
              Assert.isTrue state.ExtensionActive "the extension is active"
              Assert.isTrue state.CompanionRunning "a companion process exists"
              Assert.isTrue (Harness.isProvenLive ()) "the before hook reached a proven-live workbench"

              // Setup emits the table as its last act, so this observes a write that already
              // happened rather than performing the one it verifies.
              Assert.isTrue (Harness.timingSummaryWasEmitted ()) "setup emitted the timing table"

              if Proc.env "GITHUB_STEP_SUMMARY" "" <> "" then
                  Assert.isTrue
                      (Harness.timingSummaryWasWrittenToJobSummary ())
                      "the timing table reached the GitHub Actions job summary") ]
