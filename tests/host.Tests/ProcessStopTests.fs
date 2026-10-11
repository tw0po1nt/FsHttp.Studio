module Extension.Tests.ProcessStopTests

open Expecto
open ProcessStop

[<Tests>]
let tests =
    testList
        "ProcessStop"
        [ test "platformOf maps the process.platform of Node" {
              Expect.equal (platformOf "linux") Posix "Linux has process groups"
              Expect.equal (platformOf "darwin") Posix "macOS has process groups"
              Expect.equal (platformOf "win32") Windows "Windows has no process groups"
          }

          test "Linux and macOS signal the process group of the companion" {
              Expect.equal
                  (killCommand Posix 4242)
                  (SignalGroup 4242)
                  "the group contains each process that the companion started"
          }

          test "Windows runs taskkill on each process that the companion started" {
              Expect.equal
                  (killCommand Windows 4242)
                  (Exec("taskkill", [| "/T"; "/F"; "/PID"; "4242" |]))
                  "taskkill must stop the child processes too, by force"
          }

          test "the companion spawns detached on Linux and macOS only" {
              Expect.isTrue (detachOnSpawn Posix) "a detached child leads its own process group"
              Expect.isFalse (detachOnSpawn Windows) "a detached child on Windows opens a console window"
          } ]
