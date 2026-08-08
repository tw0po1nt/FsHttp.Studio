# The core path: a click on a lens, a response viewer that renders it

## Problem Statement

`docs/manual-check.md` opens with the section a person walks first and trusts most — **The core
path**. It has seven numbered steps. The lens appears above each block. A click opens the response
viewer beside the editor. The viewer shows `Running…` in flight, then the body and the status code.
A click on a second block renders the second response and not the first.

Nothing automated observes any of it. `host.Tests` asserts what `CodeLensProvider` returns.
`companion.Tests` asserts what the companion answers. `renderer.Tests` asserts what the renderer core
draws when a test hands it a viewer-update record. `npm run smoke` proves the bundle loads under
node. Every one of them stops short of a workbench.

So the product's most load-bearing property — *a user clicks a lens and sees a response* — is
verified only by a person. Only against a Beta, and only when someone remembers to walk it. A
regression anywhere along that wire ships green through four suites: the lens's command arguments,
the viewer panel's creation, the `postMessage` shape, the webview's message handler, or the
renderer's mount point.

Spec 1 built the ground: a packaged `.vsix` in a real VSCode, a driven UI, a local HTTP server, a
sanctioned wait, and a self-check proving the ground is live. It contains no product check. This spec
puts the first one on it.

## Solution

One check in the UI suite, named **the core path**, over one checked-in fixture script.

The check clicks the Run request CodeLens on a block. It watches the response viewer open beside
the editor and render the response. It then clicks a second block's lens, and watches the viewer
replace the first response with the second. Every assertion reads the workbench or the webview DOM
— the same channels a person reads.

After this spec lands, steps 1 through 7 of the manual check's core-path section are covered by
something that runs on every relevant pull request. The file itself is not deleted here. It is deleted with spec 7, when all six checks exist.

## User Stories

1. As a maintainer, I want a check that clicks a real Run request CodeLens in a real workbench, so
   that the lens surface is verified by something other than a person.
2. As a maintainer, I want the check to observe the lens's rendered title, so that a lens that
   renders with the wrong words fails the build.
3. As a maintainer, I want the check to observe that a lens appears above *each* block in the
   fixture, so that a provider that finds only the first block fails the build.
4. As a maintainer, I want the check to assert that the response viewer opens beside the editor, so
   that a viewer that opens in the wrong column or not at all fails the build.
5. As a maintainer, I want the check to observe `Running…` while a Run is in flight, so that the
   in-flight state a user sees is verified rather than assumed.
6. As a maintainer, I want the check to assert the rendered response body in the webview DOM, so that
   a break anywhere between the server and the rendered pixel is caught.
7. As a maintainer, I want the check to assert the rendered status code in the webview DOM, so that
   the status line's wiring to a real response is verified.
8. As a maintainer, I want the check to assert the status line's URL, so that the viewer is proven to
   be showing the response to the block that was clicked.
9. As a maintainer, I want a second Run in the same script to replace the first response, so that a
   viewer that appends, caches, or races is caught.
10. As a maintainer, I want the second block's response to be distinguishable from the first, so that
    "the second response and not the first" is a real assertion and not a tautology.
11. As a maintainer, I want the two blocks to differ in both body and URL, so that a stale render
    fails on two independent tells rather than one.
12. As a check author, I want the fixture to be a checked-in `.fsx` that the editor opens verbatim, so
    that a failing check is reproducible by opening the same file by hand.
13. As a check author, I want the fixture to read the server's base URL from the sidecar, so that no
    port is hardcoded and the fixture survives an ephemeral port.
14. As a check author, I want every wait to go through the harness's `eventually`, so that this check
    invents no polling of its own.
15. As a check author, I want to use the harness's named deadlines, so that this check invents no
    timeout of its own.
16. As a check author, I want every lens click to find-and-click inside one retry, so that a handle
    that goes stale between the find and the click does not redden a green run.
17. As a check author, I want no fixed `sleep` anywhere in the check, so that the check costs what the
    product costs and races on no runner.
18. As a maintainer, I want the check to run inside the shared ExTester session the harness already
    started, so that it costs a Run and not a VSCode launch.
19. As a maintainer, I want the check to fit the harness's flat per-check budget, so that a drift in
    the product's own speed is visible.
20. As a maintainer, I want the check to leave the workbench in a state the next check can use, so
    that the six checks can share one session.
21. As a contributor, I want a failure to name the `.fs` line of the failing assertion, so that I can
    find it without reading a bundle offset.
22. As a contributor, I want a failure to leave a screenshot of the editor behind, so that I can see
    what the viewer actually rendered.
23. As a contributor, I want the check's assertions phrased against what a user sees, so that I can
    read the check and know which manual step it replaced.
24. As a maintainer, I want the response proven to travel from a real server, through the companion and
the host, into a live webview, so that the four existing suites gain an end-to-end companion.
25. As a maintainer, I want this check to be the first product check to land, so that the later five
    can borrow its viewer knowledge rather than each discover it.

## Implementation Decisions

### The fixture

One checked-in fixture under the suite's `fixtures/` directory, owned by this check alone. Fixture
content is never shared between checks — the rule is one fixture per Automated check.

It holds **two blocks**, both of which a Run can reach (neither is refused), and both of which request
the local test server. It reads the sidecar beside itself:

- `Path.Combine(__SOURCE_DIRECTORY__, "sidecar.json")`, parsed for `baseUrl`.
- The sidecar is written by the test server at startup and is gitignored. The fixture is checked in.
- This depends on #144. Without it, `__SOURCE_DIRECTORY__` resolves to the process working directory
  and the fixture reads nothing.

The fixture needs no package reference beyond the `#r "nuget: FsHttp"` line every fixture carries. It reads two string fields out of a two-field JSON file. Prefer a minimal read to a JSON package in
a fixture's restore path. Every added restore is charged to the first Run's wall clock.

### The two blocks, and what makes them distinguishable

Both blocks target routes that already exist in the test server's fixed route table (spec 1). This
spec adds no route and changes no route.

| Block | Route | Renders |
|---|---|---|
| First | `GET {baseUrl}/json` | 200, the stable probe body |
| Second | `GET {baseUrl}/status` | 200, `{"slowSeen":N,"slowWaiting":M}` |

The manual check's step 7 reads: *the viewer renders the second response, and not the first*. That
needs two responses a DOM assertion can tell apart. Two calls to `/json` render identical bodies,
so the assertion would pass against a viewer that never updated. `/status` is therefore the second
200, rather than a second call to `/json`.

Using `/status` gives **two independent tells**: a different URL in the status line, and a different
JSON body. A stale render has to defeat both.

**Assert on `/status`'s key names, never its numbers.** `slowSeen` and `slowWaiting` are counters.
Their values depend on whether the companion-death check has already run in the session. The check
asserts that the key `slowSeen` is rendered, and that the `/json` probe body's distinctive key is
*not*. The two probe bodies must therefore have no key in common. That is a requirement on the route
table's bodies, and the only thing this spec asks of the server.

### What the check does

In order, with every wait through `eventually`:

1. Open the fixture. Assert that a `▶ Run request` lens is rendered above **each** of the two blocks
   (lens-appearance deadline).
2. Click the first block's lens, find-and-click inside one retry.
3. Assert the response viewer opened beside the editor — a second editor group holding the viewer.
4. Assert `Running…` is rendered in the viewer (see the softness note below).
5. Assert, in the webview DOM, that the viewer renders the status code `200`, the first block's URL in
   the status line, and the probe body (viewer-update deadline).
6. Click the second block's lens, find-and-click inside one retry.
7. Assert, in the webview DOM, that the viewer renders `/status`'s URL and the `slowSeen` key, and
   that the first block's distinctive body key is gone.

Step 7 is the stale-render assertion. It contains an absence — the first body is gone — so it obeys
the positive-tell rule. The check asserts that absence only inside the same `eventually` that first
proves the second response arrived. Absence at a fixed time means nothing.

### The `Running…` assertion, and its recorded softness

Step 4 is the one assertion in this check that races the product rather than waiting for it. `Running…`
is a transient state, and a check that looks for a transient state can lose.

The check asserts it anyway, and it is expected to hold. The **first** Run in a session pays for a
`#r "nuget:"` restore and a cold FSI session. Prototyping measured that first Run in seconds, not
milliseconds. The assertion is therefore ordered deliberately: `Running…` is asserted on the **first**
Run of the check, never the second.

**Recorded softness.** If this assertion flakes in practice, the correct response is to drop it from
this check, not to add a retry or a sleep. The companion-death check (spec 7) asserts `Running…`
against the hang-until-release route, where the in-flight window is controlled by the suite and the
assertion is deterministic. The core path's copy is a convenience, not the only coverage.

### DOM the check reads

The check reads the webview DOM the shipping renderer produces. It does not read a `postMessage`
payload, and it does not call a provider directly. The renderer already commits to stable class names
for exactly this kind of reading:

- The status line, its URL span, and its status-code span, which also carries a status-band class.
- The rendered JSON body region.
- The in-flight indicator's label.

The check asserts against **rendered text and the renderer's own class names**. It does not assert a DOM tree shape, an element count, or a layout property. A restyle must not
redden this check. A wrong status code must.

### Session state, and what the next check inherits

The check leaves the fixture open and the response viewer open, showing the second response. That is
the state the next check (spec 3, Run outcomes) expects to inherit and replace. Any check that needs
the viewer *closed* closes it itself — that is spec 4's problem, not this one's.

### Waits and budgets

No new deadline and no new budget. The check uses the harness's lens-appearance deadline for step 1
and the viewer-update deadline for steps 4, 5, and 7. It is measured against the harness's flat
per-check budget, asserted in the harness's `afterEach`, and its elapsed time prints to the timing
table on every run.

## Testing Decisions

**What makes a good test here.** The fidelity floor is fixed by the map and this spec does not relax
it. A check earns its place only when it observes the channel a person observes:

- CodeLens text in the workbench, not `executeCodeLensProvider`.
- Response viewer content in the webview DOM, not a viewer-update object.
- A real click, on a real lens, in a real editor.

**Seam.** No new seam. The one seam is the packaged `.vsix` driven through ExTester, established by
spec 1. **No test-only seam is added to the shipping extension** — no probe command, no test hook, no
exported handle. If this check cannot see something, the answer is a better DOM assertion, not a hook.

**Module under test.** The product, end to end: `CodeLensProvider`, `RunCommand`, `Companion`,
`ResponseViewer`, the webview entry point, and the renderer core — as one thing, through one `.vsix`.

**Prior art in this repository:**

- `tests/renderer.Tests/` — the assertions this check *escalates*. Those pin what the renderer
draws from a handed-in record. This check pins that a real response reaches it.
- `tests/companion.Tests/` — the F# assertion style the check's source should read like.
- Spec 1's harness self-check — the shape of a check in this suite: hooks, `eventually`, `Assert`.

**Negative verification is required before this is called done.** Run the check once with a
deliberately wrong expected body. Confirm that CI goes red and that the failure names the `.fs`
line. Prototyping produced ten green runs under a shell shape that could not report red. Do not
inherit that.

## Out of Scope

- **Every other product check.** 404 and dead-port renders (spec 3), the loop lens and its toast (spec
  4), cross-block Refused Run (spec 5), Compile Error (spec 6), companion death (spec 7).
- **Changing the harness.** No new route, no new deadline, no new budget, no change to `run.sh` or the
  CI job. If this check needs one, that is a finding to raise, not a change to make quietly.
- **Deleting `docs/manual-check.md` or changing `release.yml`.** Those land with spec 7, when all six
  checks exist. A gate that is green while covering one section of six would be worse than the walk.
- **Asserting the elapsed-time and response-size fields of the status line.** They are real, and they
  vary per run. Pinning them buys nothing and costs a flaky assertion.
- **Screenshot or visual-regression assertions on the viewer.** Not what "retire the manual walk"
  asked for.
- **macOS and Windows.** The suite runs headless on `ubuntu-latest` and nowhere else.

## Further Notes

**Land order.** This is the first of the six product checks. The rest follow: Run outcomes render
honestly, loop lens refuses with toast, cross-block Refused Run, Compile Error names its source,
companion death is visible and recoverable.

**Why this one is first.** It is the check most likely to work, and the only one whose failure
means the product is broken for every user. Every later check also reuses the viewer knowledge it
establishes: how to reach the webview frame, how to read the status line, and what a settled render
looks like.

**What the manual check said, for the record.** This check replaces steps 1–7 of *The core path*.
Steps 4–6 of *Prepare* — install the `.vsix`, reload, open a `.fsx` — are covered by the harness's
setup, by construction. Steps 1–3 of *Prepare* — cut a Beta, open the pre-release, download the `.vsix` — were dropped as
release ceremony. The accepted risk is that a broken Beta workflow can still ship while packaging
and install stay green.
