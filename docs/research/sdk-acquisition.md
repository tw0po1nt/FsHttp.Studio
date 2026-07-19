# Research: how to provide a .NET SDK for the companion's `#r "nuget:"` restore

Issue: [#74](https://github.com/tw0po1nt/FsHttp.Studio/issues/74) — "Research: how to provide a
.NET SDK for the companion's `#r "nuget:"` restore." Part of [#48](https://github.com/tw0po1nt/FsHttp.Studio/issues/48).

> **Scope.** This document gathers facts to inform a decision. It does **not** make the decision —
> that belongs to decision ticket [#75](https://github.com/tw0po1nt/FsHttp.Studio/issues/75).

## Problem statement

The companion runs user `.fsx` scripts through FSI. Scripts pull FsHttp with
`#r "nuget: FsHttp, x.y.z"`. FSI resolves that directive through **`FSharp.DependencyManager.Nuget`**,
which generates an SDK-style project and drives **`dotnet msbuild -restore`** to resolve the package.
The extension currently acquires only a .NET **runtime** (via the .NET Install Tool, `mode: "runtime"`,
per [#57](https://github.com/tw0po1nt/FsHttp.Studio/issues/57)). A runtime carries no `msbuild` and no
SDK targets, so on a clean machine every nuget-referencing Run fails with
`The application 'msbuild' does not exist` / `No .NET SDKs were found` (confirmed live on fresh VSCodium
in [#67](https://github.com/tw0po1nt/FsHttp.Studio/issues/67); pointing the Install Tool at a system SDK
via `existingDotnetPath` makes the flow pass, isolating the defect to runtime-vs-SDK). The maintainer must
pick how an SDK gets onto a clean machine.

Two realistic strategies, examined below against primary sources:

- **(A)** **Acquire a global SDK** on demand via the .NET Install Tool's `dotnet.acquireGlobalSDK`.
- **(B)** **Require a pre-installed system .NET SDK** and guide the user on first run (the Ionide model).

A third, **(C) point at any discovered/user-supplied SDK via `existingDotnetPath`**, is really a *fallback
that both A and B need*, not a standalone strategy — documented in §5.

---

## 1. Does the .NET Install Tool expose SDK acquisition?

**Yes — `dotnet.acquireGlobalSDK` exists**, but it is a *system-level, semi-private* capability, not the
peer of the runtime `dotnet.acquire`.

From the repo's command reference (`dotnet/vscode-dotnet-runtime`, `Documentation/commands.md`) [1]:

- `dotnet.acquire` — installs a **.NET runtime at a user-level folder** (major.minor version). This is
  what [#57](https://github.com/tw0po1nt/FsHttp.Studio/issues/57) already calls.
- `dotnet.acquireGlobalSDK` — "will install a .NET SDK in a **system-level location**." Accepts an
  `IDotnetAcquireContext`, returns an `IDotnetAcquireResult`. Version forms: major (`6`), major.minor
  (`6.0`), feature band (`6.0.4xx`), or fully specified (`6.0.402`). [1]
- `dotnet.acquireGlobalSDKPublic` — a **user-facing** command ("Install the .NET SDK System-Wide" in the
  Command Palette) that pops an input box pre-filled with the recommended version, then calls
  `dotnet.acquireGlobalSDK`. [1]

**Global vs local.** Runtime = **user-level, per-extension**, usable only inside VS Code. Global SDK =
**system-level, machine-wide** (the same install any other tool would see). [1]

**Elevation.** A system-level install inherently needs elevation. The Linux global-install design doc is
explicit: "Each `sudo` command … will require an elevation prompt using `@vscode/sudo`." [2] On Windows a
system-wide .NET install triggers a **UAC** prompt; on macOS the installer needs **admin**. So option (A)
crosses an elevation boundary the runtime path never touches. (Exact Windows/macOS elevation UX inside the
extension is not spelled out in the repo docs — see Open Unknowns.)

**Platform / maturity caveats.** The SDK-acquisition feature is described in the extension README as
**semi-private and limited**: "As of version 2.0.2, you can install the .NET SDK using part of our
**private API** … Note this feature does **not support all distros, WSL, nor preview or RC versions** of
.NET." [3] The Linux design doc confirms initial distro support is narrow (Ubuntu first; RHEL/CentOS
aspirational) and **WSL is explicitly unsupported** ("Our current code will fail with a specific error if
you try to run under WSL"). [2] Crucially, Microsoft's own **extension-author conceptual doc still says the
tool is runtime-only**: "This tool can be used to install the .NET runtime only, it currently does **not**
have the capability to install the .NET SDK." [4] That doc is stale (dated 2020, last touched 2023) and the
repo has since shipped `acquireGlobalSDK`, but the contradiction is the point: **SDK acquisition is not part
of the tool's blessed extension-author contract.** Treat it as "exists, works in the happy path, not
guaranteed stable for programmatic third-party use."

## 2. CRITICAL — is SDK acquisition on BOTH Marketplace AND Open VSX?

**The extension is present on both registries and the SDK command ships in the same package on both** — but
with the maturity asterisk from §1.

- **Open VSX:** `ms-dotnettools/vscode-dotnet-runtime` latest is **v3.1.0**, published **2026-06-12**
  (verified via the Open VSX API `open-vsx.org/api/ms-dotnettools/vscode-dotnet-runtime`). The listing
  publishes the full package — README, `package.json`, and vsix manifest. [5] The runtime `dotnet.acquire`
  was already confirmed on Open VSX (v3.1.0) in [#50](https://github.com/tw0po1nt/FsHttp.Studio/issues/50).
- The **`.vsix` published to Open VSX is the same artifact as Marketplace** (single codebase,
  `dotnet/vscode-dotnet-runtime`). `dotnet.acquireGlobalSDK` / `dotnet.acquireGlobalSDKPublic` are commands
  contributed by that one package, so they are present wherever the package is — i.e. on **both**
  registries — from the version that introduced them (README says v2.0.2 [3]; current is v3.1.0). [1][5]
- **Verdict:** unlike a hypothetical Marketplace-only feature, SDK acquisition is **not** registry-gated.
  Option (A) survives the both-registries requirement. The remaining risk is *maturity* (private API,
  limited distros, no WSL), not *availability*.

> **Open unknown (low risk, worth a live-verify):** I confirmed the v3.1.0 Open VSX package *exists* and is
> the same codebase; I did not unpack that specific Open VSX `.vsix` to eyeball the `contributes.commands`
> list. Recommend a 5-minute unzip-and-grep of the Open VSX artifact before #75 commits to (A).

## 3. Is anything lighter than a full SDK enough for `#r "nuget:"` restore?

**No — a full .NET SDK is effectively required.** This is settled by reading how
`FSharp.DependencyManager.Nuget` actually restores (dotnet/fsharp, `src/FSharp.DependencyManager.Nuget/`):

- It **generates an SDK-style project**: the template opens with `<Project Sdk='Microsoft.NET.Sdk'>`
  (`FSharp.DependencyManager.ProjectFile.fs`). [6]
- It restores by shelling out to **`dotnet msbuild -restore`** against that project, hitting a custom target
  (`FSharp.DependencyManager.Utilities.fs`, `buildProject`):

  ```
  msbuild -v:quiet -restore <binlog?> "<projectPath>" /nologo /t:InteractivePackageManagement
  ```
  executed as `dotnet <args>` via `ProcessStartInfo`. [7]
- The `InteractivePackageManagement` target **depends on SDK-provided targets** —
  `ResolveReferences;ResolveSdkReferences;ResolveTargetingPackAssets;GenerateBuildDependencyFile;…`
  (`FSharp.DependencyManager.ProjectFile.fs`) — and it also runs `dotnet nuget list source` to enumerate
  feeds. [6][7]

Implications:

- `dotnet msbuild` is an **SDK command**; it does not exist in a runtime-only install (hence the observed
  `'msbuild' does not exist`). [7]
- A **restore-only NuGet client** (e.g. `nuget.exe restore`, or NuGet client libraries) would download
  packages but would **not** provide `Microsoft.NET.Sdk`, MSBuild, or the `ResolveReferences` /
  `GenerateBuildDependencyFile` targets the custom target composes over — so it cannot produce the
  `resolvedReferences.paths` FSI consumes. The dependency manager is hard-wired to a real MSBuild + SDK
  targets evaluation, not just a package download. [6]
- **Bottom line:** for the stock `FSharp.DependencyManager.Nuget` path, "lighter than an SDK" is not on the
  table. The only lighter alternative would be *replacing the resolver* (e.g. our own NuGet-client-based
  resolution feeding FSI raw `#r` DLL paths), which is a much larger build, not a packaging tweak. Flag as a
  distinct future option, out of scope for #75's acquire-vs-require call.

## 4. What do peer extensions rely on for SDK presence?

- **Ionide for F# (`Ionide.Ionide-fsharp`) — requires a user-installed SDK (strategy B).** Ionide lists a
  ".NET SDK" as an explicit prerequisite with a link to the download page and does **not** acquire one; it
  discovers `dotnet` via PATH / `DOTNET_ROOT` / `FSharp.dotnetRoot`, and on failure surfaces a "dotnet was
  not found" notification with settings guidance. [8][9] (Detail and failure-tail citations captured in
  `docs/research/runtime-distribution.md` §1.1.)
- **C# extension (`ms-dotnettools.csharp`) — acquires a RUNTIME, not an SDK.** Its Roslyn language server is
  framework-dependent, so it takes an `extensionDependencies` on the .NET Install Tool and calls
  `dotnet.acquire` (**runtime** mode) — the same call we already make. Building/running C# projects still
  needs an SDK, which is user-provided. [10] (See `runtime-distribution.md` §1.2.)
- **C# Dev Kit (`ms-dotnettools.csdevkit`) — Install-Tool dependency for the runtime; SDK is user-provided,
  with `existingDotnetPath` as the reuse hatch.** The C# Dev Kit FAQ documents Install-Tool-driven .NET
  acquisition with a ~4.5-minute timeout and the `dotnetAcquisitionExtension.existingDotnetPath` override
  for reusing an existing install; it does **not** advertise silently installing a full machine-wide SDK for
  your builds. [11]

**Takeaway:** No peer extension auto-acquires a full SDK as its primary path. The Microsoft extensions
auto-acquire a **runtime** for their own framework-dependent language servers (what we already do) and lean
on a **user-provided SDK** for build/restore; Ionide requires a user-provided SDK outright. That is
industry precedent for **option (B)** as the baseline, with `existingDotnetPath` as the shared escape hatch.
Our twist: our *core* Run feature needs restore, so a missing SDK breaks the headline flow, not just an
auxiliary feature — which raises the bar on (B)'s first-run guidance.

## 5. The `existingDotnetPath` escape hatch — exact shape

Confirmed from the Install Tool's README [3] and mirrored by the C# Dev Kit FAQ [11]:

```jsonc
"dotnetAcquisitionExtension.existingDotnetPath": [
    {
        "extensionId": "ms-dotnettools.csharp",
        "path": "C:\\Program Files\\dotnet\\dotnet.exe"
    }
]
```

- It is an **array of `{ extensionId, path }`** objects — the override is **keyed per calling extension**,
  so FsHttp.Studio would need an entry with **our** `extensionId` (`tw0po1nt.fshttp-studio`) pointing at the
  user's `dotnet` (an SDK-bearing one, given §3). [3]
- Related discovery knobs exist (`dotnet.findPath` resolves an existing `dotnet` across VS Code settings /
  shell / PATH / `DOTNET_ROOT` / managed installs / hostfxr [1]), but `existingDotnetPath` is the explicit,
  documented user-facing bypass. Both options (A) and (B) should honor it.

---

## 6. Trade-off matrix

Rows = viable options. `existingDotnetPath` (§5) is assumed present as a fallback for **both** A and B.

| Dimension | **(A) Acquire a global SDK** via `dotnet.acquireGlobalSDK` | **(B) Require a system SDK** + first-run guidance (Ionide model) |
|---|---|---|
| **On VS Code Marketplace** | Yes — command in `ms-dotnettools.vscode-dotnet-runtime` (v2.0.2+; v3.1.0 current) [1][3] | N/A — no extension needed; user installs SDK from dot.net [8] |
| **On Open VSX** | Yes — same package, v3.1.0 (2026-06-12); same `.vsix` as Marketplace (unpack-verify pending) [5] | N/A — registry-independent [8] |
| **Elevation / prompt** | **Yes** — system-level install: sudo/`@vscode/sudo` on Linux, UAC on Windows, admin on macOS [1][2] | Elevation happens **outside** the extension, during the user's own SDK install; extension itself prompts nothing |
| **Install location** | **Global / machine-wide** (system-level) [1] | Global / machine-wide (wherever the user installed it); discovered via PATH/`DOTNET_ROOT`/`existingDotnetPath` [8][5] |
| **Disk & UX cost** | Full SDK (~hundreds of MB–~1 GB) downloaded on first Run; network + elevation gate; ~minutes; timeout/proxy failure modes | Zero download by us; **UX cost is the gap+guide**: first Run fails until the user goes and installs an SDK, then returns — the Ionide "dotnet not found" failure tail [8][9] |
| **Who owns the failure tail** | **Us + the Install Tool.** We own: private-API instability, unsupported distros, **no WSL**, no preview SDKs, elevation refusal, corporate-proxy timeouts. [2][3] | **The user + us.** User owns getting a working SDK on PATH; we own **detecting** it and giving a good, actionable first-run message (detection is the hard part — non-standard paths, WSL, PATH quirks). [8][9] |
| **Recommendation input** | Best *happy-path* (no manual step) and matches "it just works." But it rides a **semi-private, distro-limited, WSL-hostile, elevation-gated** API that Microsoft's own author-facing doc still calls runtime-only [4]. High capability, higher tail risk. | Most **robust and precedented** (Ionide, and effectively the MS extensions for builds). No elevation *we* trigger, no private-API bet, works on WSL/any distro the user's SDK supports. Cost is a real first-run friction moment on our headline feature — mitigable with a good detect-and-guide flow + `existingDotnetPath`. |

### Cross-cutting notes

- **Neither option removes the runtime work.** We still acquire the runtime (or the SDK's bundled runtime
  can serve). An SDK includes a runtime, so option (A) could *replace* the current `mode: "runtime"` call
  rather than sit beside it — worth modeling in #75.
- **A full SDK is non-negotiable for the stock resolver (§3).** "Lighter payload" is only reachable by
  swapping out `FSharp.DependencyManager.Nuget` for a custom NuGet-client resolver — a separate, larger
  initiative, not an acquire-vs-require choice.
- **`existingDotnetPath` is table stakes either way (§5).** Ship it regardless so power users / corporate /
  offline / WSL users can point at their own SDK.

## 7. Open unknowns (flag for #75 / live-verify)

1. **Open VSX artifact parity (low risk):** confirmed v3.1.0 exists and is the same codebase; have **not**
   unzipped the Open VSX `.vsix` to grep `contributes.commands` for `acquireGlobalSDK`. Quick to verify.
2. **Windows/macOS elevation UX (medium):** repo docs detail the **Linux** elevation mechanism; the exact
   in-extension UAC (Windows) / admin-pkg (macOS) prompt flow for `acquireGlobalSDK` is not documented —
   needs a live run on each OS.
3. **Programmatic third-party call (medium):** `acquireGlobalSDK` is described as "private API" [3] and the
   author doc says runtime-only [4]. Whether Microsoft supports a *third-party* extension invoking
   `dotnet.acquireGlobalSDK` via `executeCommand` (vs. only their own palette command
   `acquireGlobalSDKPublic`) should be confirmed against the sample/API before betting on (A).
4. **SDK size on first Run (low):** exact download size for the pinned `10.0` SDK band on each OS (order of
   ~200 MB–1 GB) affects the first-Run UX estimate.

---

## Sources

1. .NET Install Tool — command reference (`dotnet.acquire`, `dotnet.acquireGlobalSDK`,
   `dotnet.acquireGlobalSDKPublic`, `dotnet.findPath`, system-level vs user-level, version forms) —
   <https://github.com/dotnet/vscode-dotnet-runtime/blob/main/Documentation/commands.md>
2. .NET Install Tool — Linux global-install design (elevation via `@vscode/sudo`, distro support, WSL
   unsupported) —
   <https://github.com/dotnet/vscode-dotnet-runtime/blob/main/Documentation/global-installs/linux-global-install-design.md>
3. .NET Install Tool — extension README (`existingDotnetPath` shape; "Install the .NET SDK System-Wide";
   SDK install as "part of our private API" since v2.0.2; "does not support all distros, WSL, nor preview or
   RC versions") —
   <https://github.com/dotnet/vscode-dotnet-runtime/blob/main/vscode-dotnet-runtime-extension/README.md>
4. Microsoft Learn — ".NET install tool for VS Code extension authors" ("can be used to install the .NET
   runtime only … does not have the capability to install the .NET SDK") —
   <https://learn.microsoft.com/en-us/dotnet/core/additional-tools/vscode-dotnet-runtime>
5. Open VSX Registry API — `ms-dotnettools/vscode-dotnet-runtime` (latest v3.1.0, published 2026-06-12) —
   <https://open-vsx.org/api/ms-dotnettools/vscode-dotnet-runtime> (listing:
   <https://open-vsx.org/extension/ms-dotnettools/vscode-dotnet-runtime>)
6. dotnet/fsharp — `FSharp.DependencyManager.ProjectFile.fs` (generated `<Project Sdk='Microsoft.NET.Sdk'>`;
   `InteractivePackageManagement` target depends on SDK targets `ResolveReferences;ResolveSdkReferences;…`)
   — <https://github.com/dotnet/fsharp/blob/main/src/FSharp.DependencyManager.Nuget/FSharp.DependencyManager.ProjectFile.fs>
7. dotnet/fsharp — `FSharp.DependencyManager.Utilities.fs` (`buildProject`: shells out to
   `dotnet msbuild -v:quiet -restore … /t:InteractivePackageManagement` via `ProcessStartInfo`;
   `dotnet nuget list source`) —
   <https://github.com/dotnet/fsharp/blob/main/src/FSharp.DependencyManager.Nuget/FSharp.DependencyManager.Utilities.fs>
8. Ionide for F# — getting started / prerequisites (.NET SDK prerequisite, user-installed) —
   <https://ionide.io/Editors/Code/getting_started.html>
9. Ionide for F# — "dotnet was not found" failure UX / detection tail —
   <https://github.com/ionide/ionide-vscode-fsharp/issues/1712>
10. vscode-csharp — README (".NET Install Tool will be installed as a dependency"; framework-dependent
    Roslyn LS acquires the runtime) — <https://github.com/dotnet/vscode-csharp/blob/main/README.md>
11. C# Dev Kit FAQ — Install-Tool-driven .NET acquisition, ~4.5-min timeout, `existingDotnetPath` reuse
    hatch — <https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq>

_Companion doc: `docs/research/runtime-distribution.md` (issue #49) covers the runtime-distribution decision
and holds the fuller Ionide / C# extension citations referenced above._
