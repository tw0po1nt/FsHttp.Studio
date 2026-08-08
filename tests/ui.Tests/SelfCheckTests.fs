module SelfCheckTests

open Fable.Mocha
open Harness

let tests =
    testList
        "setup self-check"
        [ testCase "proven-live workbench and timing summary" (fun () ->
              Assert.isTrue (Harness.isProvenLive ()) "the before hook reached a proven-live workbench"

              match Harness.provenLiveState () with
              | Some state ->
                  Assert.isTrue state.WorkbenchReady "waitForWorkbench returned"
                  Assert.isTrue state.ServerLive "the test server healthcheck passed and the sidecar parsed"
                  Assert.isTrue state.FixtureOpen "the fixture folder is open"
                  Assert.isTrue state.ExtensionActive "the extension is active"
                  Assert.isTrue state.CompanionRunning "a companion process exists"
              | None -> Assert.fail "proven-live state was not recorded during setup"

              Harness.writeTimingSummary ()
              Assert.isTrue (Harness.timingSummaryWasPrinted ()) "the timing table was written to the job summary") ]
