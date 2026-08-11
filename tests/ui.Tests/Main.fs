module Main

open Fable.Mocha
open Harness

Harness.registerHooks ()

Mocha.runTests (
    testList
        "FsHttp.Studio UI tests"
        [ SelfCheckTests.tests
          CorePathTests.tests
          RequestSectionTests.tests
          CopyButtonsTests.tests
          RunOutcomesTests.tests
          LoopLensTests.tests
          CrossBlockRefusedRunTests.tests
          CompileErrorTests.tests
          NoRequestsLensTests.tests
          CompanionDeathTests.tests ]
)
|> ignore
