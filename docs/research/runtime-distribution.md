# Research: Shipping the .NET runtime dependency for FsHttp.Studio

Issue: [#49](https://github.com/tw0po1nt/FsHttp.Studio/issues/49) — "Research: how peer
.NET/F# VS Code extensions ship their .NET runtime dependency."

> **Scope.** This document gathers facts to inform a distribution decision. It does **not**
> make the decision — that belongs to a separate human decision ticket.

## Problem statement

The FsHttp.Studio companion is a **framework-dependent** .NET 10 process, built with
`dotnet publish -c Release` (no RID, not self-contained). Per `global.json` the repo pins SDK
`10.0.200`. Because the publish output carries no runtime, the extension only works if the user
**already** has a compatible .NET 10 runtime on the machine; otherwise the companion fails to
launch and the extension does nothing. The maintainer must pick how the runtime gets onto the
user's machine.

There are three realistic strategies, examined below against how peer extensions solve the same
problem:

- **(a)** Keep framework-dependent + document the prerequisite / guide the user on first run.
- **(b)** Ship a **self-contained**, per-platform `.vsix` (the runtime is bundled).
- **(c)** **Acquire on demand** via the `ms-dotnettools.vscode-dotnet-runtime` (.NET Install
  Tool) extension.

---

## 1. How peer extensions handle it

### 1.1 Ionide for F# (`Ionide.Ionide-fsharp`) — strategy (a)

- Ionide **requires a user-installed .NET SDK** and states so as an explicit prerequisite. Its
  README/docs list ".NET 8.0/9.0 SDK" with a link to the download page. Some features (debugging,
  project scaffolding) depend on the SDK/`dotnet` CLI. [1][2]
- Ionide does **not** bundle a runtime and does **not** declare a dependency on the .NET Install
  Tool. It relies on discovering `dotnet` via **PATH**, the **`DOTNET_ROOT`** environment
  variable, and the **`FSharp.dotnetRoot`** setting (OS-dependent default:
  `C:\Program Files\dotnet` on Windows, `/usr/local/share/dotnet` on Unix). [3][4]
- **Failure UX (the cautionary tale):** when `dotnet` is not found, Ionide surfaces:
  > "Cannot start .NET Core language services because `dotnet` was not found. Consider: setting
  > the `FSharp.dotnetRoot` settings key to a directory with a `dotnet` binary, including `dotnet`
  > in your PATH, or installing .NET Core into one of the default locations." [5]
  There is a long tail of "dotnet not found even though it's installed" issues driven by
  non-standard install locations, WSL, localized `Program Files` paths, and PATH quirks — i.e.
  detection is the hard part of strategy (a), not the happy path. [3][5][6]
- Ionide is also **activated lazily** on F# activation events (opening/creating `.fs`, `.fsx`,
  `.fsproj`), which is the right place to run a prerequisite check. [2]

**Idiomatic F# norm:** the dominant F# tooling (Ionide) assumes a **user-provided SDK** and
communicates a missing one with a notification + settings guidance. There is no F#-specific
convention for bundling or auto-acquiring a runtime. [1][2]

### 1.2 C# extension (`ms-dotnettools.csharp`) / C# Dev Kit / OmniSharp — strategy (c) + framework-dependent LS

- The C# extension's language server (`Microsoft.CodeAnalysis.LanguageServer`, Roslyn-based) is a
  **framework-dependent** .NET app — it needs a runtime present, exactly like our companion. When
  the runtime can't be resolved it fails with "You must install .NET to run this application …
  .NET location: Not found." [7]
- Rather than bundle a runtime, **the C# extension takes a hard dependency on the .NET Install
  Tool**: "Whether you install C# Dev Kit or just the C# extension, the .NET Install Tool will be
  installed as a dependency." It acquires the runtime on demand via that extension's API. [8]
- Users can override discovery with
  `"dotnetAcquisitionExtension.existingDotnetPath": [{ "extensionId": "ms-dotnettools.csharp",
  "path": "/usr/bin/dotnet" }]` when they want to point at an existing install. [7]

**Takeaway:** the closest analog to our companion (a framework-dependent Roslyn LS) is Microsoft's
own C# extension, and it chose **strategy (c)** — acquire-on-demand — not bundling.

---

## 2. The .NET Install Tool (`ms-dotnettools.vscode-dotnet-runtime`) — strategy (c) in detail

Official repo: `dotnet/vscode-dotnet-runtime`. It exists precisely so extensions "install a .NET
runtime for VS Code extensions." [9][10]

**How you declare the dependency** (in `package.json`):

```json
"extensionDependencies": ["ms-dotnettools.vscode-dotnet-runtime"]
```

VS Code then installs the Install Tool automatically alongside your extension. [9]

**How you call it** (from the extension host):

```ts
const res = await vscode.commands.executeCommand<IDotnetAcquireResult>('dotnet.acquire', {
  version: '10.0',                         // major.minor
  mode: 'runtime',                         // 'runtime' | 'aspnetcore'
  requestingExtensionId: 'tw0po1nt.fshttp-studio',
  // architecture?: optional
});
const dotnetPath = res.dotnetPath;         // path to the acquired dotnet executable
```

Key facts about the API surface: [9][10][11]

- `dotnet.acquire` installs a **runtime** (or ASP.NET Core runtime via `mode`) at a **user-level**
  folder and returns an `IDotnetAcquireResult` containing the path to the `dotnet` executable. It
  auto-updates to the latest **patch** of the requested major.minor.
- **It does not install an SDK.** SDK installs are a separate, **global/system-level** command:
  `dotnet.acquireGlobalSDK` (accepts `6`, `6.0`, `6.0.4xx`, `6.0.402` version forms). Our
  companion needs only a runtime, so `dotnet.acquire` in `runtime` mode is the relevant call.
- Supporting commands: `dotnet.acquireStatus` (check without installing), `dotnet.findPath`
  (resolve an existing `dotnet`), `dotnet.availableInstalls`, `dotnet.listVersions`,
  `dotnet.recommendedVersion`, `dotnet.uninstall` / `dotnet.uninstallAll` / `dotnet.resetData`,
  `dotnet.showAcquisitionLog` / `dotnet.getAcquisitionLog`, `dotnet.reportIssue`.
  `dotnet.ensureDotnetDependencies` (Linux native deps) is **deprecated**.
- **Offline behavior:** if a compatible install already exists, it is returned without a network
  round-trip.

**Reliability caveats** (must be planned for, not assumed away):

- **Behavior change in v3.0.0:** `dotnet.acquire` **no longer always fetches the latest** version.
  It reuses existing installs and only checks for newer versions daily after a configurable delay.
  To force a fresh runtime every call, pass `forceUpdate: true`. This is a documented **breaking
  change** with an official migration note. [12][13]
- **Corporate networks:** installs pull from Microsoft servers, so timeouts/proxies matter. Users
  can tune `dotnetAcquisitionExtension.installTimeoutValue` and set a proxy via
  `dotnetSDKAcquisitionExtension.proxyUrl`; `dotnetAcquisitionExtension.existingDotnetPath` lets
  them bypass acquisition entirely and point at a pre-installed runtime. [7][9]
- Historically there are field reports of the acquired runtime not being picked up by a
  framework-dependent language server (path/`DOTNET_ROOT` resolution), which is the failure mode
  C# users hit — the companion would need to launch using the returned `dotnetPath` explicitly. [7]

---

## 3. Platform-specific `.vsix` (self-contained bundling) — strategy (b) in detail

Official VS Code publishing docs define the mechanism. [14]

- **Mechanism:** publish separate VSIX packages per platform via the `--target` flag (requires
  `@vscode/vsce` ≥ 1.99.0). A package published **without** `--target` becomes the **fallback**
  used for any platform lacking a dedicated build. [14]
- **Target identifiers (10):** `win32-x64`, `win32-arm64`, `linux-x64`, `linux-arm64`,
  `linux-armhf`, `alpine-x64`, `alpine-arm64`, `darwin-x64`, `darwin-arm64`, and `web`. [14]
- **Commands:**

  ```bash
  vsce publish --target win32-x64 win32-arm64
  # or package then publish a specific artifact:
  vsce package --target win32-x64
  vsce publish --packagePath PATH_TO_WIN32X64_VSIX
  ```

  [14]
- **CI/matrix implication:** to cover the desktop matrix (win x64/arm64, linux x64/arm64, darwin
  x64/arm64 — plus optionally the alpine/armhf variants) you run a build matrix, produce a
  self-contained companion per RID, and publish one VSIX per target. VS Code Marketplace serves
  each user the matching build automatically. Microsoft explicitly recommends automating this with
  CI (e.g. GitHub Actions). [14]

**Payload size of a self-contained .NET app** (this is what gets multiplied per platform):

- A basic **self-contained** .NET app is roughly **60–70 MB** on disk; single-file variants land
  around **~58 MB** (.NET 6, Windows). These are order-of-magnitude figures for an
  untrimmed runtime bundle. [15][16]
- **Trimming** (`<PublishTrimmed>true</PublishTrimmed>`, self-contained only) can cut this
  dramatically — a hello-world with trimming has been reduced to ~11 MB — but trimming is fragile
  for code that uses **unbounded reflection** or dynamically-loaded assemblies, and can silently
  break at runtime. The companion's dependency surface (FsHttp + its transitive deps) must be
  vetted before relying on trimming. [17][18]
- For contrast, today's **framework-dependent** publish is a few hundred KB–single-digit MB
  (just the app DLLs), because it carries no runtime. [19]

---

## 4. Side-by-side trade-offs for FsHttp.Studio

| Dimension | (a) Framework-dependent + first-run guide | (b) Self-contained per-platform VSIX | (c) Acquire-on-demand (.NET Install Tool) |
|---|---|---|---|
| **VSIX payload** | Smallest — app DLLs only, no runtime (KB–low MB) [19] | Largest — +~60–70 MB runtime **per platform** (or ~11 MB if trimmed & trimming is safe) [15][17] | Small — no runtime in VSIX; runtime fetched at runtime [9] |
| **User prerequisite** | Must pre-install a compatible .NET 10 runtime themselves [1] | None — runtime ships in the VSIX [14] | None at install; a **network fetch** happens on first use (unless a matching runtime already exists) [9][11] |
| **Per-platform packaging burden** | None — one universal VSIX | High — build matrix + N `--target` publishes + a fallback VSIX [14] | None — one universal VSIX; the tool handles per-OS acquisition [9] |
| **First-run/failure UX** | Worst — the Ionide "dotnet not found" class of problems: PATH/`DOTNET_ROOT` discovery, WSL, localized paths [5][6] | Best happy-path — nothing to fetch/detect; runs offline immediately | Good, but depends on a **network install** succeeding; proxies/timeouts and the v3.0.0 "not-latest" behavior are real caveats [12][13] |
| **Offline installs** | Works if runtime already present | Works fully offline | Works only if a compatible runtime is already cached; otherwise needs network [11] |
| **Runtime updates/servicing** | User's responsibility (OS/package manager) | **Maintainer** owns it — must re-ship VSIX per platform for every runtime patch | Tool auto-updates to latest patch of the pinned major.minor (with v3.0.0 caveat) [11][12] |
| **Precedent** | Ionide (F#) [1] | (native-module extensions; heavier for a full runtime) [14] | Microsoft C# extension / C# Dev Kit — the closest analog to our companion [7][8] |

### Notes that cut across the options

- **The companion needs a runtime, not an SDK.** That favors `dotnet.acquire`'s `runtime` mode in
  (c), and means (b)'s bundle can be runtime-only (not the ~800 MB SDK). [11]
- **Version pinning:** the repo pins SDK `10.0.200` in `global.json`. Whatever strategy is chosen,
  the target runtime major.minor (`10.0`) must be pinned consistently — `dotnet.acquire` takes
  `10.0` and rolls the patch; a self-contained bundle pins the exact runtime shipped. [11]
- **Hybrid is viable and is what Microsoft effectively does:** ship framework-dependent, depend on
  the Install Tool, *and* honor an `existingDotnetPath`-style override so power users can point at
  their own runtime. This keeps the VSIX small while removing the "you must install .NET yourself"
  prerequisite. [7][8]

---

## Sources

1. Ionide getting started / prerequisites — <https://ionide.io/Editors/Code/getting_started.html>
2. Ionide for F# repo (activation events, walkthrough, package.json) — <https://github.com/ionide/ionide-vscode-fsharp>
3. Ionide can't find dotnet even though installed (PATH/detection) — <https://github.com/ionide/ionide-vscode-fsharp/issues/1516>
4. FsAutoComplete: auto-detection of dotnet SDK root / `DOTNET_ROOT` / `FSharp.dotnetRoot` — <https://github.com/ionide/FsAutoComplete/issues/475>
5. Ionide "Cannot start .NET Core language services because `dotnet` was not found" — <https://github.com/ionide/ionide-vscode-fsharp/issues/1712>
6. Ionide: no helpful error when dotnet isn't installed — <https://github.com/ionide/ionide-vscode-fsharp/issues/1079>
7. vscode-csharp: LSP server can't find installed .NET runtime (framework-dependent LS, `existingDotnetPath`) — <https://github.com/dotnet/vscode-csharp/issues/5734>
8. vscode-csharp README (".NET Install Tool will be installed as a dependency") — <https://github.com/dotnet/vscode-csharp/blob/main/README.md>
9. .NET Install Tool extension README (API, `extensionDependencies`, settings) — <https://github.com/dotnet/vscode-dotnet-runtime/blob/main/vscode-dotnet-runtime-extension/README.md>
10. vscode-dotnet-runtime repo — <https://github.com/dotnet/vscode-dotnet-runtime>
11. .NET Install Tool commands reference (`dotnet.acquire`, `dotnet.acquireGlobalSDK`, modes, versions) — <https://github.com/dotnet/vscode-dotnet-runtime/blob/main/Documentation/commands.md>
12. Breaking change: `dotnet.acquire` no longer always downloads latest (v3.0.0) — <https://learn.microsoft.com/en-us/dotnet/core/compatibility/install-tool/3.0.0/vscode-dotnet-acquire-no-latest>
13. Tracking issue for the v3.0.0 `dotnet.acquire` breaking change — <https://github.com/dotnet/docs/issues/49127>
14. VS Code — Publishing Extensions: platform-specific extensions / `--target` / identifiers — <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>
15. Reducing the size of self-contained .NET Core applications (~64 MB self-contained) — <https://ianqvist.blogspot.com/2018/01/reducing-size-of-self-contained-net.html>
16. Making a tiny self-contained single .exe (~58 MB, trimming to smaller) — <https://www.hanselman.com/blog/making-a-tiny-net-core-30-entirely-selfcontained-single-executable>
17. Trim self-contained applications (`PublishTrimmed`, self-contained only) — <https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained>
18. Smallest .NET Hello World (trimming/AOT size reductions and caveats) — <https://www.awise.us/2021/06/05/smallest-dotnet.html>
19. .NET application publishing overview (framework-dependent vs self-contained) — <https://learn.microsoft.com/en-us/dotnet/core/deploying/>
