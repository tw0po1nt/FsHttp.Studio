module Extension.Tests.ProcessStopTests

open Expecto
open ProcessStop

[<Tests>]
let tests =
    testList
        "ProcessStop"
        [ test "Linux and macOS signal the process group of the companion" {
              Expect.equal (killCommand "linux" 4242) (SignalGroup 4242) "linux"
              Expect.equal (killCommand "darwin" 4242) (SignalGroup 4242) "darwin"
          }

          test "Windows runs taskkill for the process tree" {
              Expect.equal (killCommand "win32" 4242) (Taskkill 4242) "win32"

              Expect.equal (taskkillArgs 4242) [| "/T"; "/F"; "/PID"; "4242" |] "taskkill kills the tree by force"
          }

          test "the companion spawns detached on Linux and macOS only" {
              Expect.isTrue (detachOnSpawn "linux") "linux"
              Expect.isTrue (detachOnSpawn "darwin") "darwin"
              Expect.isFalse (detachOnSpawn "win32") "win32"
          }

          test "the stop waits 1000 ms before the kill" { Expect.equal stopDelayMs 1000 "delay" } ]
