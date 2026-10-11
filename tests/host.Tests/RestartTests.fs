module Extension.Tests.RestartTests

open Expecto
open Protocol

let private block =
    { StartLine = 3
      StartCol = 0
      EndLine = 5
      EndCol = 1
      Refusal = None }

[<Tests>]
let tests =
    testList
        "Restart"
        [ test "a remembered Block gets the starting title while the Companion is Starting" {
              Expect.equal
                  (notReadyLensTitles Starting (Some [ block; block ]))
                  [ Refusals.companionStartingLensTitle; Refusals.companionStartingLensTitle ]
                  "each remembered Block keeps its lens"
          }

          test "a first start remembers no Block, so it paints no lens" {
              Expect.isEmpty (notReadyLensTitles Starting None) "nothing was located yet"
          }

          test "the stopped title shows in the Stopped state and in SdkNotFound only" {
              let remembered = Some [ block ]
              let stopped = [ Refusals.companionStoppedLensTitle ]
              Expect.equal (notReadyLensTitles Stopped remembered) stopped "Stopped"
              Expect.equal (notReadyLensTitles SdkNotFound remembered) stopped "SdkNotFound"
              Expect.isEmpty (notReadyLensTitles Ready remembered) "Ready locates afresh"
          }

          test "a command during the stop gives the restarting notice and starts no second stop" {
              let afterFirst, first = Restart.onCommand Restart.initial
              let afterSecond, second = Restart.onCommand afterFirst
              Expect.equal first Restart.BeginRestart "the first command begins a Restart"
              Expect.equal second Restart.NotifyRestarting "the second command only gives the notice"
              Expect.equal afterSecond afterFirst "the second command changes nothing"
          }

          test "a command after the new Companion spawns restarts again" {
              let stopping, _ = Restart.onCommand Restart.initial
              let spawned = Restart.onStopped stopping
              let _, decision = Restart.onCommand spawned
              Expect.equal decision Restart.BeginRestart "a start that hangs can be restarted"
          }

          test "an event of the old Companion after the command changes no state" {
              let oldEpoch = Restart.initial.Epoch
              let stopping, _ = Restart.onCommand Restart.initial
              Expect.isFalse (Restart.accepts stopping oldEpoch) "a late exit of the old Companion is stale"
              Expect.isTrue (Restart.accepts stopping stopping.Epoch) "the new start is current"
          } ]
