# UI test suite, spec 5 of 7: cross-block Refused Run

Spec 5 of 7 for the UI test suite that retires `docs/manual-check.md`. This one adds the check named
**cross-block Refused Run**.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The map
is not a GitHub issue, so this spec restates every decision it depends on rather than linking to one.

**Blocked by** #146 (the harness and its setup) and spec 2 (the core path). **Not blocked by #144** —
this check's fixture never leaves the machine and reads no sidecar.

## Problem Statement

`docs/manual-check.md`'s *The lens tells the truth* section ends with three steps. They look like
the loop-lens steps. They are a different mechanism entirely:

> 6. Bind a value in one block, then use that value in a second block.
> 7. Run the second block, and confirm that the viewer reports a Refused Run.
> 8. Confirm that the viewer marks no fault in the script.

A block that depends on a value another block binds is **not** refused by its position. Its lens reads
`▶ Run request`, because a Run can reach it. The refusal arrives *after* the Run starts. FsHttp.Studio blanks every other block before it
evaluates the setup. The binding the target block needs then has no value, and the companion
answers with a Refused Run that names it.

That makes this a **viewer** assertion, not a toast assertion — and it makes step 8 the interesting one.
The product's position is deliberate and written down. The response viewer owns the report of why a
Run failed, and FsHttp.Studio contributes **no editor diagnostic**. Its per-block isolation can
flag source that is not wrong in the whole file. A squiggle would tell the user their script is broken when
it is not. Nothing enforces that position today.

What is untested is therefore threefold:

- That a Refused Run reaches the response viewer at all, over the same wire a response takes.
- That it renders as a **notice** — a reason and a workaround — and not as an error.
- That the editor stays clean.

`host.Tests` can assert the mapping from refusal code to shipped words. `companion.Tests` can assert
that the companion produces the outcome. Neither observes a webview, and neither observes the editor.

## Solution

One check in the UI suite, named **cross-block Refused Run**, over one checked-in fixture. The
fixture holds two blocks, and the second uses a value the first binds.

The check runs the second block. It asserts in the webview DOM that the viewer renders a Refused
Run whose words name the missing binding. It asserts that the render is a notice rather than an
error. With that render as its positive tell, it then asserts that the script carries no fault
markers.

After this spec lands, steps 6–8 of *The lens tells the truth* are automated.

## User Stories

1. As a maintainer, I want a Refused Run asserted in the webview DOM, so that the refusal path to the
   viewer is proven end to end and not from a viewer-update object.
2. As a maintainer, I want the check to run a block whose lens reads `▶ Run request`, so that the
   difference between a position refusal and a Run-outcome refusal is preserved by the test.
3. As a maintainer, I want the check to assert that the second block's lens is **not** a refusal lens,
   so that a provider that starts refusing this shape at classify time is caught as a behavior change.
4. As a maintainer, I want the rendered refusal to name the binding the block depends on, so that a user
   can tell which value went missing.
5. As a maintainer, I want the shipped `unboundBlockValue` words asserted exactly, so that a wording
   change is deliberate and carries a failing test.
6. As a maintainer, I want the refusal rendered as a notice, so that a user is not told their script is
   broken when it is not.
7. As a maintainer, I want the refusal render asserted to carry no status line, so that a refusal is
   never dressed as a response.
8. As a maintainer, I want the refusal render asserted to carry no runtime-error text, so that a refusal
   is never dressed as a failure.
9. As a maintainer, I want the check to assert that the script shows no fault markers, so that the
   product's decision to contribute no editor diagnostic is enforced rather than merely documented.
10. As a check author, I want a positive tell before that absence assertion, so that I am not asserting
    absence against a diagnostic that has not arrived yet.
11. As a check author, I want the positive tell to be the viewer's own Refused Run render, so that the
    tell proves the Run actually completed.
12. As a check author, I want a fixture that needs no network and no sidecar, so that this check is not
    blocked on #144 and cannot fail for a server's reasons.
13. As a check author, I want the fixture's URLs inert, so that a regression that *does* evaluate the
blanked block fails loudly.
14. As a check author, I want the viewer wait to use the harness's viewer-update deadline, so that this
    check invents no timeout.
15. As a check author, I want no fixed `sleep`, so that the check costs what the product costs.
16. As a check author, I want every lens click to find-and-click inside one retry, so that a stale handle
    does not redden a green run.
17. As a maintainer, I want the check to fit the harness's flat per-check budget, so that drift shows as
    a budget failure.
18. As a contributor, I want a failure to name the `.fs` line and leave a screenshot, so that I can see
    whether the viewer or the editor was wrong.
19. As a maintainer, I want the no-fault assertion's weakness written down, so that nobody reads a
green run as stronger evidence than it is.

## Implementation Decisions

### The fixture

One new checked-in fixture under the suite's `fixtures/` directory, owned by this check alone, holding
**two blocks**:

- The first binds a value to a name — the shape a user writes when one request's result feeds another.
- The second uses that name and is otherwise a perfectly reachable, module-level block.

**The fixture reads no sidecar and needs no live server.** FsHttp.Studio blanks every block other
than the target before it evaluates the setup. The binding the second block needs then has no
value, so the Run is refused before any request is sent. Its URLs are inert literals on loopback. A
regression that *does* send a request then fails fast, rather than succeeding against a real route.

This is what makes the check independent of #144.

### The shipped words have one owner

The `unboundBlockValue` refusal is one of the two outcome-only refusals. The classifier never
produces it, so it has no lens and no row in the position-refusal catalog. Its words are built from
the blanked binding's name. The sentence a user reads therefore names the value that went missing.

The check asserts against **the shipped strings**, built from the fixture's own binding name. This
spec deliberately does not restate the sentence. A spec that copies a user-facing sentence becomes
a second owner of it, and the two drift. The implementation session reads the `unboundBlockValue` words and
asserts the rendered heading and body against them, with the fixture's binding name substituted.

### What the check does

In order, every wait through `eventually`:

1. Open the fixture. Assert that **both** blocks render a `▶ Run request` lens — neither is refused by
   position (lens-appearance deadline).
2. Click the **second** block's lens, find-and-click inside one retry.
3. Assert in the webview DOM that the viewer renders a Refused Run: the heading and the body paragraph
   the refusal's own words specify, with the binding's name in them (viewer-update deadline). **This is
   the positive tell.**
4. Assert, inside that same settled state, that the render is a **notice**: no status line, no headers
   section, no runtime-error text.
5. With the tell in hand, assert that the script carries **no fault markers** — no problem entries
   attributed to the fixture.

Step 1's assertion does real work. It pins the boundary between this check and the loop-lens check.
If the product ever starts refusing this shape at classify time, this check goes red at step 1 with
a legible message. Without it, the check would go red at step 3 with a confusing one.

Step 5 is the absence assertion, and step 3 is the positive tell that licenses it.

### What "renders as a notice" means, precisely

A Refused Run is neither a response nor a failure. The viewer replaces its contents with a heading and a body paragraph, styled as a notice. That
means the editor foreground for the heading and the description foreground for the body, with no
color that reads as a failure.

Step 4 therefore asserts the *shape*, in the renderer's own class names:

- The refusal heading and detail elements are present with the shipped text.
- No status line and no headers section are present.
- No runtime-error text is present.

The check asserts class names and rendered text. It does **not** assert computed colors. A restyle
must not redden this check. A refusal dressed as an error must.

### The no-fault assertion, and its recorded softness

Step 5 asserts that no diagnostics are attributed to the fixture — through the workbench's Problems
view, filtered to the fixture.

**Recorded softness, stated so a green run is not over-read.** In the suite's VSCode, only
FsHttp.Studio is installed. No F# language service is present, so nothing else contributes diagnostics
to a `.fsx` file. An empty Problems view is therefore weaker evidence here than in a user's editor. It proves that
FsHttp.Studio contributes no diagnostic. It cannot prove that FsHttp.Studio's output would not
*look* like a fault beside another extension's.

Two consequences, both deliberate:

- The assertion stays. It is the only automated enforcement of a written product position, it costs
  almost nothing, and it fails correctly if FsHttp.Studio ever starts contributing a diagnostic.
- **Step 4 carries the stronger half of step 8's intent.** "The viewer marks no fault in the script" is
  as much about the *render* being a notice as about the editor being clean. Step 4 is a real, sharp
  assertion in a channel with no such weakness, and it is not optional.

Installing an F# language service into the test VSCode was considered and rejected. It would add a
second extension's activation, its own diagnostics, and its own startup cost to every run of every
check. That is a high price to sharpen one assertion.

### Session state

The check inherits a session in which the response viewer is closed — spec 4 closes it and leaves it
closed. Its own first Run reopens it. It leaves the viewer open, showing the refusal.

### Waits and budgets

No new deadline and no new budget. The lens assertion uses the harness's lens-appearance deadline.
The viewer assertion uses the viewer-update deadline. The harness measures the check against its
flat per-check budget, and prints the result to the timing table.

## Testing Decisions

**What makes a good test here.** The fidelity floor, unchanged. Response viewer content in the
webview DOM, not a viewer-update object. CodeLens text in the workbench. A real click. This check
adds the editor's own state — the Problems view — to that list. Step 8 is an assertion about the
editor, and there is nowhere else to make it.

**Seam.** No new seam. The packaged `.vsix` driven through ExTester. **No test-only seam is added to the shipping extension.** In particular, no diagnostic-collection
handle is exposed for the test. The Problems view is the assertion surface.

**Modules under test.** The companion's Run-outcome refusal for a blanked binding, the envelope,
the host's mapping from a refusal outcome to a viewer update, and the webview's refused render.
And, by their absence, the editor's diagnostics.

**Prior art in this repository:**

- `tests/host.Tests/` — pure assertions on refusal words, which this check escalates.
- `tests/companion.Tests/` — pins the companion's blanking behavior and its refusal outcomes.
- `docs/spec/0003-lens-tells-the-truth.md`, Decisions 6 and 10 — the shipped behavior this check
  verifies in a workbench.
- Spec 2's core path check — the viewer-reading vocabulary this check reuses.

**Negative verification.** Run the check once expecting a status line in the refusal render.
Confirm CI goes red with a named `.fs` line. That proves the notice assertion can tell a refusal
render from a response render, which is the whole point of step 4.

## Out of Scope

- **The stale-lens refusal.** `staleBlockIndex` is the other outcome-only refusal. It is already driven
  by its own coverage and does not need a workbench to prove its words.
- **Position refusals.** Those are spec 4's business, and they arrive by a different path.
- **Enumerating the twelve refusal codes.** The code-to-words mapping is unit-tested. This check proves
  the outcome-refusal path renders, for one code.
- **Asserting the notice's colors.** Class names and text only.
- **Installing an F# language service into the test VSCode.** Rejected. See Implementation
Decisions.
- **Changing the harness.** No new deadline, no new route, no CI change.
- **Changing `release.yml` or deleting `docs/manual-check.md`.** Those land with spec 7.
- **macOS and Windows.** Linux only, by decision.

## Further Notes

**Why this lands fourth.** It reopens the response viewer that spec 4 closed. The ordering keeps
the session's viewer state legible: opened by spec 2, used by spec 3, closed by spec 4, reopened
here.

**What the manual check said, for the record.** This check replaces *The lens tells the truth* steps
6–8. Steps 1–5 belong to spec 4.

**The distinction worth keeping.** A position refusal is refused *before* a Run. No code is evaluated, the lens is a refusal lens, and
the user gets a toast. A cross-block refusal is refused *during* a Run. The lens is ordinary, there
is no toast, and the viewer shows a notice. Two mechanisms, two checks. Step 1 of this check stops
them from quietly becoming one.
