module ScaffoldTests

open Fable.Core
open Fable.Mocha

[<Emit("process.env.UI_TEST_ASSERT_DEMO === '1'")>]
let private assertStackDemoEnabled () : bool = jsNative

let private scaffoldTests =
    testList
        "UI test suite scaffold"
        [ testCase "Assert.equal passes" (fun () -> Assert.equal 1 1 "one equals one")

          testCaseAsync
              "eventually waits until the predicate holds"
              (async {
                  let mutable count = 0

                  do!
                      Harness.eventually Harness.ToastDeadlineMs 25 (fun () ->
                          count <- count + 1
                          count >= 3)

                  Assert.equal count 3 "poll count"
              })

          testCase "assert failure names the F# source line when UI_TEST_ASSERT_DEMO is set" (fun () ->
              if assertStackDemoEnabled () then
                  Assert.fail "deliberate scaffold failure for stack trace demo") ]

Mocha.runTests scaffoldTests |> ignore
