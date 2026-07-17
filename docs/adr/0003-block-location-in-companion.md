# Locate blocks in the companion via FCS; no tree-sitter in v0.1

Block ranges are found by the companion's FCS parse — the same parse it already runs for diagnostics — rather than by a tree-sitter grammar running in the extension host. v0.1 ships no tree-sitter.

## Considered Options

Research built a working in-host path: `web-tree-sitter` loading the tree-sitter-fsharp wasm, which located blocks with ranges identical to FCS in ~1.7 ms. We still chose the companion because, once it exists and is long-lived ([ADR-0002](./0002-fcs-companion-framed-envelope.md)), it is already re-parsing on every edit for diagnostics — so block ranges piggyback that parse for free: one parser, one truth, and no second language toolchain locked into the build. The in-host path's one advantage — CodeLenses that survive a down companion — is near-worthless, because the CodeLens's only action (Run) needs the companion anyway.

## Consequences

No CodeLenses exist while the companion is booting or absent; this is surfaced through a companion-state status-bar item rather than looking like silent breakage. The tree-sitter research is kept in reserve as a proven fallback, to return only if degraded-mode CodeLenses become a real post-demo complaint.

### Range coordinate convention

Because ranges come straight off the FCS parse tree, the `blocks` envelope carries **FCS-native coordinates: 1-based lines, 0-based columns**. VS Code's own model (`vscode.Range`) uses **0-based lines**, so a consumer that forwards `startLine`/`endLine` unchanged into a `Range` lands every CodeLens exactly one line too low. The extension host is therefore responsible for subtracting 1 from the line when translating a `blocks` range into a VS Code position; the wire format is intentionally FCS-native so the companion never has to know about editor conventions. (Introduced with the block locator in [#15](https://github.com/tw0po1nt/FsHttp.Explorer/issues/15); the companion-side convention is also documented on `BlockLocator.BlockRange`.)
