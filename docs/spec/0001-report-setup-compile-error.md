# Report the compile error of the Setup, not an error in the block

Spec for #94. Filed from wayfinder ticket 009 on the v0.2 map ("Run works everywhere").

## Problem Statement

A user clicks `▶ Run request`. The Setup above the block does not compile, so the Run cannot
succeed. FsHttp.Studio does not report the fault in the Setup. It reports a compile error
against the block. The usual message is *"The value or constructor 'http' is not defined."*

The message tells the user to examine `http`. The `http` identifier is correct. The fault is in
the Setup. The message gives no indication of the Setup, so the user must guess.

The message is not absent. The message is wrong.

## Solution

When the Setup does not compile, the Run reports the compile error of the Setup. The Run reports
the source location of the Setup. The Run does not evaluate the block.

Example: a user clicks Run on a block below a `for` loop that does not compile. Today
FsHttp.Studio tells the user that `http` is not defined. After this change, FsHttp.Studio tells
the user the fault in the Setup.

The message states that the Setup failed. This wording prevents an incorrect reading when the
location is the first line of the script. Refer to Implementation Decision 3.

## User Stories

1. As a script author, I want a Run to report the fault in the Setup, so that I correct the applicable line.
2. As a script author, I want the Setup error to carry a source location, so that the editor moves me to that location.
3. As a script author, I want the Run to stop when the Setup fails, so that FsHttp.Studio does not show me a second, incorrect error.
4. As a script author, I want each Setup error to keep a location in my script, so that the editor can show it.
5. As a script author, I want an error at the first line to name the Setup, so that I do not misread my `#r` line.
6. As a script author with a correct Setup, I want the Run to behave as it behaves today, so that this change costs me nothing.
7. As a script author with an incorrect block, I want its compile error to stay the same, so that the two failures stay distinct.
8. As a script author whose code throws an exception, I want a runtime error, so that the outcome shows the type of the failure.
9. As a script author, I want the same behavior on both Run paths, so that a version pin does not change the message.
10. As a maintainer, I want a test that shows that the message omits `http`, so that a subsequent change cannot return it.
11. As a maintainer, I want a test for case 9 of the matrix in #90, so that we assert on the reported case.
12. As a maintainer, I want the cause of this defect recorded with the fix, so that no one must measure the `Choice` behavior again.
13. As a maintainer of the reach mechanism, I want a correct Setup error before I start, so that I can identify my own defects.
14. As SchlenkR, I want the reply to #90 to state the true cause of case 9, so that the matrix records the correct cause.

## Implementation Decisions

### 1. Use the diagnostics array. Do not use the `Choice`.

`FsiEvaluationSession.EvalInteractionNonThrowing` returns a `Choice<FsiValue option, exn>` and an
`FSharpDiagnostic[]`. The `Choice` reports an exception from execution. The `Choice` does not
report compile errors. `Choice1Of2` means that the code threw no exception. `Choice1Of2` does not
mean that the code has no errors.

The companion reads the diagnostics of the Setup only in the `Choice2Of2` branch. Do these steps
instead:

1. Read the error diagnostics of the Setup before you examine the `Choice`.
2. If the array contains one error or more, report a compile error.
3. If the array is empty and the `Choice` is `Choice2Of2`, report a runtime error.

Do not use the inner `None` or `Some` of the `Choice` in place of the array. In our measurements,
a correct Setup returned `Choice1Of2 (Some …)`, and a Setup with discarded diagnostics returned
`Choice1Of2 None`. This behavior is an FSI implementation detail and not a documented contract.
The diagnostics array is the contract.

### 2. A Setup failure stops the Run.

When the Setup produces error diagnostics, do not evaluate the block. The second error is the
incorrect message. The second error occurs only because the companion discarded the first error.

### 3. A Setup compile error states that the Setup failed.

The Setup contains the text of the user and the addendum of the companion. Sometimes a parse
failure occurs only in the addendum. The editor cannot show a line after the end of the file. The
companion therefore moves the diagnostic to the first line of the script. Keep this behavior.

Change the wording. A Setup compile error must state that the Setup failed to evaluate. The
message must then give the text of the compiler. A location at the first line then reads as a
Setup failure and not as a fault on that line.

Give the text of the compiler without changes. Do not rewrite a compiler diagnostic. Do not
shorten a compiler diagnostic.

Apply this wording to each Setup compile error, and not only to a diagnostic that the companion
moved.

### 4. Scope: the evaluation of the Setup only.

The evaluation of the block uses a different FCS entry point. That entry point does not have this
defect.

We measured that entry point against the pinned FCS version. We supplied these error types:

- an identifier that is not defined
- text that does not parse
- an expression with incorrect types
- a brace that has no match
- a statement in the position of an expression

Each type returned the exception branch with its diagnostics. The current code reports these
diagnostics. The `"expression returned no value"` result carried no diagnostics in any test.

The block branch prefers an error diagnostic to the exception. This rule is also correct, and we
measured it. We supplied three exception types from expressions with correct types. Each
exception carried zero error diagnostics, so the runtime error keeps the message of the
exception.

Do not change the block branch. This spec records the decision for two reasons. A subsequent
reader does not have to measure this behavior again. A maintainer does not apply the same change
to a path that does not need it.

### 5. One change covers both Run paths.

The in-process path and the `--worker` child process use the same function to evaluate the Setup.
A change in that function corrects both paths. The `--worker` envelope needs no change, because
it already carries a compile error.

### 6. No change to the vocabulary.

This change uses the compile error outcome that `CONTEXT.md` defines: *"A Run whose block or
setup did not compile."* This definition already includes the Setup. Only the code did not obey
the definition. Add nothing to the domain document. Write no ADR. This defect is a defect against
a decision that exists.

### 7. Effect on a correct Run

The change reports a compile error only when the Setup produces error diagnostics. We measured
six correct Setup shapes against the pinned FCS version:

- a pin only
- a pin and a `let`
- a blanked adjacent block
- a binding that the script does not use
- a module
- an empty Setup

Each shape produced zero error diagnostics and zero warnings. No correct Run changes its
behavior.

## Testing Decisions

### What makes a good test

A test must assert on the outcome of the Run. A test must not assert on the method that produced
the outcome. A test that asserts that the companion read a diagnostics array tests the fix. A
test that asserts that the user reads a Setup error tests the behavior. Only the second test
stays correct after a change to the code. This property is important here, because the reach
mechanism changes how the companion builds the Setup.

The primary assertion is negative. The message must not contain `http`. That name is the symptom
of the defect, and a subsequent change can return it.

### Seam

Use the seam that exists. Do these steps:

1. Drive the Run entry point of the companion as a black box.
2. Supply `.fsx` source and a block index.
3. Assert on the outcome.

Do not add a seam. This seam is the highest seam available. Each comparable case already uses it.
The header of that file states this purpose.

Use this prior art:

- *"a non-compiling setup returns compileError"*. This test is the closest test that exists. It
  passes today because its Setup has incorrect types, which returns the exception branch. The new
  tests use a Setup that does not parse, which is the branch that discards the diagnostics. Add a
  comment that states this difference, because the two tests look almost the same.
- *"a non-compiling target block returns compileError with a source range"*. This test is the
  counterpart for the block. It must continue to pass, which shows that the two failures stay
  distinct.
- The suite uses `testSequenced` because of the package cache. New cases keep this property.

### Cases

1. **A Setup that does not parse produces a compile error.** Use the minimal example from #94: a
   `let`, and then two lines that start with `|>`. Its diagnostics keep a location on a line of
   the user, so this case also asserts the source location.
2. **The message does not contain `http`.** Use the same source. This assertion is the regression
   guard.
3. **Case 9 of the matrix in #90.** The block is in the body of a `for` loop. Assert a compile
   error. Assert that the message does not contain `http`. The companion moves the location to
   the first line, so assert only that the location is in the file. Do not assert a specific
   line, because that behavior belongs to the tests of the diagnostic mapping.
4. **A correct Setup still runs.** The tests that pass today cover this case. Add no new test. A
   full suite that passes is the test for Decision 7.
5. **An exception at run time still produces a runtime error.** This case guards the measurement
   in Decision 4.

Add no test project. Add no fixture. Add no server handler.

## Out of Scope

- **The reach mechanism.** A separate spec on the same map defines how the companion builds the
  Setup. This defect is in how the companion reads the result of the Setup, and it survives that
  change. Make this change first and alone, so that the failures of the reach mechanism are clear
  when that work arrives.
- **The refusal lens policy.** A different spec decides whether a block is runnable. This change
  applies to a Run that started and failed.
- **The block branch.** Refer to Decision 4. We measured no defect there.
- **Changes to compiler diagnostics.** The introductory sentence belongs to FsHttp.Studio. The
  diagnostic text after it does not change.
- **Warnings.** Only error diagnostics stop the Run, as today.
- **Cases 10 and 11c.** Refer to Further Notes. Case 10 has a different cause. The refusal work
  covers case 11c.

## Further Notes

### Why to make this change first

Each message of the refusal work depends on this change. A block that FsHttp.Studio refuses to
run must state a reason. The user reads that reason only when the Run reaches it. A discarded
Setup error names the incorrect fault before the Run reaches the reason. The user then reads that
`http` is not defined and does not see the refusal reason.

This change is therefore a prerequisite of the refusal spec, and not an equal item beside it.
Make this change first, in its own pull request. It depends on no other item on the map. It also
makes the subsequent changes clear. When the Setup reports the truth, a failure during the reach
mechanism work belongs to the reach mechanism.

### A correction for the reply to #90

The v0.2 runnability matrix recorded #94 as the cause of matrix cases 9, 10 and 11c. Our
measurements against the pinned FCS version confirm case 9 and refute case 10.

**Case 9 is confirmed.** Its Setup returns the no-exception branch with three parse errors. Each
error starts at the addendum or after it. Today the user reads *"The value or constructor 'http'
is not defined."* After this change, the user reads *"Unexpected keyword 'open' in expression"* at
the first line of the script, with the wording from Decision 3.

**Case 10 is different.** Its Setup is an `if` head without an `else`. This Setup returns the
exception branch with its diagnostics, so the companion reports it correctly today. Case 10 stays
Refused for the reason that the matrix gave. #94 is not its cause.

The reply to #90 must state the cause of case 9. The reply must not give #94 as the cause of case
10.

### The value of this change after the reach mechanism

After FsHttp.Studio refuses a block in the body of a `for` loop, case 9 no longer starts a Run.
This change then has no effect on case 9. The change keeps its value for a different case. The
Setup of the user does not parse, for a reason unrelated to the position of the block. One
example is a user who writes a `let` above the block and stops in the middle of it. That user is
the reason that test case 1 uses the example from #94 and not case 9.

### Why the defect stayed hidden

A Setup with discarded diagnostics does not damage the session. The companion only omits the
Setup. A block that needs nothing from the Setup still succeeds. The defect becomes visible only
when the block needs something that the Setup supplies. The defect then looks like a fault in the
block.

### Provenance

We found this defect during research on #90 §1. The measurements in Decisions 1, 4 and 7, and in
the correction above, come from four probes. We ran the probes for this spec against the FCS
version and the FSharp.Core version that the companion pins. The probes used the addendum text of
the companion without changes.
