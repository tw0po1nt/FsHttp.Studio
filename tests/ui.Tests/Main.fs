// The suite's single bundle entry. Every check module exposes a test list and is composed here, so
// the bundler never has to name a check file.
module Main

open Fable.Mocha
open Harness

Harness.registerHooks ()

Mocha.runTests (testList "FsHttp.Studio UI tests" [ SelfCheckTests.tests; CorePathTests.tests; RunOutcomesTests.tests ])
|> ignore
