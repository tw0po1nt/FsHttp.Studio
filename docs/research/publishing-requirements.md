# Publishing FsHttp.Studio to the VS Code Marketplace and Open VSX

Research for issue #50. Goal: establish the exact end-to-end requirements to publish v0.1 to **both**
registries. This is a facts-only account — no decisions are made here. Every claim is cited to a
primary source (official VS Code / Azure DevOps / Eclipse / Open VSX docs, or the `vsce`/`ovsx` tool
repos).

Date of research: 2026-07-18.

---

## 0. Repo context (why this matters for us)

`extension-host/package.json` today:

- `name: "fshttp-studio"`, `displayName: "FsHttp.Studio"`, `version: "0.0.1"`, `engines.vscode: "^1.66.0"`.
- `"private": true` and **no `publisher` field** — both must change before a publish will succeed.
- Already packages with `vsce package --no-dependencies` (esbuild bundle), and ships a **.NET companion**
  built via `dotnet publish` into `dist/companion`. A self-contained .NET runtime is
  OS/arch-specific, which is exactly the situation the `--target` (platform-specific) publishing
  matrix in §3 exists for.

No decision is made here about publisher id, license, or whether to ship platform-specific builds —
those are downstream tickets. This doc only records what each path *requires*.

---

## 1. VS Code Marketplace — step-ordered

### 1.1 Prerequisites → Azure DevOps organization

The Marketplace uses **Azure DevOps** for publisher authentication. You need an Azure DevOps
organization to mint the token (not to host anything). "If you don't have an Azure DevOps
organization yet, follow the steps in the Create an organization article."
Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

### 1.2 Personal Access Token (PAT) — exact scopes

From the Azure DevOps user-settings → **Personal access tokens** → New Token:

- **Organization** field must be set to **"All accessible organizations"**. Selecting a single
  specific organization causes publishing to fail.
- **Scopes**: choose "Custom defined" → "Show all scopes" → under **Marketplace** select
  **Manage**. (The Marketplace **Manage** scope is the one required to publish.)
- **Expiration**: you set the lifetime; the token expires and must be regenerated. Note the platform
  change: **global/"full" PATs in Azure DevOps are being retired (effective 2026)**, so plan on
  scoped tokens with an explicit expiry and a rotation story for CI.

Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

### 1.3 Create the publisher

1. Go to the Marketplace publisher management page: <https://marketplace.visualstudio.com/manage>
2. Log in with the **same Microsoft account** used to create the PAT.
3. "Create publisher". Mandatory fields:
   - **ID** — "the unique identifier for your publisher in Marketplace that will be used in your
     extension URLs. **ID cannot be changed once created.**"
   - **Name** — the human display name (can be a brand/company name; changeable).

Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

### 1.4 Authenticate the CLI

Install and log in:

```
npm install -g @vscode/vsce      # the tool is @vscode/vsce; binary is `vsce`
vsce login <publisher-id>        # paste the PAT when prompted
```

Success message: "The Personal Access Token verification succeeded for the publisher
'<publisher id>'." Alternatively supply the token non-interactively via the **`VSCE_PAT`**
environment variable (recommended for CI; also avoids the OS keychain/keytar).

Sources: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>,
<https://github.com/microsoft/vscode-vsce>

### 1.5 Manifest metadata

See the consolidated **mandatory-fields checklist** in §5. Marketplace-specific notes:

- `name` "must be all lowercase with no spaces" and **must be unique to the Marketplace**.
- `displayName` "must be unique to the Marketplace" (when present).
- The fully-qualified extension id is **`<publisher>.<name>`**.

Source: <https://code.visualstudio.com/api/references/extension-manifest>

### 1.6 Package

```
vsce package                     # produces a .vsix
vsce package --no-dependencies   # when the extension is already bundled (our case)
```

- By default `devDependencies` are ignored automatically; `vsce` otherwise tries to resolve
  `dependencies` from `node_modules`.
- **`--no-dependencies`** tells `vsce` **not** to resolve/include `node_modules` because everything is
  already bundled (webpack/esbuild). Use it on **both** `package` and `publish`. Our repo already does
  this. Use a `.vscodeignore` to keep the .vsix lean.

Sources: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>,
DEV/community write-ups confirming the flag skips `node_modules` resolution for bundled extensions
(<https://medium.com/@fengyu214/vsce-support-16014c35bc3c>).

### 1.7 Publish

```
vsce publish                     # package + upload in one step (uses stored PAT / VSCE_PAT)
vsce publish --no-dependencies   # bundled extensions
vsce publish minor               # bump semver + publish; in a git repo also creates a version commit + tag
vsce publish 1.1.0               # explicit version
```

Alternatively: `vsce package` then upload the `.vsix` by hand on the management page.

Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

### 1.8 What is permanent after first publish (id constraints)

- **Publisher ID** — "ID cannot be changed once created."
- **Extension `name`** — permanent and reserved: "Once an extension is removed, its extension name is
  permanently reserved and cannot be reused" (even by the original publisher).
- Therefore the **fully-qualified id `<publisher>.<name>`** is effectively permanent. `displayName`,
  `description`, `version`, icon, etc. are all changeable across releases; the id pair is not.

Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

### 1.9 Verified publisher (optional, later)

A blue-check "verified publisher" requires **domain verification via a DNS TXT record**. Prerequisites:
"a publisher must have one or more extensions on the VS Marketplace for a minimum of 6 months, and the
registration of the domain must also be at least 6 months old." The domain must support DNS TXT
records, must not be a subdomain, must use HTTPS, and must return HTTP 200 to a HEAD request. Not
required to publish v0.1.

Source: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>

---

## 2. Open VSX — step-ordered

### 2.1 Prerequisites → Eclipse account + GitHub

1. Create an account at **eclipse.org** and **fill in the GitHub Username field** — "It is important to
   fill in the GitHub Username field and to use exactly the same GitHub account as when you log in to
   open-vsx.org."
2. Log in at **open-vsx.org** by authorizing with that **same GitHub account**.

Sources: <https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>,
<https://www.eclipse.org/legal/open-vsx-registry-faq/>

### 2.2 Sign the Publisher Agreement (mandatory)

On your open-vsx.org profile, "Log in with Eclipse" to link the Eclipse account, then use the
**"Show Publisher Agreement"** button: "read the agreement text to the bottom and click Agree."
The FAQ states plainly: "All publishers are required to: **Sign the Eclipse Foundation Open VSX
Publisher Agreement**." (This is a *different* document from the Eclipse Contributor Agreement / ECA.)

Sources: <https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>,
<https://www.eclipse.org/legal/open-vsx-registry-faq/>

### 2.3 Generate an access token

Profile → **Access Tokens** → "Generate New Token". The value "is never displayed again after you
close the dialog", so store it immediately. Recommendation: "generate a new token for each
environment where you want to publish, e.g. a local machine, cloud IDE, or CI build."

Source: <https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>

### 2.4 Create / claim the namespace

The **`publisher` field of package.json defines the namespace.** Create it before first publish:

```
npx ovsx create-namespace <name> -p <token>
```

- Valid namespace names "can only contain letters, numbers, and '-', '+', '$' and '~'."
- **Creating a namespace does NOT make you the verified owner.** "Initially, everyone will be able to
  publish an extension with the new namespace" until ownership is claimed. To get the **verified**
  badge you must **claim ownership** of the namespace (Open VSX verifies via the linked GitHub account
  / repository ownership).

Sources: <https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>,
<https://github.com/eclipse/openvsx/blob/master/cli/README.md>

### 2.5 License is REQUIRED on Open VSX

Open VSX **rejects any extension without a declared license.** The Eclipse FAQ: "Yes, all extensions
must be licensed. Your extension's license should be expressed by including a **license expression in
the package.json manifest**." Notes:

- It **need not** be an OSI-approved open-source license.
- The source-code license and the manifest `license` field **may differ**.
- The `ovsx` CLI enforces this at publish time; without a `license` field the publish fails
  ("extension cannot be accepted because it has no license"), and for open-vsx.org the CLI can
  interactively offer to add an MIT license.

This is the main manifest difference from the Marketplace, where `license` is only *recommended*.

Sources: <https://www.eclipse.org/legal/open-vsx-registry-faq/>,
<https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>

### 2.6 Package & publish

```
npx ovsx publish -p <token>                 # builds via vsce in cwd, then publishes
npx ovsx publish <file.vsix> -p <token>     # publish an already-packaged .vsix (recommended: reuse the same .vsix as the Marketplace)
```

- Token can also be passed via the **`OVSX_PAT`** environment variable instead of `-p`.
- Other CLI commands: `ovsx login <name>` / `ovsx logout <name>` (store/remove token),
  `ovsx get <extension>` (download), `ovsx create-namespace`.
- After upload the extension shows **"Deactivated"** for ~5–10 s while it is processed asynchronously,
  then goes live.

Sources: <https://github.com/eclipse/openvsx/blob/master/cli/README.md>,
<https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>

---

## 3. Platform-specific publishing (`--target`) on both registries

Relevant to us because the bundled **.NET companion** is OS/arch-specific if shipped self-contained.

- **Target identifiers (shared by both registries):**
  `win32-x64`, `win32-arm64`, `linux-x64`, `linux-arm64`, `linux-armhf`, `alpine-x64`,
  `alpine-arm64`, `darwin-x64`, `darwin-arm64`, and `web`.
- **Marketplace (`vsce`)**: `vsce publish --target win32-x64 win32-arm64` (or use `--target` on
  `vsce package`). "If you don't pass this flag, that package will be used as a **fallback** for all
  platforms that have no platform-specific package." You publish one .vsix per target; the store
  serves the matching build per client OS/arch, falling back to the universal one.
- **Open VSX (`ovsx`)**: also supports `ovsx publish --target <id>` (since ovsx ≥ 0.5.0). The
  platform-specific artifact name format is `namespace.extension-version@target.vsix`. Multiple
  `--target` values and multiple `--packagePath` args are supported.
- The publishing model is a **matrix**: build one .vsix per (registry × target), then publish each. A
  single universal build (no `--target`) is the simplest path and works if the companion is resolved
  at runtime rather than bundled per-arch — that trade-off is a downstream decision, not decided here.

Sources: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>,
<https://github.com/eclipse/openvsx/blob/master/cli/README.md>,
<https://github.com/eclipse/openvsx/issues/461>

---

## 4. Naming / uniqueness constraints (constrains the publisher-id choice)

- **Marketplace publisher ID**: globally unique, lowercase-style identifier, **immutable after
  creation**, appears in extension URLs. Choose carefully — it can't be changed later.
- **Marketplace extension `name`**: lowercase, no spaces, **unique across the whole Marketplace**, and
  **permanently reserved once used** (never reusable). `displayName` must also be Marketplace-unique.
- **Open VSX namespace** = the `publisher` field: character set restricted to letters, numbers, and
  `- + $ ~`; created via `create-namespace`; ownership must be **claimed** to earn the verified badge.
- **Cross-registry alignment**: because the Open VSX namespace is literally the `package.json`
  `publisher` value, using the **same publisher id on both registries** keeps a single manifest
  publishing to both. Any divergence would force per-registry manifests. (Recording the constraint,
  not making the call.)

Sources: <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>,
<https://code.visualstudio.com/api/references/extension-manifest>,
<https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>

---

## 5. Mandatory manifest-fields checklist

`R` = required (publish fails / listing broken without it) · `Rec` = recommended for a good listing.

| Field            | Marketplace | Open VSX | Notes |
|------------------|-------------|----------|-------|
| `publisher`      | **R**       | **R**    | = the Open VSX namespace. Immutable id half. |
| `name`           | **R**       | **R**    | lowercase, no spaces, Marketplace-unique, permanently reserved. |
| `version`        | **R**       | **R**    | SemVer. |
| `engines.vscode` | **R**       | **R**    | e.g. `^1.66.0`; **cannot be `*`**. |
| `license`        | Rec         | **R**    | **Open VSX requires it** (license expression in manifest, or a bundled LICENSE). |
| `displayName`    | Rec         | Rec      | must be Marketplace-unique when set. |
| `description`    | Rec         | Rec      | short summary shown in listing. |
| `icon`           | Rec         | Rec      | **PNG, at least 128×128 px (256×256 for Retina).** |
| `repository`     | Rec         | Rec      | shown in Resources; helps Open VSX namespace verification. |
| `categories`     | Rec         | Rec      | from the fixed allowed list (Programming Languages, Snippets, …, Testing). |
| `keywords`       | Rec         | Rec      | max 30. |
| `galleryBanner`  | Rec         | Rec      | listing banner color/theme. |

The **strictly mandatory set for a successful publish to both** is:
`publisher`, `name`, `version`, `engines.vscode`, **and `license` (for Open VSX)**. Everything else is
strongly recommended for a credible listing but won't block a publish.

Also note for our repo: `"private": true` must be removed/false and a `publisher` field added before
either tool will publish.

Sources: <https://code.visualstudio.com/api/references/extension-manifest>,
<https://www.eclipse.org/legal/open-vsx-registry-faq/>

---

## 6. Recommended combined flow (facts, not a decision)

1. Pick a publisher id that is valid on **both** registries (§4) — downstream decision ticket.
2. Add `publisher`, `license`, `icon` (≥128×128 PNG), `repository`, `displayName`, `description`,
   `categories`, `keywords` to the manifest; remove `"private": true`.
3. **Marketplace**: Azure DevOps org → PAT (Marketplace **Manage** scope, "All accessible
   organizations") → create publisher → `vsce login` / `VSCE_PAT`.
4. **Open VSX**: eclipse.org account (with GitHub username) → open-vsx.org GitHub login → sign
   Publisher Agreement → generate access token → `ovsx create-namespace`.
5. Build **one `.vsix`** (`vsce package --no-dependencies`).
6. Publish that same artifact to both: `vsce publish --no-dependencies` and
   `npx ovsx publish <file.vsix> -p <token>`.
7. If shipping the .NET companion self-contained per arch, repeat build+publish per `--target` (§3).
8. Later: claim the Open VSX namespace (verified badge); pursue Marketplace verified-publisher after
   the 6-month prerequisites.

---

## Primary sources

- VS Code — Publishing Extensions:
  <https://code.visualstudio.com/api/working-with-extensions/publishing-extension>
- VS Code — Extension Manifest reference:
  <https://code.visualstudio.com/api/references/extension-manifest>
- `vsce` (Visual Studio Code Extension Manager): <https://github.com/microsoft/vscode-vsce>
- Open VSX — Publishing Extensions wiki:
  <https://github.com/EclipseFdn/open-vsx.org/wiki/Publishing-Extensions>
- `ovsx` CLI README: <https://github.com/eclipse/openvsx/blob/master/cli/README.md>
- Eclipse Foundation — Open VSX Registry FAQ (license + publisher agreement):
  <https://www.eclipse.org/legal/open-vsx-registry-faq/>
- Open VSX platform-specific `--target` support: <https://github.com/eclipse/openvsx/issues/461>
