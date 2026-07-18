# Research: license options + dependency-license constraints for FsHttp.Studio

**Issue:** #51 · **Date:** 2026-07-18 · **Branch:** `research/license-options`

This is a **facts-gathering** note to support an open-source license decision. It does
not make the decision. Bottom line up front: **nothing in the dependency graph constrains
the choice** — every dependency is permissive (MIT or Apache-2.0), and permissive licenses
do not dictate the downstream project's license. FsHttp.Studio is free to pick either
**MIT** or **Apache-2.0** (or another permissive license).

---

## 1. What FsHttp.Studio actually depends on, and how

Dependencies split into three buckets by *how* they reach a user, which matters for
attribution obligations. Sources for the manifests are in-repo:

- `companion/Companion.fsproj` — FSharp.Compiler.Service `43.12.204`, FSharp.Core `10.1.204`
- `extension-host/package.json` — `esbuild ^0.28.1`, `@vscode/vsce ^3.9.2` (both devDependencies); `engines.vscode ^1.66.0`
- `extension-host/esbuild.mjs` — bundles the Fable-emitted JS, marks `vscode` **external**
- `.config/dotnet-tools.json` — `fable 5.10.0`, `fantomas 7.0.5` (local dotnet tools)
- FsHttp is **not** referenced by the project. It is pinned by the *user's own script*
  via `#r "nuget: FsHttp, <ver>"` and resolved by FSI at run time
  (see `companion.Tests/BlockRunnerTests.fs` — the companion deliberately carries no
  static FsHttp reference; ADR-0002).

| Bucket | What it means | Members |
|---|---|---|
| **Shipped in the VSIX** | Code/binaries redistributed inside the extension package | FSharp.Compiler.Service, FSharp.Core (companion DLLs), fable-library-js (bundled into `dist/extension.js` by esbuild) |
| **Build/dev tools only** | Run at build time, not redistributed | Fable compiler, Fantomas, esbuild, @vscode/vsce |
| **User runtime pin** | Never shipped; supplied by the end user's script | FsHttp |
| **Host-provided (external)** | Not bundled; provided by the VS Code runtime | `vscode` API |

Attribution obligations attach mainly to the **shipped** bucket — and everything shipped
is MIT.

---

## 2. Dependency licenses

All confirmed from primary sources (repo LICENSE files / NuGet / npm registry).

| Dependency | Version | License (SPDX) | Bucket | Obligation | Source |
|---|---|---|---|---|---|
| FSharp.Compiler.Service | 43.12.204 | **MIT** | Shipped | Retain copyright + permission notice | NuGet page; `dotnet/fsharp` License.txt (MIT, © Microsoft) |
| FSharp.Core | 10.1.204 | **MIT** | Shipped | Retain copyright + permission notice | `dotnet/fsharp` License.txt (MIT) |
| fable-library-js | (Fable 5.x) | **MIT** | Shipped (bundled) | Retain copyright + permission notice | Published from `fable-compiler/Fable` (MIT); package `@fable-org/fable-library-ts` declares `license: MIT` |
| Fable (compiler) | 5.10.0 | **MIT** | Build tool | None on redistribution (tool) | `fable-compiler/Fable` LICENSE (MIT) |
| Fantomas | 7.0.5 | **Apache-2.0** | Build tool | None shipped; Apache attribution only if redistributed | `fsprojects/fantomas` LICENSE.md ("Licensed under the Apache License, Version 2.0") |
| esbuild | ^0.28.1 | **MIT** | Build tool | None on redistribution (tool) | `evanw/esbuild` LICENSE.md; npm `license: MIT` |
| @vscode/vsce | ^3.9.2 | **MIT** | Build tool | None on redistribution (tool) | npm `license: MIT`; `microsoft/vscode-vsce` LICENSE text is MIT (GitHub mislabels it "NOASSERTION" due to the custom header) |
| FsHttp | user-pinned (tests use 15.0.3) | **Apache-2.0** | User runtime pin | None on the extension — not shipped | `fsprojects/FsHttp` LICENSE (Apache-2.0, © 2018 Ronald Schlenker) |
| VS Code extension API (`vscode`) | engine ^1.66.0 | **MIT** (Code – OSS / `@types/vscode`) | Host-provided, external | Not bundled; no redistribution | Marked `external` in `esbuild.mjs`; types from DefinitelyTyped (MIT) |

**Copyleft?** None. No GPL/LGPL/MPL/EPL anywhere in the graph.

**Notable attribution / NOTICE requirements:**
- **MIT deps (everything shipped):** keep the copyright line + permission notice with the
  distribution. Standard practice: a `ThirdPartyNotices` / bundled-licenses file in the VSIX.
- **Apache-2.0 deps (Fantomas, FsHttp):** Apache-2.0 §4 adds "state significant changes"
  and "preserve NOTICE file if present" obligations — **but neither is shipped by the
  extension**, so these obligations are not triggered. Fantomas is a build-time formatter;
  FsHttp is resolved from the user's own `#r`.
- Net: the only live obligation is MIT notice-retention for the shipped .NET DLLs and the
  bundled fable-library-js.

---

## 3. What comparable projects license under

Confirmed from each project's repo (GitHub license API / LICENSE file).

| Project | Kind | License (SPDX) | Source |
|---|---|---|---|
| Ionide (ionide-vscode-fsharp) | F# VS Code extension | **MIT** | `ionide/ionide-vscode-fsharp` LICENSE.md |
| Fable | F#→JS compiler | **MIT** | `fable-compiler/Fable` LICENSE |
| FsHttp | F# HTTP DSL (this repo's namesake) | **Apache-2.0** | `fsprojects/FsHttp` LICENSE |
| F# compiler / FSharp.Core | Language + core lib | **MIT** | `dotnet/fsharp` License.txt |
| Paket | .NET dependency manager | **MIT** | `fsprojects/Paket` |
| FAKE | F# build DSL | **Apache-2.0** | `fsprojects/FAKE` LICENSE |
| Fantomas | F# formatter | **Apache-2.0** | `fsprojects/fantomas` LICENSE.md |
| Expecto | F# test framework | **Apache-2.0** | `haf/expecto` |
| Thoth.Json | F# JSON lib | **MIT** | `thoth-org/Thoth.Json` |
| OmniSharp (roslyn) | .NET tooling backend | **MIT** | `OmniSharp/omnisharp-roslyn` |
| vscode-python | Prominent VS Code extension | **MIT** | `microsoft/vscode-python` |

**Prevailing choice:** **MIT dominates**, especially for VS Code extensions (Ionide,
vscode-python) and the JS/npm side generally. The F# library/tooling world is a genuine
**mix**: several fsprojects tools (FAKE, Fantomas, Expecto) and FsHttp itself use
**Apache-2.0**. So both licenses have strong, direct precedent in this exact ecosystem — an
Apache-2.0 choice would not be an outlier, but MIT is the more common default for an
extension.

---

## 4. MIT vs Apache-2.0 for a solo-maintained OSS VS Code extension

Both are OSI-approved permissive licenses; both are one-way compatible into the other and
into proprietary redistribution. The practical differences:

| Dimension | MIT | Apache-2.0 |
|---|---|---|
| Length / readability | ~170 words, universally recognized | ~10x longer, structured sections |
| **Patent grant** | No *explicit* grant (only an implied license, legally weaker) | **Explicit patent license (§3)** from contributors, **plus patent-retaliation termination** — defensive value if a contributor later asserts patents |
| Trademark | Silent | **Explicit non-grant (§6)** — clarifies the name/marks aren't licensed |
| NOTICE file | None | **§4** requires preserving a `NOTICE` file if one exists and stating significant changes — small ongoing packaging discipline if *you* ship one |
| Contributor friction | Inbound=outbound, zero ceremony; ideal for drive-by PRs | Often paired with a DCO/CLA; §5 already states contributions are under the license, so a CLA is optional but common |
| Ecosystem fit | Matches VS Code / npm norm and Ionide | Matches FsHttp, FAKE, Fantomas, Expecto |
| Marketplace / Open VSX | Accepted; SPDX `MIT` in `package.json` `license` field | Accepted; SPDX `Apache-2.0` in `package.json` `license` field |

**Marketplace / Open VSX license-field expectations:** both registries expect a license.
`vsce package` warns when no `license` field or `LICENSE` file is present; **Open VSX
(Eclipse) is stricter and effectively requires a valid license to publish**. A valid SPDX
identifier in `package.json`'s `license` field (`"MIT"` or `"Apache-2.0"`) plus a matching
`LICENSE` file at repo root satisfies both. The current `extension-host/package.json` has
**no `license` field** and `"private": true` — one will need to be added before publishing
regardless of which license is chosen.

**Gist for a solo maintainer:**
- **MIT** — shortest, lowest contributor friction, matches the VS Code/Ionide norm; the
  safe default. Cost: no explicit patent protection.
- **Apache-2.0** — adds an explicit patent grant + retaliation clause (real defensive
  value) and matches FsHttp's own license; costs a bit more packaging discipline (NOTICE
  handling) and is slightly heavier for casual contributors.

---

## 5. Does anything in the dependency graph force the choice?

**No.** All dependencies are permissive (MIT, with Apache-2.0 only on build-time/user-pinned
components that aren't shipped). Permissive licenses impose attribution, not license
inheritance, so they do not constrain FsHttp.Studio's own license. Both **MIT** and
**Apache-2.0** are fully compatible with the entire graph. The only concrete compliance task
under either choice is retaining third-party notices (MIT text for the shipped .NET DLLs and
bundled fable-library-js; Apache NOTICE/attribution would only apply if an Apache-2.0
component were ever bundled, which today none is).

---

### Sources (primary)
- dotnet/fsharp License.txt (MIT) — FSharp.Core, FSharp.Compiler.Service
- fsprojects/FsHttp LICENSE (Apache-2.0)
- fable-compiler/Fable LICENSE (MIT); fable-library package `license: MIT`
- fsprojects/fantomas LICENSE.md (Apache-2.0)
- evanw/esbuild LICENSE.md + npm `license: MIT`
- microsoft/vscode-vsce LICENSE (MIT text) + npm `@vscode/vsce license: MIT`
- ionide/ionide-vscode-fsharp, fsprojects/Paket, fsprojects/FAKE, haf/expecto,
  thoth-org/Thoth.Json, OmniSharp/omnisharp-roslyn, microsoft/vscode-python — GitHub license API
- In-repo manifests: `companion/Companion.fsproj`, `extension-host/package.json`,
  `extension-host/esbuild.mjs`, `.config/dotnet-tools.json`, `companion.Tests/BlockRunnerTests.fs`
