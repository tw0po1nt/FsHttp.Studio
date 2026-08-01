# Locate blocks in the companion with FCS, and ship no tree-sitter in v0.1

The companion's FCS parse finds block ranges. A tree-sitter grammar in the extension host does not. v0.1 ships no tree-sitter.

> **Update (2026-07-19):** The reasoning below assumed that the companion would run always-on, language-server-style diagnostics. That design re-parsed every `http {}` block on each edit, so block location could use that parse for free. We have since decided that the extension does **not** run live diagnostics. The only compile errors that it reports come from a *Run* that fails (`BlockRunner`'s `CompileError`).
>
> This change does not reverse the decision. Block location in the companion still stands on its other reasons: one parser, one language toolchain, and a CodeLens whose only action (Run) needs the companion anyway. Only the cost changes. Block location now pays for its own parse, instead of an addition to a parse that happens anyway. The cost is still small, but it is no longer free.
>
> The original reasoning stays below as a record of the trade-off at that time.

## Considered Options

Research built a working in-host path. `web-tree-sitter` loaded the tree-sitter-fsharp wasm, and located blocks in approximately 1.7 ms with ranges identical to FCS.

We still chose the companion. The companion already exists and is long-lived ([ADR-0002](./0002-fcs-companion-framed-envelope.md)), and it already re-parses on every edit for diagnostics. Block ranges therefore use that parse for free. This gives one parser, one truth, and no second language toolchain in the build. The in-host path has one advantage: CodeLenses that survive a companion that is down. That advantage is almost worthless, because the CodeLens's only action (Run) needs the companion anyway.

## Consequences

No CodeLenses exist while the companion starts, or while it is absent. A companion-state status-bar item reports that state, so it does not look like a silent failure. We keep the tree-sitter research in reserve as a proven fallback. We will return to it only if degraded-mode CodeLenses become a real complaint after the demo.

### Range coordinate convention

Ranges come directly from the FCS parse tree, so the `blocks` envelope carries **FCS-native coordinates: 1-based lines, 0-based columns**. VSCode's own model (`vscode.Range`) uses **0-based lines**. A consumer that forwards `startLine` and `endLine` into a `Range` unchanged therefore puts every CodeLens exactly one line too low.

The extension host must subtract 1 from the line when it translates a `blocks` range into a VSCode position. The wire format is FCS-native on purpose, so the companion never needs to know editor conventions. The block locator introduced this convention in [#15](https://github.com/tw0po1nt/FsHttp.Studio/issues/15). `BlockLocator.BlockRange` also documents the companion-side convention.
