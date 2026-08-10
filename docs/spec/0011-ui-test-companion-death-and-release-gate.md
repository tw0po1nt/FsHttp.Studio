# UI test suite, spec 7 of 7: companion death, and the release gate without a human

Spec 7 of 7 for the UI test suite that retires `docs/manual-check.md`. This one adds the last and riskiest check, **companion death is visible and recoverable**. It then
switches the release gate off a person and onto the suite.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The map
is not a GitHub issue, so this spec restates every decision it depends on rather than linking to one.

**Blocked by** #146 (the harness and its setup), spec 2 (the core path), and #144
(`__SOURCE_DIRECTORY__` resolves to the script's own directory). It should land only when specs 2–6
are green, because its second half deletes the walk.

## Problem Statement

Two problems close together here, and the second is why this is the last spec.

**The check.** `docs/manual-check.md`'s *The companion stops* section covers `Companion.fs`, which
is Fable and Node interop that no suite drives. It also covers the behavior
`docs/spec/0004-run-path-robustness.md` Decision 6 shipped. When the companion process exits with a
Run in flight, the pending Run must not hang forever on `Running…`. It must abandon to a message
that tells the user what happened and what to do. And the window reload that message recommends
must actually work.

A person is most likely to skip that section, and least likely to get it right by hand. It requires
killing a process at the right moment. It is also the section whose loss would hurt most. A Run
that spins on `Running…` forever, with no explanation, is the worst failure this product has.

**The gate.** Today `release.yml` refuses a version that has no Beta pre-release. ADR-0008 makes an
operator's comment on that pre-release the record that the walk happened. The workflow's own comment
admits what it is: *"A tripwire, and not a proof. It shows that a Beta was built, and it cannot show
that an operator walked `docs/manual-check.md`."*

So the release gate is a person's memory, checked by a tag's existence. Once the six checks exist,
that gate is strictly worse than the automation beside it. And `docs/manual-check.md` becomes a
file that instructs a human to do work a machine now does on every pull request.

## Solution

Two halves, in one spec because neither is safe without the other.

**First, the check.** A check named **companion death is visible and recoverable**. It runs a block
against the hang-until-release route. It waits for the server to confirm the request arrived. It
kills every companion process. It asserts the viewer leaves `Running…` for the stopped message. It
reloads the window, and asserts a Run succeeds again.

**Second, the switchover.** With all six checks green, `release.yml` stops requiring a Beta and starts
requiring the suite. `docs/release-gate.md` gets its remaining honest gaps. `docs/manual-check.md` is
deleted. **Beta** survives as a distribution channel and is redefined. ADR-0008 is superseded.

After this spec lands, nobody walks the manual check, because there is no manual check.

## User Stories

### The check

1. As a maintainer, I want a Run killed mid-flight by a real process kill, so that the failure mode a
   user hits is the failure mode under test.
2. As a maintainer, I want the request confirmed to have **arrived at the server** before the kill, so
   that the check tests death during a Run and not death before the request ever left.
3. As a maintainer, I want the viewer asserted to show `Running…` before the kill, so that the
   in-flight state is a proven precondition and not an assumption.
4. As a maintainer, I want the viewer asserted to **leave** `Running…` after the kill, so that the
   forever-spinning failure is caught.
5. As a maintainer, I want the stopped message asserted in the webview DOM, so that the user is told
   what happened rather than left with a blank panel.
6. As a maintainer, I want the stopped message's exact shipped words asserted, so that a wording change
   is deliberate and carries a failing test.
7. As a maintainer, I want the message to tell the user to reload, so that the instruction matches what
   actually recovers the product.
8. As a maintainer, I want the window reload driven for real, so that the recommended recovery is
   verified rather than advertised.
9. As a maintainer, I want a Run asserted to succeed after the reload, so that "recoverable" means a
   working product and not a fresh process.
10. As a maintainer, I want the post-reload success asserted on the core path's own channels, a status
code and a body in the viewer, so that recovery is proven to the same standard as normal operation.
11. As a check author, I want a hang route that a control endpoint releases, so that the in-flight
    window is controlled by the suite rather than by a sleep.
12. As a check author, I want the hang releasable more than once per run, so that a later hanging
    request does not silently return at once.
13. As a check author, I want the kill to find every companion process, so that a surviving child makes
    the check red rather than flaky.
14. As a check author, I want the kill confirmed, so that the check does not proceed on the belief that
    a process died.
15. As a check author, I want a **fresh companion process** as the tell that the reload finished, so
that I do not assert against the old workbench.
16. As a check author, I want the hang released during teardown, so that the test server is not left
    holding a stuck request for the rest of the job.
17. As a check author, I want every wait through the harness's `eventually`, with its post-reload
    deadline for the reload, so that this check invents no timeout.
18. As a check author, I want no fixed `sleep`, so that the check costs what the product costs.
19. As a maintainer, I want the check to fit the harness's flat per-check budget, so that its known cost
    is visible and its drift is caught.
20. As a contributor, I want a failure to name the `.fs` line and leave a screenshot, so that I can see
    which stage of the death-and-recovery sequence failed.

### The switchover

21. As a releaser, I want the UI suite to gate the release, so that the gate is evidence rather than a
    tripwire.
22. As a releaser, I want the release workflow to refuse a draft Release when the suite is red, so that
    a known-broken build cannot ship.
23. As a releaser, I want the Beta requirement removed from the release workflow, so that a tag's
    existence stops standing in for a person's attention.
24. As a releaser, I want an escape hatch for an emergency release, so that a documentation fix is not
    blocked by a flaky UI job.
25. As a releaser, I want that escape hatch to log loudly, so that its use is visible in the record.
26. As a releaser, I want a green Actions run to be the record of what was verified, so that no human
    comment has to be written or trusted.
27. As a maintainer, I want a Beta to keep existing as a way to hand someone a build, so that a useful
    channel is not deleted along with the ceremony around it.
28. As a maintainer, I want **Beta** redefined in the project's language, so that a term whose
    definition referenced a walk that no longer exists still means something.
29. As a maintainer, I want **Manual check** removed from the project's language, so that the vocabulary
    does not name a practice the project abandoned.
30. As a releaser, I want the gate's honest gaps in one document I actually read, so that a gate that
    narrows quietly is impossible.
31. As a releaser, I want a stated prerequisite about the VSCode pin, so that a release does not ship on
    a known-stale toolchain.
32. As a spec author, I want the "new untestable surface" rule rehoused before the manual check is
deleted, so that the instruction does not vanish with its file.
33. As a maintainer, I want ADR-0008 superseded rather than edited, so that the record shows what was
    decided, when, and what replaced it.
34. As a maintainer, I want `docs/manual-check.md` deleted only when all six checks are green, so that
    the walk is never removed while its replacement is partial.

## Implementation Decisions — the check

### The fixture

One new checked-in fixture under the suite's `fixtures/` directory, owned by this check alone. It
holds **two blocks**, and reads `baseUrl` from the sidecar beside it:

| Block | Requests | Role |
|---|---|---|
| First | `GET {baseUrl}/slow` | Hangs until released. The companion dies under it. |
| Second | `GET {baseUrl}/json` | The recovery Run, after the window reloads. |

**This depends on #144.**

### What the check does

Prototyping drove this exact sequence: ten CI jobs, cold and warm, twenty suite runs, all green.
The steps below are what it established, and not a design sketch.

1. Click the first block's lens, find-and-click inside one retry. Assert the viewer shows `Running…`.
2. **Wait for the server to report the request as arrived**, through its status endpoint's waiting
   count. This is the step without which the whole check is a lie.
3. Kill **every** companion process, and confirm each is gone.
4. Assert the viewer leaves `Running…` and renders the shipped stopped message (viewer-update deadline).
5. Reload the window. Wait for a **fresh companion process** — one that is neither absent nor one of the
   killed ones — within the post-reload deadline. Then reopen the fixture.
6. Click the second block's lens, find-and-click inside one retry. Assert the viewer renders `200` and
   the probe body.
7. In teardown, release the hang.

### Step 2 is not optional, and here is why

The first Run in a fresh session pays for a `#r "nuget:"` restore before it ever opens a socket. A
kill timed to the click, or to the appearance of `Running…`, lands during that restore. The server
then never sees a request at all. The check would assert that a death is visible when the companion
died *before* the Run. That is a different property, and a much easier one.

The test server therefore exposes an **arrival tell** — a status endpoint reporting how many requests
are waiting on the hang route. The check kills only after that count rises. This is the difference
between the check the manual walk describes and a check that looks like it.

### Killing the companion

Match the companion process by its assembly, kill with a signal that cannot be caught, and confirm.

On `ubuntu-latest` this matches **two** processes: the runtime host and its child. **Both must be
killed.** Killing one leaves a survivor that can answer, and the check becomes flaky rather than
red.
The check asserts that every matched process is gone before it proceeds.

### The reload's tell is a fresh companion, not a workbench

Prototyping's sharpest finding about the reload is this: **the workbench-ready wait is not a reload
tell**. It returns while the pre-reload DOM is still up. Every pre-reload element handle goes
stale. Every *fresh* lookup then works — the workbench, the lens, the webview frame — but only once
the reload has happened.

The tell that the reload happened is **a companion process that is neither absent nor one of the
killed ones**. The check waits on that. It then reopens the fixture, and looks everything up fresh.
It reuses no handle across the reload.

### The hang route needs three things, and the obvious design of it is wrong

Spec 1 specified the hang route with the server. It is restated here for two reasons. This is the
only check that exercises it, and two of the three requirements were found the hard way:

- A **release** control endpoint, so the hang ends on the suite's command and not on a timer.
- An **arrival** endpoint, per step 2 above.
- **Per-request thread-pool dispatch.** A single-threaded listener deadlocks the release behind the
  hang, and the job dies at its timeout with nothing legible in it.

And the release mechanism is a **generation counter, not a latch**. A sticky latch, once set, is
never reset. Every later request to the hang route then returns immediately. A check whose hang
silently does not hang is a bad failure, because it passes for the wrong reason. The route reads
the generation on arrival, and waits for it to advance.

### The second click is dropped, deliberately

The manual check's steps 5 and 6 read: *click the lens again, confirm the viewer reports the same
message immediately*. They are **not automated, and the suite does not intend to close that gap**.

Prototyping measured the post-death lens across eighteen observations. It was usually still
clickable more than twenty seconds after the companion died. It re-reported the stopped message in
a fraction of a second. But once it was gone within ten seconds. And once a click on it raised a
stale-element error, and reddened the job. **Neither its presence nor its absence is stable**, so the check asserts neither.

Two consequences, both recorded rather than hidden:

- The uncovered path is a send on a closed handle. `host.Tests` can drive that directly, without a
  workbench, and that is where it belongs.
- This exposed a real product disagreement. ADR-0003's "no companion, no lenses" is what the
provider's readiness flag intends, and the rendered lens usually does not comply. It is filed as
its own issue (#145), and it is not on this suite's route.

> **Update (2026-08-10):** #145 is closed, and the check now makes one claim about the post-death
> lens. The product disagreement was settled the other way: ADR-0003's invariant became "no
> companion, no *runnable* lenses", so the lens stays and reads `⊘ Cannot run: the companion
> stopped`.
>
> That title is stable where presence and absence were not. The provider now returns a lens
> instead of an empty list. The editor therefore repaints the lens, and no longer has to remove
> one, which is the step it performed slowly. The check asserts the title on the lens above each
> block, between the stopped message and the reload.
>
> The second click stays dropped. The manual step it came from asserted that a second click
> re-reports the stopped message in the viewer, and that is no longer the behavior. A click on the
> stopped lens shows the same sentence in a warning toast, and starts no Run.

### Cost, and the budget

Prototyping measured the whole check at **14.0–20.1 seconds, median 17.4**, including the reload.
Death to stopped message was **0.1 seconds**. Death to a recovered Run was about **10.4 seconds
median, and 12.9 seconds worst**. That is a fifth of the post-reload deadline.

So the reload is **not** the expensive part. The standing advice to "drop the reload first if this
check flakes" is withdrawn. The reload is measured, it is cheap, and it is the only proof that the
message's instruction works. The check fits the harness's flat per-check budget with room.

### Session state

This check runs last. It kills the companion and reloads the window, so it leaves the session in a state
no other check should try to inherit. It restores nothing beyond releasing the hang.

## Implementation Decisions — the switchover

Do this half **only when specs 2 through 6 are landed and green**. Removing the walk while its
replacement is partial would leave the project with neither.

### `release.yml`

- **Remove** the step that requires a `v<version>-beta.*` tag. A tag's existence never showed that
  anyone walked anything.
- **Run the UI suite inside the release workflow**, with the same budgets and job retries as everywhere
  else. Do not gate on a previously-green CI check for the same SHA — the release path runs its own.
- **Refuse the draft Release when the suite is red.**
- **Keep the `force` input**, redefined: it skips the UI suite for an emergency release. It must log
  loudly and unmistakably when used, so the record shows a release that skipped its gate.

### The record

A green release-workflow run is the record. No human comment enumerating sections. Optionally link the
Actions run from the draft Release body, for a releaser reading the Release rather than the workflow.

### `docs/release-gate.md`

Created in spec 1. Completed here. It states what the suite proves, the budgets, and — the point of the
document — what it does not cover. Sweep the gap list before writing:

- **Keep** the VSCode-pin entry, verbatim as spec 1 supplies it, and the pin-update pull request
  prerequisite.
- **Keep** Linux-only.
- **Keep** the dropped Beta ceremony. The suite does not exercise cutting a Beta, opening the
pre-release, or downloading the `.vsix`. A broken Beta workflow can therefore ship while packaging
and install stay green.
- **Keep** the missing-runtime path. A machine with no .NET, or a misconfigured runtime path, is
not covered. The manual check never covered it either.
- **Add** the post-death second click, per the decision above: after the companion dies, the suite does
  not assert what the lens does.
- **Remove** any entry that calls the 404 and dead-port renders uncovered. Spec 3 covers them. A
gate that states a gap it does not have is the same defect as one that hides a gap it does have.
- **Remove** any entry that says the companion-death check can drop its reload. It does not.

The document also inherits the **"new untestable surface" rule** that `docs/manual-check.md`
carried. A spec that finds a surface no suite drives records it in the honest-gap section, and
prefers to automate it. Do not leave that rule stranded inside a shipped spec, because nobody reads
a shipped spec again.

### `CONTEXT.md`

- **Rewrite Beta.** It survives as an optional pre-release `.vsix` channel for getting a build into
  someone's hands. It is not tied to a walk and it is not required for a release.
- **Remove Manual check.** The practice is gone, and the term must not outlive it.
- Leave every other term alone.

### `docs/manual-check.md`

**Delete it**, in the same change that lands the release-gate document and the workflow change. Not
before.

### ADR-0008

**Supersede, do not edit.** ADR-0008 recorded a real decision made for real reasons. The record of
why the Beta became the gate is worth keeping intact. A new ADR states that the UI suite is the
gate, that a Beta is distribution only, and why. ADR-0008 is then marked superseded by it.

## Testing Decisions

**What makes a good test here.** The fidelity floor, unchanged. Response viewer content in the
webview DOM. A real click on a real lens. A real process kill. A real window reload.

**Seam.** No new seam. The packaged `.vsix` driven through ExTester. **No test-only seam is added to the shipping extension.** In particular, no command that stops or
restarts the companion for the test's benefit. The check kills the process from outside, which is
what a crash does.

**Modules under test.** Four things: the companion's process lifecycle handling, the host's
abandonment of a pending Run when the companion exits, the stopped message's path to the viewer,
and the extension's activation after a window reload.

**Prior art in this repository:**

- `docs/spec/0004-run-path-robustness.md`, Decision 6 — the shipped behavior this check verifies. Its
  own text records that the surface was verified by hand.
- `tests/host.Tests/` — where the send-on-a-closed-handle assertion belongs, now that the second click
  is dropped.
- Spec 2's core path check — the viewer-reading vocabulary the recovery assertion reuses verbatim.

**Negative verification.** Two runs, both required:

1. Assert an obviously wrong stopped message and confirm CI goes red with a named `.fs` line.
2. Remove the arrival wait and confirm the check becomes unreliable — this is the one design decision in
   the suite whose necessity is invisible from the code.

**Before the switchover half is called done**, the suite must run green end to end on a pull
request. All six checks and the setup self-check must be present. The timing table must show every
check inside its budget.

## Out of Scope

- **The post-death lens.** The check asserts neither its presence nor its absence. See above. The
product question is #145.
- **Restarting the companion automatically.** The message says reload because reload is what recovers
  the product today. Whether the extension should restart the companion itself is a product question.
- **Killing the companion by other means.** A crash inside the companion, an out-of-memory kill, or
a runtime that fails to start. One kill path proves the abandonment logic.
- **Registry publishing.** `vsce` and `ovsx` publishing stays as it is and is not part of this gate.
- **Deleting `beta.yml`.** The Beta survives as distribution.
- **Editing ADR-0008 in place.** Superseded, not rewritten.
- **macOS and Windows.** Linux only, by decision. A defect that appears only in runtime discovery or
  companion process handling on those platforms ships uncaught — the accepted trade for retiring the
  walk.

## Further Notes

**Why the check and the switchover are one spec.** The switchover deletes the walk. The walk cannot
be deleted while a section of it is uncovered, and this check is the last uncovered section. A
split would create a window in which the project has a partial suite, a deleted walk, and a gate
that means nothing. Landing them together makes the last check's green the switchover's
precondition, enforced by their being one change.

**What the manual check said, for the record.** This check replaces *The companion stops* steps 1–4 and
7. Steps 5–6 are dropped with the reasons above. *Record the result* is dropped: the green Actions run
is the record.

**The measured numbers, so nobody re-derives them.**

| Measure | Observed |
|---|---|
| Death to stopped message | 0.1 s |
| Death to recovered Run | ~10.4 s median, 12.9 s worst |
| The whole check | 14.0–20.1 s, median 17.4 s |
| Reload survival | 20 of 20 suite runs |
| CI jobs green | 10 of 10, cold and warm |

**And the finding that makes those numbers trustworthy.** An earlier prototype's ten green runs
were never falsifiable. Piping the run script into `tee` under a non-pipefail shell reported a red
suite as a green job. The numbers above come from the run made *after* that was fixed. Every budget
and every assertion in all seven specs depends on a red suite reddening the job. That is why spec 1
requires one deliberate failing run as an acceptance criterion, and why this spec requires two.
