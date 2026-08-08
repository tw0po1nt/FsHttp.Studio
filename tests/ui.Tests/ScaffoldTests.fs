module ScaffoldTests

open Fable.Core
open Fable.Mocha

[<Emit("process.env.UI_TEST_ASSERT_DEMO === '1'")>]
let private assertStackDemoEnabled () : bool = jsNative

// The stack-trace demo is a deliberate failure, so it is only registered when it is asked for.
// Leaving it in the default run would show a permanently green case that asserts nothing.
let private assertStackDemo =
    if assertStackDemoEnabled () then
        [ testCase "assert failure names the F# source line" (fun () ->
              Assert.fail "deliberate scaffold failure for stack trace demo") ]
    else
        []

let tests =
    testList
        "UI test suite scaffold"
        ([ testCase "Assert.equal passes" (fun () -> Assert.equal 1 1 "one equals one")

           testCaseAsync
               "eventually waits until the predicate holds"
               (async {
                   let mutable count = 0

                   do!
                       Harness.eventually Harness.ToastDeadlineMs "the poll count to reach three" (fun () ->
                           async {
                               count <- count + 1
                               return count >= 3
                           })

                   Assert.equal count 3 "poll count"
               }) ]
         @ assertStackDemo)
