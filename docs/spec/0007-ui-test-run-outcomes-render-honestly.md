# UI test suite, spec 3 of 7: Run outcomes render honestly

Spec 3 of 7 for the UI test suite that retires `docs/manual-check.md`. This one adds the check named
**Run outcomes render honestly**.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The map
is not a GitHub issue, so this spec restates every decision it depends on rather than linking to one.

**Blocked by** #146 (the harness and its setup), the core path check (spec 2), and #144
(`__SOURCE_DIRECTORY__` resolves to the script's own directory).

## Problem Statement

`docs/manual-check.md`'s *Run outcomes* section has a person confirm two renders that are easy to get
wrong and easy to believe are covered:

- A block that requests a resource answering **404**. The viewer must render the body and the status
  code, and **report no failure** — an HTTP error response is a *successful* Run in this product's
  vocabulary, because the server answered.
- A block pointed at a **port nothing listens on**. The viewer must render the error as plain text —
  a runtime error, which produced no response at all.

These two renders were first bucketed as *covered elsewhere*, on the grounds that
`tests/renderer.Tests` already asserts a 404 render and `tests/companion.Tests` already pins the
outcomes. That reasoning was reviewed and rejected.

`renderer.Tests` asserts that the renderer core draws a 404 when a test **hands it a record** whose
status is 404. It does not prove that a real 404 travels the whole wire. That wire runs from a real server,
through the companion, across the envelope boundary, through the extension host, over
`postMessage`, and into a live webview. At the end of it, the 404 must render as a *response* and
not as a *failure*. The same holds for the connection-refused render. That is exactly the distinction the suite's fidelity floor was written to
make: response viewer content in the webview DOM, not a viewer-update object.

Two further facts make the old bucket wrong rather than merely debatable:

1. **The mechanism was already built and had gone dead.** The test server carries a 404 route and a
   bind-then-close dead port, and the sidecar carries the dead port as one of its two fields. Before
   this check, no fixture read either one — a contract with no reader.
2. **Neither render is hard to automate.** Both run inside the already-warm ExTester session with the
   viewer already open. Two Runs are the whole cost. A cheap, automatable step does not belong in a
   dropped or delegated bucket.

The two renders also differ in a way a unit test cannot see. One is a **success** the viewer must
not dress as a failure. The other is a **failure** the viewer must not dress as a response.
Confusing them is a plausible regression, and a user-visible one.

## Solution

One check in the UI suite, named **Run outcomes render honestly**, over one checked-in fixture holding
two blocks.

The check runs the 404 block and asserts in the webview DOM that the viewer shows the status code and
the body, with no failure reported. Then it runs the dead-port block and asserts that the viewer shows
plain error text, with no status line. Both assertions read the same webview a person reads.

After this spec lands, steps 1–2 and 6–7 of *Run outcomes* are automated, and the test server's 404
route and the sidecar's dead-port field have a reader.

## User Stories

1. As a maintainer, I want a real 404 to travel the whole wire before it is asserted, so that a break
   between the server and the rendered pixel is caught rather than assumed away.
2. As a maintainer, I want the viewer's rendered status code asserted for a 404, so that a status line
   that drops or mislabels a non-2xx status fails the build.
3. As a maintainer, I want the 404's body rendered, so that a viewer that blanks the body on a non-2xx
   response fails the build.
4. As a maintainer, I want the 404 render asserted to report **no** failure, so that a regression that
   re-dresses an HTTP error response as an error is caught.
5. As a maintainer, I want the 404 body to be distinguishable from the probe body on the success route,
   so that the check cannot pass against a stale render of an earlier Run.
6. As a maintainer, I want a real connection-refused error to travel the whole wire before it is
   asserted, so that the runtime-error path is proven end to end.
7. As a maintainer, I want the dead-port render asserted as plain text, so that a runtime error is
   never dressed as a response.
8. As a maintainer, I want the dead-port render asserted to carry **no** status line, so that the two
   outcomes cannot be confused by a partial render.
9. As a maintainer, I want the dead port to be a port the suite *knows* is closed, so that the assertion
   cannot pass or fail for reasons unrelated to the product.
10. As a maintainer, I want setup to have already failed if that "dead" port answered, so that a
    connection-refused assertion can never pass for the wrong reason.
11. As a check author, I want the fixture to read both the base URL and the dead port from the sidecar,
    so that nothing is hardcoded and the fixture survives ephemeral ports.
12. As a check author, I want a named 404 route rather than the server's catch-all, so that a fixture
typo fails differently from a deliberate 404.
13. As a check author, I want a positive tell before the second Run's assertions, so that I am never
    asserting against the first Run's render.
14. As a check author, I want both waits to use the harness's viewer-update deadline, so that this
    check invents no timeout.
15. As a check author, I want no fixed `sleep` in the check, so that it costs what the product costs.
16. As a check author, I want every lens click to find-and-click inside one retry, so that a stale
    handle does not redden a green run.
17. As a maintainer, I want the check to run inside the shared warm session with the viewer already
    open, so that it costs two Runs and nothing more.
18. As a maintainer, I want the check to fit the harness's flat per-check budget, so that drift shows
    up as a budget failure.
19. As a contributor, I want a failure to name the `.fs` line and leave a screenshot, so that I can see
    which of the two renders went wrong.
20. As a maintainer, I want the honest-gap list to stop calling 404 and dead-port renders uncovered, so
that the release gate states no false gap.
21. As a maintainer, I want `renderer.Tests` and `companion.Tests` left exactly as they are, so that
    this adds a layer above them rather than relitigating what belongs where.

## Implementation Decisions

### The fixture

One new checked-in fixture under the suite's `fixtures/` directory, owned by this check alone, holding
**two blocks**. It reads the sidecar beside itself for **both** of its fields:

```json
{ "baseUrl": "http://127.0.0.1:<port>", "deadUrl": "http://127.0.0.1:<deadPort>" }
```

| Block | Requests | Expected outcome |
|---|---|---|
| First | `GET {baseUrl}/notfound` | HTTP error response — 404, body rendered, no failure |
| Second | `GET {deadUrl}/` | Runtime error — plain error text, no response |

This is the only fixture that reads `deadUrl`. Its existence is what makes that sidecar field
load-bearing rather than decorative.

**This depends on #144.** Without it, `__SOURCE_DIRECTORY__` does not resolve to the fixture's own
directory and the sidecar read fails.

### `/notfound` is a named route, not the catch-all

The test server's route table (fixed by spec 1) names `/notfound` explicitly and answers it with a
body distinguishable from `/json`'s. The catch-all also answers 404, and it would satisfy a naive
version of this check by accident.

Naming the route frees the catch-all to mean **"the fixture has a typo"**. That is a different
failure, and this check must never read it as a pass. The check asserts the `/notfound` body's own
distinctive content, so a request that fell through to the catch-all fails.

### What the check does

In order, every wait through `eventually`:

1. Open the fixture. Assert a `▶ Run request` lens above each block (lens-appearance deadline).
2. Click the first block's lens, find-and-click inside one retry.
3. Assert in the webview DOM that the viewer renders: the status code `404`, the status line's URL
   ending in `/notfound`, and the `/notfound` body's distinctive content.
4. Assert, inside that same settled state, that the viewer reports **no failure** — see below for what
   that means precisely.
5. Click the second block's lens, find-and-click inside one retry.
6. Assert in the webview DOM that the viewer renders plain error text naming a runtime error, and that
   the 404 render is gone.
7. Assert, inside that same settled state, that the viewer shows **no status line**.

Steps 4 and 7 are absence assertions, and both obey the positive-tell rule. The check asserts each
one only inside the `eventually` that has already proven its own tell arrived. Those tells are the
404 status line in step 3, and the runtime-error text in step 6. Absence at a fixed time is not
meaningful.

Step 6's "the 404 render is gone" is the second tell that the second Run actually landed. Both renders
write the same viewer, so a single tell is not enough.

### What "reports no failure" means, precisely

A 404 in this product is a **successful Run with an HTTP error response**. The viewer renders it exactly as it renders a 200. That is a status line whose status-code span
carries the 4xx status band, a headers section, and the body rendered by content type. The
difference from a 200 is a class and a number, not a different shape of render.

So step 4 asserts all three of:

- The response render is present — status line plus body region.
- The body is rendered by its content type, not as a plain-text error dump.
- No runtime-error text is present.

That triple is what a person means by "renders the body and the status code, and reports no failure."

Step 7 asserts the mirror. The runtime-error render is **plain text with no status line and no
headers section**, because no response ever existed to describe.

### DOM the check reads

The check asserts rendered text and the renderer's own class names. Those are the status line, the
status-code span and its status-band class, the body region, and the error text. It does not assert
DOM tree shape, element counts, or layout. A restyle must not redden this check. A 404 dressed as a
failure must.

### Session state

The check inherits an open fixture, an open viewer, and a warm companion from the core path check. It
leaves the viewer showing the runtime error. No check that follows depends on the viewer's content, only
on its being open — and spec 4 closes it deliberately.

### Waits and budgets

No new deadline and no new budget. Both viewer assertions use the harness's viewer-update deadline.
The lens assertion uses the lens-appearance deadline. The harness measures the check against its
flat per-check budget, and prints the result to the timing table.

### Consequence for the release gate's honest-gap list

The gap list previously written for `docs/release-gate.md` carried an entry saying that 404 and
dead-port renders were not a dedicated UI check. **That entry must be removed.** A gate that states a
gap it does not have is the same defect as one that hides a gap it does have. Spec 1 writes that document and spec 7 completes it. This spec's obligation is to make sure the
entry does not survive.

## Testing Decisions

**What makes a good test here.** The fidelity floor, unchanged. Response viewer content in the
webview DOM, not a viewer-update object. A real click on a real lens. This check exists *because*
the unit-level versions of these assertions already pass, and prove less than they appear to.

**Seam.** No new seam. The packaged `.vsix` driven through ExTester. **No test-only seam is added to the shipping extension.**

**Modules under test.** The 404 path and the runtime-error path, end to end. That means the
companion's Run outcome, the envelope, the host's mapping from outcome to viewer update, the
webview's handler, and the renderer core — as one thing.

**Prior art in this repository:**

- `tests/renderer.Tests/` — asserts the 404 render from a handed-in record. This check is the
end-to-end escalation of that assertion. Both stay.
- `tests/companion.Tests/` — pins the companion's Run outcomes, including runtime errors.
- Spec 2's core path check — the viewer-reading vocabulary this check reuses verbatim.

**Negative verification.** Run the check twice against deliberately wrong expectations. Once with
the 404 assertion expecting a 200, and once with the dead-port assertion expecting a status line.
Confirm CI goes red both times, with a named `.fs` line.

## Out of Scope

- **The remaining four checks.** Loop lens (spec 4), cross-block Refused Run (spec 5), Compile Error
  (spec 6), companion death (spec 7).
- **Compile errors.** *Run outcomes* steps 3–5 mutate the buffer and touch no network. They are their
  own check, spec 6, and do not merge with this one.
- **Other status codes.** 500, redirects, and the rest are `renderer.Tests`'s business. This check
proves the wire carries a non-2xx faithfully. It does not enumerate the codes.
- **Other content types.** Image, HTML, binary, and text renders are `renderer.Tests`'s business.
- **Changing the harness or the route table.** Both are fixed by spec 1. `/notfound` and `deadUrl`
already exist, and this spec adds nothing to either.
- **Changing `release.yml` or deleting `docs/manual-check.md`.** Those land with spec 7.
- **macOS and Windows.** Linux only, by decision.

## Further Notes

**Why this lands second.** It reuses the core path's viewer knowledge and adds no new mechanism of
its own. That knowledge is how to reach the webview frame, how to read the status line, and what a
settled render looks like.

**What the manual check said, for the record.** This check replaces *Run outcomes* steps 1–2 and 6–7.
Steps 3–5 belong to spec 6.

**The bucket that was corrected.** This check exists because a "covered elsewhere" verdict was reviewed
against the suite's own fidelity floor and failed it. Remember it when a future check looks redundant. The question is not *does an existing suite assert
this property*. The question is *does an existing suite assert it on the channel the user reads*.
