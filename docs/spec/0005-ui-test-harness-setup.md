# The UI test suite harness and its setup

Spec 1 of 7 for the UI test suite that retires `docs/manual-check.md`. This one builds no product
check. It builds the ground the other six stand on, and proves that ground is live.

Decisions come from a wayfinder map held locally (`.local/wayfinder/ui-tests/`, gitignored). The
map is not a GitHub issue, so this spec restates every decision it depends on rather than linking
to one.

## Problem Statement

Today a person is the release gate. Before a release, someone cuts a Beta pre-release, installs the
`.vsix` by hand, walks every section of `docs/manual-check.md` in a real editor, and comments on the
pre-release to say it passed. ADR-0008 makes that comment the gate.

That is slow, it is unrepeatable, and it does not scale past one maintainer. Worse, it is the only
check that observes the surfaces a user actually touches. The three existing suites
(`companion.Tests`, `host.Tests`, `renderer.Tests`) and `npm run smoke` test provider return values,
envelopes, and viewer-update objects. None of them sees CodeLens text in a workbench, a warning
toast, or the response viewer's rendered DOM. So the walk cannot simply be deleted — something has
to observe the same channels a person does.

Six automated checks will do that. No one of them can be written first, because all six need the
same ground:

- A packaged extension installed into a real VSCode.
- A driven UI.
- A local HTTP server.
- A way to wait for product state without sleeping.
- A way to fail legibly when any of that ground is sick.

This spec builds that ground. Nothing here observes a product surface, so nothing here can be
verified by a product check. It therefore also ships the one test that proves the ground itself is
live.

## Solution

A new UI test suite at `tests/ui.Tests/`, authored in F#, Fable-compiled, and driven by
`vscode-extension-tester` (ExTester) against the **packaged `.vsix`** installed into a **pinned**
VSCode, headless on `ubuntu-latest`.

After this spec lands:

- `npm run ui-tests` (and `tests/ui.Tests/run.sh`) builds and runs the suite locally on macOS and
  Linux, driving the same VSCode version CI drives.
- A CI job runs the suite on every pull request that touches the relevant surfaces, on `main`, and
  on release.
- The suite contains exactly one test — the **setup self-check** — which asserts that the harness
  reached a proven-live workbench and that the timing table printed. It touches no product surface.
- A weekly workflow opens a pull request when the pinned VSCode version falls behind stable.
- `docs/release-gate.md` exists and states, honestly, what the suite does and does not cover.

`docs/manual-check.md` is **not** deleted here, and `release.yml` is **not** changed here. A gate
that is green while covering nothing is worse than the walk it replaced. Those land with spec 7.

## User Stories

1. As a maintainer, I want the test suite to drive the packaged `.vsix` rather than a source
   extension, so that the artifact I actually ship is the artifact under test.
2. As a maintainer, I want the suite to install that `.vsix` into a real VSCode, so that the
   Prepare steps of the manual check are covered by construction rather than by a checklist.
3. As a maintainer, I want the suite to run headless on `ubuntu-latest`, so that no human machine
   and no human attention is on the critical path.
4. As a maintainer, I want one CI entry point for the suite, so that there is no ambiguity about
   which job is the gate.
5. As a contributor, I want a single command that runs the suite locally, so that I can reproduce
   a CI failure without reconstructing the job.
6. As a contributor, I want my local run to drive the same VSCode version CI drives, so that a
   green local run is evidence about the red CI run and not a different experiment.
7. As a contributor, I want the suite authored in F#, so that it reads like every other test in the
   repository and I do not context-switch languages to add a check.
8. As a contributor, I want assertion failures to name the `.fs` file and line that failed, so that
   I can find the failing assertion without bisecting a bundle.
9. As a contributor, I want a failing check to leave a screenshot behind, so that I can see what the
   editor looked like at the moment of failure.
10. As a check author, I want one sanctioned way to wait for product state, so that no check invents
    its own polling and no check contains a fixed sleep.
11. As a check author, I want named default deadlines per surface, so that I do not invent a
    timeout when I write a new check.
12. As a check author, I want a local HTTP server with a documented route table, so that I can write
    a fixture without reading the server's source.
13. As a check author, I want the server's base URL and a guaranteed-dead port delivered to my
    fixture, so that a fixture never hardcodes a port.
14. As a check author, I want a hang route I can release over HTTP, so that I can test a Run in
    flight without a fixed sleep.
15. As a check author, I want to know a slow request has actually arrived at the server, so that I
    can distinguish killing the companion mid-flight from killing it before the request left.
16. As a check author, I want the hang route to be reusable within one run, so that a second hanging
    request does not silently return at once.
17. As a maintainer, I want setup to fail fast and legibly when VSCode never comes up, so that the
    job does not burn to its timeout with no message.
18. As a maintainer, I want setup to fail when the sidecar is missing, unparseable, or stale, so
    that a check never runs against a dead port and reports a product defect.
19. As a maintainer, I want setup to fail when the "dead" port is actually live, so that a
    connection-refused assertion cannot pass for the wrong reason.
20. As a maintainer, I want setup to end at a *proven-live* workbench rather than a visible one, so
    that the first check starts from the same state as the last.
21. As a maintainer, I want a per-check time budget, so that a check that hangs is killed rather
    than running to the job timeout.
22. As a maintainer, I want a suite time budget below the sum of the per-check budgets, so that
    creeping slowness is caught before every check has crept to its own ceiling.
23. As a maintainer, I want a budget failure to name the check, the budget, and the observed
    elapsed time, so that I can act on it without reading the run's raw log.
24. As a maintainer, I want the timing table printed to the job summary on every run, so that drift
    is readable before a budget fires.
25. As a maintainer, I want the suite's red to actually redden the job, so that a green badge is
    evidence.
26. As a maintainer, I want the VSCode version pinned in one checked-in place, so that the gate is a
    reproducible record rather than a function of the day it ran.
27. As a maintainer, I want ChromeDriver derived from that pin rather than pinned separately, so
    that the two cannot drift apart.
28. As a maintainer, I want the CI cache keyed on the pin, so that a stale cache cannot serve the
    wrong VSCode.
29. As a maintainer, I want a weekly pull request when the pin falls behind stable, so that the
    pin's roughly three-month useful life is a schedule rather than a surprise.
30. As a maintainer, I want that pull request's own CI to be the gate run for the new version, so
    that merging it is the whole bump.
31. As a maintainer, I want an open pin-update pull request to be a stated release prerequisite, so
    that a release does not ship on a known-stale toolchain.
32. As a maintainer, I want the suite's honest gaps written down in `docs/release-gate.md`, so that
    what the automation does not cover is visible rather than assumed.
33. As a maintainer, I want to know that the declared `engines.vscode` floor is tested by nothing,
    so that I do not believe the suite covers it.
34. As a maintainer, I want the test server built as its own CI step, so that build time is not
    charged against the harness setup budget.
35. As a maintainer, I want the test server's publish output outside `dist/`, so that a test binary
    can never be packaged into the released `.vsix`.
36. As a maintainer, I want both new `.fsproj` files in `FsHttp.Studio.slnx`, so that the repository
    compiler gate type-checks the suite and the server.
37. As a maintainer, I want the suite to leave no orphaned processes after a local Ctrl-C, so that a
    second run is not poisoned by the first.
38. As a maintainer, I want the harness to have a test of its own, so that spec 1 can land and be
    verified without waiting for spec 2.
39. As a maintainer, I want a green self-check and a red product check to be an unambiguous signal,
    so that I know the harness is not the suspect.
40. As a maintainer, I want the existing suites left alone, so that this adds a layer rather than
    relitigating what belongs where.

## Implementation Decisions

### Layout

A new suite directory, `tests/ui.Tests/`, matching the three sibling suites:

- A Fable test project at the suite root, targeting `netstandard2.0`, referencing `Fable.Core`,
  `Fable.Mocha`, and the repository's pinned `FSharp.Core`.
- A `server/` subdirectory holding a `net10.0` executable project — the test HTTP server.
- A `fixtures/` directory. Fixture `.fsx` scripts are checked in; the generated `sidecar.json`
  beside them is gitignored.

**Both projects are added to `FsHttp.Studio.slnx`.** The solution already carries Fable-only
projects, so `dotnet build FsHttp.Studio.slnx` — the repository's real compiler gate — then covers
the suite and the server. Neither was type-checked by the repository build during prototyping.

One `.fsproj` per directory, so pointing Fable at the suite root is unambiguous.

### Harness composition

The harness is a module of the suite, not a per-check concern. It owns:

- ExTester bindings (roughly 45 members over about nine page objects).
- The assertion module, the `eventually` combinator, and the named deadline constants.
- The `before` hook, the `afterEach` / `after` budget asserts, and the timing table.

Checks import it. A check must not define its own wait primitive, its own budget assert, or its own
deadline constant.

### Assertions and stack traces

Fable-compiled F# exceptions carry **no stack**, by design in `fable-library-js`. So `failwith` and
every `Fable.Mocha` `Expect.*` failure print a message and no location.

The suite therefore ships **its own thin assertion module over a short `Emit` shim** that throws a
real JS `Error`, from its first test. Retrofitting later means touching every assertion ever
written.

Two constraints, both found the hard way in prototyping, both non-obvious:

- **The module must not be named `Expect`.** `open Fable.Mocha` shadows it, and the shim silently
  becomes dead code — which is what happened in prototyping, undetected, across two prototypes.
  Name it `Assert`.
- **`NODE_OPTIONS=--enable-source-maps` must be set.** ExTester builds Mocha in-process, so
  `.mocharc.js`'s `node-option` is ignored. Without the flag, a bundle offset is printed instead of
  an `.fs` line.

Both are required for source maps to reach `.fs` lines. Either one missing defeats the other.

### Build and run

Fable compiles the suite to a third output target, and esbuild bundles it to a **single CJS
file** — the same shape `smoke.mjs` and `esbuild.mjs` already establish, with `format: "cjs"` and
`external: ["vscode"]`. CJS is forced: Fable's `import … from "vscode"` only resolves through
esbuild's `external`, and every Mocha-driven harness loads files synchronously.

Mocha runs in **`ui: 'bdd'`**. This is one config word and it is mandatory — Fable.Mocha calls the
global `describe`/`it`, and under the `tdd` default the suite dies with `describe is not defined`.
ExTester needs no change here, since Mocha's own default is `bdd`.

A `run.sh` in the suite directory is the single local entry point. It builds the server, starts it,
packages the `.vsix`, builds the suite, downloads VSCode and ChromeDriver, installs the `.vsix`, and
runs the tests. CI calls the same script.

Four sharp edges `run.sh` must handle, all found in prototyping:

- **`--open_resource` is variadic.** The test glob must come *before* `-r`, or the glob is
  swallowed.
- **macOS IPC socket path length.** VSCode's `*.sock` path exceeds the ~104-character limit under a
  deep storage tree. Use a short storage path under `/tmp`.
- **Stale settings.** Remove the storage `settings` directory before each run.
- **Cleanup trap.** Kill the server pid and any leftover companion process on exit, so a local
  Ctrl-C leaves nothing behind.

### The test HTTP server

**The UI suite owns its own server. `companion.Tests` keeps `TestServer.fs` untouched.** No shared
project, no extract.

The earlier decision said "shares the handlers in `TestServer.fs` (or an extract of them)", and
rejected a Node rewrite for duplication. That argument was aimed at a *second language* — second
runtime, second dependency set, drift in what a status code means. A second .NET file is a different
animal, and the two route surfaces do not overlap:

- `companion.Tests` builds a fresh route map per test, in process, on a **single-threaded** listener
  loop. That single thread is load-bearing: `countingHandler` mutates a `ref` and relies on it.
- The UI server has a **fixed** route table, needs **thread-pool dispatch per request**, needs two
  control endpoints that have no in-process meaning, and writes a sidecar.

Common code is roughly 25 lines. A shared library over two different concurrency models is the shape
most likely to be wrong on day one.

**Route table — this is a cross-process contract, and nothing compiles it:**

| Route | Behavior |
|---|---|
| `GET /json` | 200 `application/json`, a stable probe body |
| `GET /notfound` | 404, a body distinguishable from `/json` |
| `GET /slow` | Blocks until the release generation advances, then 200 |
| `GET /release` | Increments the release generation, 200 |
| `GET /status` | 200 `{"slowSeen":N,"slowWaiting":M}` |
| anything else | 404 |

`/notfound` is a **named route**, not the catch-all. A later check needs a 404 body distinguishable
from `/json`, and the catch-all satisfied that only by accident. Naming it frees the catch-all to
mean "the fixture has a typo" — a different failure, which a check must not read as a pass.

`/status` is the **arrival tell**. It is what separates killing the companion during a Run in flight
from killing it before the request ever left.

**Thread-pool dispatch per request is mandatory, not stylistic.** Prototyping measured the failure
mode: without it, the kill lands before the request leaves and the release deadlocks behind the hang.

**The release mechanism is a generation counter, not a latch.** The prototype used a sticky
`ManualResetEventSlim`: once set, never reset, so every later `/slow` returned at once. A check
whose hang silently does not hang is a bad failure. `/slow` reads the generation on arrival and
waits until it advances; `/release` increments it. About eight lines more than a latch.

`companion.Tests` does **not** move to the HTTP form. Its `hangingHandler` has one call site and
releases through a locally-scoped `ManualResetEventSlim`. Replacing that with a second process, a
port, and a network round trip would be strictly worse.

### The sidecar

The server writes a sidecar file beside the fixtures at startup:

```json
{ "baseUrl": "http://127.0.0.1:<port>", "deadUrl": "http://127.0.0.1:<deadPort>" }
```

Both ports are ephemeral loopback. The dead port is obtained by bind-then-close.

Each fixture reads the sidecar with `Path.Combine(__SOURCE_DIRECTORY__, …)`. The editor opens the
real repository fixture, not a generated copy. A file is used rather than an environment variable
because the companion is a child of the extension host and environment inheritance through Electron
is easy to get wrong.

**This depends on #144** (`__SOURCE_DIRECTORY__` resolving to the script's own directory). The
harness spec itself does not read the sidecar from a fixture, so **this spec is not blocked by
#144** — but three later specs are.

### Setup, and what "live" means

Setup is the Mocha `before` hook. It ends at a **proven-live** workbench, not a visible one. All
four tells must hold:

1. `waitForWorkbench` returned — which on its own is *not* a readiness tell.
2. The test server answers its healthcheck and the sidecar parses.
3. The fixture folder is open and the extension activated.
4. A companion process exists.

The fourth tell is what makes a single flat per-check budget honest: the first check then starts
from the same state as the sixth. It also correctly attributes a slow first companion spawn to
setup rather than to a check.

Setup **fails fast, before any Run**, when the sidecar is missing, does not parse, fails its
healthcheck, or when the dead port answers. The message names the cause. The sidecar is deleted
before the server starts, so a stale file cannot be mistaken for a live one.

**The harness owns the dead-port probe**, not the server. The server allocates the port and reports
it. The server does not self-police. One gate with one error vocabulary means "dead port was live" reads in
the same voice as "sidecar is stale".

### Waiting

**`eventually` is the only sanctioned assertion wait.** Every product assertion that needs time goes
through it. No second polling combinator. No `driver.wait` for product state from a check.

*Amended after spec 0006.* The harness also supplies `eventuallyObserved`. Its poll returns what the
poll saw, and not a bare `bool`. A timeout then names the state that the last poll read. An
exact-count assertion needs this, because too few and too many both time out on the same message.
`eventuallyObserved` is not a second combinator. It holds the one polling loop, and `eventually`
calls it. A check that has nothing to report continues to use `eventually`.

Named default deadlines, as constants in the harness module:

| Surface | Default |
|---|---|
| Lens appearance | 45 s |
| Viewer update | 30 s |
| Toast | 15 s |
| Post-reload recovery | 60 s |

A check can pass a shorter override. A longer deadline needs a named constant and a one-line reason.
No magic number in a check body.

**Fixed `sleep` is banned in check bodies. There is no escape hatch.** The poll interval inside
`eventually` is retry spacing, not a sleep. ExTester's own timeouts (`waitForWorkbench`,
`switchToFrame`) remain allowed in harness setup — they are not product assertions.

**Positive tell before every absence assertion.** Before a check asserts something is missing, it
must first observe a positive tell that the prior step finished. Absence at a fixed time is not
meaningful.

**Every click on a lens must find-and-click inside one retry.** Prototyping attributed a flake to a
warm session. The true cause was a missing retry.

### Budgets

Budgets are **green-path**, all of them. A budget measures what a passing run costs. A deadline is a
ceiling on a single wait and only fully elapses on a run that is already failing. They measure
different runs — so the 60-second post-reload deadline does not need to fit inside the 45-second
per-check budget, and no reconciliation is needed.

| Budget | Span | Value |
|---|---|---|
| Job timeout | GitHub Actions hard kill | 20 min |
| Harness setup | The `before` hook, ending at proven-live | 180 s |
| Per-check | First action to last assertion, flat across all checks | 45 s |
| Suite | First check's first action to last check's last assertion, setup excluded | 240 s |

A run that is **slow but passing** busts its budget and reddens. That is the intent — it is the only
signal that the suite is drifting toward the job timeout.

Where the numbers come from:

- **45 s** is 2.2× the observed worst case (20.1 s) of the dearest check. Flat, not per-check: six
  bespoke numbers would mean five invented ones, and invented numbers get read as measurements. It
  is a backstop against a hung check, not a drift detector.
- **180 s** is roughly an order of magnitude over the inferred value. It is a **hang-catcher, not a
  measurement**: setup is the noisiest step in the job, and a tight threshold there would be the
  flakiest assertion in the suite while protecting nothing. **The implementation session records the
  observed setup time on the first green run and can tighten it. A tighter value is not necessary to
  ship.**
- **240 s** is ~3× the observed green path and deliberately **below** the sum of the per-check
  budgets (6 × 45 = 270 s), so the suite fires before every check has crept to its own ceiling. This
  is the drift detector.

Setup measures the `before` hook and nothing earlier. `npm install`, `dotnet publish`, Fable,
esbuild, `get-vscode`, `get-chromedriver`, and `install-vsix` all finish before the suite process
exists. They are bounded by the job timeout and nothing else.

**Enforcement — two mechanisms that never share a number:**

| Mechanism | Value | Fires when |
|---|---|---|
| Mocha per-test timeout | 120 s | Something is wedged |
| Mocha `before` timeout | 300 s | The workbench never came up |
| Per-check budget assert | 45 s | Green path drifted |
| Setup budget assert | 180 s | Setup drifted |
| Suite budget assert | 240 s | Suite drifted |

Budget asserts live in the harness `afterEach` / `after` hooks, never in a check body, so no check
can forget one or invent its own. The Mocha timeouts sit above every budget and above the largest
deadline. A slow-but-passing run therefore dies on a budget assert with a legible message, not on a
timeout with an illegible one. A budget message names the check, the budget, the observed elapsed
time, and the word *budget*.

**Stated so it is not hidden: when a check truly hangs, the Mocha timeout fires first and the budget
assert never runs.** Hang and drift are different failures with different messages. That is the
design, not a gap.

### The VSCode pin

**One pinned version, in `extester.config.json` in the suite directory**, field
`setup.vscodeVersion`. It is the single source of truth for the VSCode version, the ChromeDriver
version, and the CI cache key.

**This spec names no version number.** The implementation session pins whatever stable VSCode is
current when the harness lands. A number chosen now would be stale before a reader opens the spec.

- ExTester loads `extester.config.json` by walking up from `cwd`, and honors `setup.vscodeVersion`
  in `get-vscode`, `get-chromedriver`, and `run-tests` alike. CLI flags win over it, so existing
  plumbing is untouched and **`run.sh` passes no `-c`**. Local and CI therefore drive identical
  VSCode by construction.
- The config file lives in the **suite directory, not the repository root** — `find-up` from an
  unrelated `cwd` would otherwise pick up a root config.
- **ChromeDriver is not pinned separately.** ExTester derives the driver version from the resolved
  VSCode version.
- A `CODE_VERSION` environment variable in the workflow was rejected. The version would then live in
  CI only. A local run would float while CI pinned, so the local reproduction of a red CI test could
  differ from the run that went red.

A float was rejected outright — the gate's job is to be a reproducible record, and a float means an
upstream release changes the gate on a day nobody touched the repository.

### CI

One job, one entry point, on `ubuntu-latest`. Steps in order: checkout, .NET and Node setup, apt
dependencies for ExTester, the AppArmor adjustment, `dotnet tool restore`, `npm ci`, package the
`.vsix`, **build the test server**, then run the suite. Job timeout 20 minutes; **job retries ×3**;
**no Mocha per-check retries**.

Three CI-specific requirements:

- **Ubuntu 24 AppArmor.** ExTester's `openResources` is a no-op until
  `kernel.apparmor_restrict_unprivileged_userns=0`. Without it, CI opens an empty welcome page and
  the lens never appears. The older `unprivileged_userns_clone` knob is **not** enough. This is
  documented in ExTester's own KNOWN_ISSUES and is the single ugliest environmental dependency in
  the suite.
- **`shell: bash`, never `./run.sh | tee` under `bash -e`.** Piping the script into `tee` reports a
  red suite as a green job. Prototyping ran ten green attempts under exactly that shape before
  discovering the green was never falsifiable. Every budget assert and every check in this spec
  depends on a red suite reddening the job.
- **Cache VSCode and ChromeDriver binaries** under key `${{ runner.os }}-extest-<pin>`, reading the
  pin out of the config file. **Binaries only — never the storage settings directory**, because
  `run.sh` deliberately removes it and a cached VSCode profile would carry state between runs. The
  pin being the cache key is the safety property: a different pin is a different key and misses, so
  a stale cache cannot serve the wrong VSCode.

Failure screenshots and logs upload as artifacts on failure. ExTester's runner already installs an
`afterEach` that screenshots every non-passing test.

**The test server is built by its own CI step**, publishing to a path under `out/`, and `run.sh`
calls the same script locally. The `before` hook does **not** build — it spawns the published
executable and waits for it to report. Only the spawn counts against the 180-second setup budget.

A missing binary fails setup at once with a message naming the build command. Setup never builds
silently. Accepted cost: someone who edits the server and bypasses `run.sh` gets a stale server;
`run.sh` always rebuilds, and an incremental publish is cheap.

**Publish output goes under `out/`, never `dist/`.** `.vscodeignore` excludes `out/**` outright but
*ships* `dist/` minus named files — so publishing the test server to `dist/` would package a test
binary into the released `.vsix`.

### The weekly pin-update pull request

ExTester supports only the three most recent VSCode minor releases, and VSCode ships monthly, so a
pin's useful life is about three months. A pin nobody bumps is not a risk but a schedule.

A weekly workflow resolves the latest stable VSCode. If it differs from the pin, it pushes a branch
with the pin updated and opens a pull request. **That pull request's ordinary CI is the suite run
against the new version**. A green run means that merging the pull request is the whole update. A
red run leaves a titled, reproducible artifact with logs and screenshots, not a stale failure email.

- Weekly rather than monthly: a weekly no-op is free, and a patch release that breaks ChromeDriver
  should not wait three weeks to surface.
- Use `gh pr create` with the built-in `GITHUB_TOKEN`. **No third-party action** — the repository
  just finished a supply-chain hardening pass and this adds no new SHA-pinned dependency. Scope
  `contents: write` and `pull-requests: write` to that one workflow.
- This shape was chosen over a red scheduled run for two reasons. A detector alone leaves the update
  step unnamed. And the ×3 job retry means a genuinely flaky new version can come up green on a
  schedule and teach nobody anything.

Mechanizing the prerequisite inside `release.yml` was rejected. It puts a network call to a third
party in the release path. It also invents a fresh way for a release to fail for reasons unrelated
to the product.

### `docs/release-gate.md`

Created by this spec. It states what the suite covers and — the point of the document — what it does
not. It carries the two paragraphs and the one prerequisite below **verbatim**. That text has
already had a Simplified Technical English pass. Do not paraphrase it.

> **The suite tests one VSCode version.**
> The UI suite drives the VSCode version that `extester.config.json` pins, and it runs on Linux
> only. `package.json` states `"engines": { "vscode": "^1.66.0" }`. The suite does not test that
> minimum version, because ExTester cannot drive it. No other check tests it either — the repository
> has no `@types/vscode` dependency, so no tool checks the declared minimum. A release can ship a
> defect that occurs only on a VSCode version older than the pin. The manual check that this suite
> replaced had the same gap.
>
> **The pin becomes stale.**
> ExTester supports the three most recent VSCode minor releases, so the pin is useful for about
> three months. A weekly workflow opens a pull request that updates the pin. The CI run on that pull
> request is the gate run for the new version. If a pin is outside the ExTester support window,
> ExTester can fail to download the matching ChromeDriver, and the gate then fails.

The trailing requirement belongs in the document's prerequisites list, not as trailing prose:

> Before you publish a release, merge or close each open pin-update pull request.

The document must also record the remaining honest gaps, which the later specs will add to:
**Linux only** — a defect that appears only in `dotnet` discovery or companion process handling on
macOS or Windows ships uncaught. That is a known and accepted cost.

### Recorded softness

Three weaknesses, written down rather than hidden. None blocks the work.

1. **Nothing compile-checks that the two test servers agree.** The route table above is the only
   contract. The prototypes already drifted — two prototype servers returned different `/json`
   bodies.
2. **The ×3 job retry launders exactly what the budgets are for.** A check that drifts to 50 s busts
   its budget on attempt 1, comes in at 43 s on attempt 2, and the job is green. Making budget
   failures non-retryable is not worth the plumbing — the retry is a workflow-level construct that
   cannot see why the suite failed. Mitigation is **visibility, not a gate**: the suite prints its
   timing table — setup, each check, the suite total, each against its budget — to the GitHub Actions
   job summary on **every** run, green or red. This relies on someone looking, which is weaker than
   a gate.
3. **The redesign trigger is a policy, not a mechanism.** If the suite produces two or more final
   failures in ten consecutive runs, the design is reconsidered. Note that "drop the reload step
   first" is *not* the opening move — the reload was measured and is the cheap part.

## Testing Decisions

**What makes a good test here.** A check earns its place only when it observes the same channel a
person would. That fidelity floor is fixed and this spec does not relax it:

- CodeLens text in the workbench, not `executeCodeLensProvider` alone.
- Toast text in the notification UI, not a `showWarningMessage` call site.
- Response viewer content in the webview DOM, not a `postMessage` payload.
- Real clicks, edits, and reloads.

Provider return values, envelopes, and viewer-update objects stay in `host.Tests`,
`companion.Tests`, and `renderer.Tests`. They are good tests; they simply do not retire the manual
walk.

**Seams.** One new seam: the packaged `.vsix` driven through ExTester. **No test-only seams in the
shipping extension** — no probe command, no test hook. If a future need genuinely requires `vscode`
API access from outside the host, it is a separate test-hook extension, not a change to
FsHttp.Studio.

**What this spec tests.** One test: the **setup self-check**. It asserts that the `before` hook
reached a proven-live workbench — all four tells — and that the timing table printed to the job
summary. It touches no product surface.

This exists so spec 1 can land and be verified independently of spec 2. It also exercises the budget
asserts and the timing table before any product check depends on them. And it makes later failures
unambiguous. If the self-check is green and a product check is red, the harness is not the suspect.

**Negative verification is required before this is called done.** Prototyping produced ten green
runs under a shell shape that could not report red. The implementation session must demonstrate at
least one deliberately failing run that reddens the job — this is the acceptance criterion for the
`shell: bash` requirement, and it cannot be satisfied by inspection.

**Prior art in this repository:**

- `smoke.mjs` — the existing pattern of a JS-side guard that runs Fable output for real.
- `tests/companion.Tests/TestServer.fs` — an in-process HTTP server for an existing suite; the
  vocabulary the new server mirrors, deliberately without sharing code.
- `esbuild.mjs` — the `format: "cjs"` + `external: ["vscode"]` bundling shape the suite reuses.
- `tests/companion.Tests/` generally — the Expecto-shaped assertion style the suite's F# should
  read like.

## Out of Scope

- **Any product check.** The six checks are specs 2 through 7. This spec builds only the ground and
  the self-check.
- **Changing `release.yml`, deleting `docs/manual-check.md`, and superseding ADR-0008.** These land
  with spec 7 (companion death), because only when all six checks exist does the suite actually
  replace the walk. Making the gate green while it covers nothing would be worse than the human walk.
- **macOS and Windows coverage.** The suite runs headless on `ubuntu-latest` and nowhere else. A
  defect that appears only in `dotnet` discovery or companion process handling on those platforms
  ships uncaught. That is the accepted trade for retiring the walk.
- **Testing the `engines.vscode` floor.** ExTester cannot drive 1.66. A matrix over the oldest
  supported version would double the suite's cost against every budget. It would prove roughly three
  months of history against a floor that claims years, and the gap survives it intact. Whether
  `^1.66.0` is still a floor the project means is a product question, not part of this work.
- **Extracting a shared test server project.** Decided against. See Implementation Decisions.
- **Moving `companion.Tests` to the HTTP hang route.** It keeps its in-process form.
- **Replacing the existing suites.** `companion.Tests`, `host.Tests`, `renderer.Tests`, and
  `npm run smoke` keep their jobs. This adds the layer above them.
- **Screenshot and visual-regression testing, performance assertions, and accessibility checks.**
  All plausible, none of them what "retire the manual walk" asked for.
- **Fixing production source maps.** Neither existing Fable invocation passes `--sourceMaps`, so
  `dist/extension.js.map` maps to `out/*.js` rather than to `.fs`. A real and small win, gated on
  nothing here, and it should be its own issue rather than smuggled into a UI-test spec.

## Further Notes

**Land order.** This is spec 1 of 7. The other six land in this order:

1. The core path.
2. Run outcomes render honestly.
3. Loop lens refuses with toast.
4. Cross-block Refused Run.
5. Compile Error names its source.
6. Companion death is visible and recoverable.

**Dependency on #144.** This spec is **not** blocked — the harness does not read the sidecar from a
fixture. Three later specs are blocked: the core path, Run outcomes, and companion death.

**Prototype evidence.** Two throwaway prototypes established this shape on `ubuntu-latest` and on
Apple Silicon. The measured numbers:

| Measure | Observed |
|---|---|
| Cold full run | 34–69 s |
| Warm full run | 14–20 s |
| Dearest check (companion death, with a window reload) | 14–20 s, median 17.4 s |
| Death to stopped message | 0.1 s |
| Death to recovered Run | ~10.4 s median |
| Post-reload worst case | 12.9 s, against a 60 s deadline |

The prototypes live outside the repository. Do not lift them wholesale —
several of their shapes are explicitly corrected above.

**One open ergonomics question, deliberately unresolved.** Whether a *failing* Linux CI test is
cheap to reproduce on macOS is not fully answered. The pin removes ChromeDriver drift between a
laptop and the runner by construction, but the display and the AppArmor-only Linux failure mode
remain. This is not a blocker. It is a known rough edge to revisit when the suite has real failures
to reproduce.

