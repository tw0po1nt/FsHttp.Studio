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
        [ test "a remembered Block gets the starting title while the Companion is Starting, and a click does nothing" {
              let starting = (block, Refusals.companionStartingLensTitle, DoesNothing)

              Expect.equal
                  (notReadyLenses Starting (Some [ block; block ]))
                  [ starting; starting ]
                  "each remembered Block keeps its lens"
          }

          test "a first start remembers no Block, so it paints no lens" {
              Expect.isEmpty (notReadyLenses Starting None) "nothing was located yet"
          }

          test "the stopped title shows in the Stopped state and in SdkNotFound only, and a click explains the stop" {
              let remembered = Some [ block ]
              let stopped = [ block, Refusals.companionStoppedLensTitle, ExplainsStop ]
              Expect.equal (notReadyLenses Stopped remembered) stopped "Stopped"
              Expect.equal (notReadyLenses SdkNotFound remembered) stopped "SdkNotFound"
              Expect.isEmpty (notReadyLenses Ready remembered) "Ready locates afresh"
          }

          test "a click on the Status line text restarts in the Stopped state and in SdkNotFound only" {
              Expect.equal (statusClick Stopped) RestartsCompanion "Stopped"
              Expect.equal (statusClick SdkNotFound) RestartsCompanion "SdkNotFound"
              Expect.equal (statusClick Starting) StatusDoesNothing "a click never ends a start"
              Expect.equal (statusClick Ready) StatusDoesNothing "a click never ends a Run"
          }

          test "a command during the stop gives the restarting notice and starts no second stop" {
              let afterFirst, first = Restart.onCommand Restart.initial
              let afterSecond, second = Restart.onCommand afterFirst
              Expect.equal first Restart.BeginRestart "the first command begins a Restart"
              Expect.equal second Restart.NotifyRestarting "the second command only gives the notice"
              Expect.equal afterSecond afterFirst "the second command changes nothing"
          }

          test "a command after the old Companion stops restarts again" {
              let stopping, _ = Restart.onCommand Restart.initial
              let stopped = Restart.onStopped stopping
              let _, decision = Restart.onCommand stopped
              Expect.equal decision Restart.BeginRestart "a start that hangs can be restarted"
          }

          test "an event of the old Companion after the command changes no state" {
              let mutable session = Restart.initial
              let received = ResizeArray<State>()
              let onOldState = Restart.guard (fun () -> session) session.Epoch received.Add
              session <- fst (Restart.onCommand session)
              onOldState Stopped
              Expect.isEmpty received "a late exit of the old Companion is stale"

              let onNewState = Restart.guard (fun () -> session) session.Epoch received.Add
              onNewState Starting
              Expect.equal (List.ofSeq received) [ Starting ] "the new start is current"
          }

          test "no event counts after the window closes" {
              let mutable session = Restart.initial
              let received = ResizeArray<State>()
              let onState = Restart.guard (fun () -> session) session.Epoch received.Add
              session <- Restart.onWindowClose session
              onState Ready
              Expect.isEmpty received "a start that ends after the window closes is stale"
          } ]
