# UI test suite, spec 4 of 7: loop lens refuses with toast

Spec 4 of 7 for the UI test suite that retires `docs/manual-check.md`. This one adds the check named
**loop lens refuses with toast**.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The map
is not a GitHub issue, so this spec restates every decision it depends on rather than linking to one.

**Blocked by** #146 (the harness and its setup) and spec 2 (the core path). **Not blocked by #144** —
this check's fixture never leaves the machine and reads no sidecar.

## Problem Statement

`docs/manual-check.md`'s *The lens tells the truth* section opens with a surface no suite drives.
`docs/spec/0003-lens-tells-the-truth.md` shipped it. A block in a position a Run cannot reach
renders a **refusal lens** instead of `▶ Run request`. A click on that lens shows a **warning
toast** that states the reason and the workaround, and starts no Run.

Three product surfaces meet there, and all three are VSCode interop:

- The refusal lens's rendered title, glyph included.
- The warning toast, raised through `window.showWarningMessage`.
- The response viewer **not** opening.

`host.Tests` covers the pure part honestly. It can assert that a refusal code maps to its shipped
title and detail, because that catalog is a plain module with no interop. What it cannot prove is
that the provider *rendered* that title into a workbench. Nor that a click *raised a notification a
user can read*. Nor that the Run path stayed out of it. The manual check exists for precisely that gap,
and it says so out loud: "No suite drives the lens surface or the toast."

The failure this leaves open is not subtle. A refusal lens that silently invokes the Run command lies to the user about what it will do. So
does a refusal whose toast never appears. Every existing suite stays green through both.

## Solution

One check in the UI suite, named **loop lens refuses with toast**, over one checked-in fixture holding
a block inside a `for` loop.

The check asserts the refusal lens's rendered title in the workbench. It clicks the lens, and
asserts the warning toast's text in the notification UI. It then asserts that no response viewer
opened.

After this spec lands, steps 1–5 of *The lens tells the truth* are automated.

## User Stories

1. As a maintainer, I want the refusal lens's rendered title asserted in a real workbench, so that the
   words a user reads above their block are verified rather than assumed from a unit test.
2. As a maintainer, I want the refusal glyph asserted as part of the rendered title, so that a lens that
   loses its marker and reads like an ordinary action fails the build.
3. As a maintainer, I want the exact shipped sentence for a loop refusal asserted, so that a wording
   change is a deliberate act with a failing test attached.
4. As a maintainer, I want the check to assert that the refused block offers **no** `▶ Run request`
   lens, so that a provider that offers both cannot ship.
5. As a maintainer, I want a real click on the refusal lens, so that the lens's command wiring is
   verified and not just its title.
6. As a maintainer, I want the resulting notification asserted in the notification UI, so that a toast
   that never renders fails the build.
7. As a maintainer, I want the toast asserted to be a **warning**, so that a refusal that degrades to an
   information toast — or escalates to an error — is caught.
8. As a maintainer, I want the toast's text asserted to state the reason, so that a user learns why the
   Run was refused.
9. As a maintainer, I want the toast's text asserted to state the workaround, so that a user learns what
   to do instead.
10. As a maintainer, I want the check to assert that **no response viewer opened**, so that a refusal
    that quietly starts a Run is caught.
11. As a check author, I want the response viewer closed before the click, so that "no viewer opened"
is a real assertion after an earlier check opened one.
12. As a check author, I want a positive tell before that absence assertion, so that I am not asserting
    absence against a viewer that simply has not opened *yet*.
13. As a check author, I want a fixture that needs no network and no sidecar, so that this check is not
    blocked on #144 and cannot fail for a server's reasons.
14. As a check author, I want the toast wait to use the harness's toast deadline, so that this check
    invents no timeout.
15. As a check author, I want no fixed `sleep`, so that the check costs what the product costs.
16. As a check author, I want every lens click to find-and-click inside one retry, so that a stale handle
    does not redden a green run.
17. As a maintainer, I want the check to dismiss the toast it raised, so that a notification does not
    leak into the next check's view of the workbench.
18. As a maintainer, I want the check to leave the workbench in a known state, so that the checks after
    it are not order-dependent in ways nobody wrote down.
19. As a contributor, I want a failure to name the `.fs` line and leave a screenshot, so that I can see
    whether the lens, the toast, or the viewer was wrong.
20. As a maintainer, I want the shipped refusal words to have one owner, so that this check asserts the
product's own string and not a copy.

## Implementation Decisions

### The fixture

One new checked-in fixture under the suite's `fixtures/` directory, owned by this check alone. It holds
a block inside a `for` loop — the `loopBody` refusal shape — and nothing else that a Run could reach.

**The fixture reads no sidecar and needs no live server.** A refused block is never evaluated: no
request is sent, and no code runs. Its URL is therefore an inert literal on loopback. A regression that *does* start a Run then fails
fast and loudly, rather than hanging or succeeding against a real route.

This is what makes the check independent of #144, and it is why this check can land before the two that
are not.

### The shipped words have one owner

The product keeps every refusal's title and detail in one host-side catalog, keyed by the refusal
code the companion sends over the wire. The lens prepends the refusal glyph. The toast and the
response viewer show the sentence without it.

The check asserts against **the shipped strings**. This spec deliberately does not restate them. A
spec that copies a user-facing sentence becomes a second owner of it, and the two drift. The implementation session reads the `loopBody` row, and asserts two things:

- The rendered lens title is the glyph followed by that row's title.
- The toast's text is that row's detail.

Each assertion on rendered text compares **exactly those strings**. Not a paraphrase, and not a
keyword.

### The response viewer must be closed first

This is the one piece of state the check manages. It is also why the check cannot follow the order
the manual check walks.

The response viewer is a single persistent panel. The core path check (spec 2) and Run outcomes (spec 3)
both open it and leave it open. When the viewer is already open, "no response viewer opened" cannot be asserted. The panel is there
for reasons that have nothing to do with this click.

So the check **closes the response viewer before it clicks**, through the workbench's own
editor-closing command. It asserts the viewer is gone before it proceeds. The post-click absence
assertion then means what it says.

The check leaves the viewer closed. The checks that follow open it themselves on their first Run, which
is what they already do.

An alternative was considered and rejected: leaving the viewer open and asserting its *content* is
unchanged. It is weaker, because it cannot distinguish "no Run started" from "a Run started and has not
rendered yet". It also makes this check depend on what the previous check left behind.

### What the check does

In order, every wait through `eventually`:

1. Close the response viewer if it is open. Assert it is gone.
2. Open the fixture. Assert that the block inside the loop renders the **refusal lens** — the glyph plus
   the shipped `loopBody` title — within the lens-appearance deadline.
3. Assert that the same block offers no `▶ Run request` lens.
4. Click the refusal lens, find-and-click inside one retry.
5. Assert that a **warning** notification appears whose text is the shipped `loopBody` detail, within the
   toast deadline. **This is the positive tell.**
6. With that tell in hand, assert that **no response viewer is open**.
7. Dismiss the notification.

Step 6 is the absence assertion, and step 5 is the positive tell that licenses it. That is the rule
the suite applies to every absence. A check never asserts that something is missing until it
observes a visible product signal that the prior step finished.

Step 3 is a second absence assertion, and its tell is step 2. The refusal lens rendering *is* the
proof that the provider has run on this block.

### The notification's level

The check asserts the notification's **type is warning**, not merely that some notification exists.
The product calls `window.showWarningMessage`, and the level is part of what the user reads. A
refusal is not an error, because nothing is wrong with the user's script. It is not an
informational aside either. ExTester's notification page object exposes the type, so the assertion
costs nothing beyond reading it.

### Dismissing the toast

The check dismisses the notification it raised. A VSCode notification can persist, and a stale toast in
the notification center is visible to any later check that looks at notifications. No later check does so today. But leaving one behind makes the session order-dependent in a way
nobody wrote down, and dismissing costs one call.

### Waits and budgets

No new deadline and no new budget. The lens assertion uses the harness's lens-appearance deadline.
The toast assertion uses the toast deadline. The harness measures the check against its flat
per-check budget, and prints the result to the timing table.

This is the cheapest check in the suite: it starts no Run, so it pays for no FSI evaluation and no
network.

## Testing Decisions

**What makes a good test here.** The fidelity floor, unchanged:

- CodeLens text in the workbench, not `executeCodeLensProvider`.
- Toast text in the notification UI, not a `showWarningMessage` call site.
- A real click, on a real lens.

The refusal catalog's own unit coverage stays in `host.Tests` and is not duplicated here. This check
proves the *rendering and wiring* of what that catalog holds, for one refusal code.

**One code, not twelve.** The check drives the `loopBody` refusal only. The catalog holds twelve
codes. Enumerating them through a driven UI would prove one mapping twelve times, at twelve times
the cost. The mapping from code to words is already unit-tested. What is untested is that *a*
refusal renders, and that *a* click toasts. One code establishes both.

**Seam.** No new seam. The packaged `.vsix` driven through ExTester. **No test-only seam is added to the shipping extension.** In particular, no test-visible hook for
the notification. The notification UI is the assertion surface.

**Modules under test.** The provider's refusal-lens rendering, the explain command's registration
and wiring, and the refusal catalog's words as rendered. And, by its absence, the Run command's
non-involvement.

**Prior art in this repository:**

- `tests/host.Tests/` — the pure assertions on refusal codes and their words, which this check escalates
  rather than replaces.
- `docs/spec/0003-lens-tells-the-truth.md` — the spec that shipped this behavior. Its Decisions 2,
8, and 10 are what this check verifies in a workbench.
- Spec 2's core path check — the lens-reading vocabulary this check reuses.

**Negative verification.** Run the check once against a fixture whose block is *not* in a loop.
Confirm it goes red on the lens-title assertion, with a named `.fs` line. That also proves the
check reads a rendered title rather than a constant.

## Out of Scope

- **The other refusal codes.** Eleven more exist. Their words are unit-tested, and their rendering
path is identical to `loopBody`'s.
- **Cross-block Refused Run.** *The lens tells the truth* steps 6–8 are a Run *outcome*, not a lens
  refusal — the second block's lens reads `▶ Run request` and the refusal arrives after the Run starts.
  That is spec 5, and it asserts in the viewer, not in a toast.
- **The stale-lens refusal.** `staleBlockIndex` is a Run outcome with no lens of its own.
- **Asserting the toast's buttons or actions.** The refusal toast carries no action today.
- **Changing the harness.** No new deadline, no new route, no CI change.
- **Changing `release.yml` or deleting `docs/manual-check.md`.** Those land with spec 7.
- **macOS and Windows.** Linux only, by decision.

## Further Notes

**Why this lands third.** It is the cheapest check in the suite — no Run, no FSI, no network. It is
also the first check that closes the response viewer. Landing it after the two viewer checks means
that step is written against a session that genuinely has a viewer open. That is the condition it
exists to handle.

**What the manual check said, for the record.** This check replaces *The lens tells the truth* steps
1–5. Steps 6–8 belong to spec 5.

**The absence rule, stated once more because this check turns on it.** Before a check asserts that
something is missing, it must observe a positive tell that the prior step finished. Absence at a
fixed time is not meaningful. The fix is never a `sleep`.
