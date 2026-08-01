# Bundle with esbuild, not webpack

v0.1 bundles both the extension host and the webview script with esbuild.

The bindings research ([#4](https://github.com/tw0po1nt/FsHttp.Studio/issues/4)) recommended webpack and rejected esbuild, but for **one** reason only. esbuild silently broke the wasm load in `web-tree-sitter`. Its ESM-to-CJS shim leaves `import.meta.url` undefined, so the runtime wasm never resolves.

That risk no longer exists. [ADR-0003](./0003-block-location-in-companion.md) moved block location into the companion. The extension host now ships **no tree-sitter, and no other wasm**, so the only objection to esbuild is gone.

The build is a plain Fable-ESM bundle plus a webview script, with `vscode` marked external. For that build, esbuild is the faster modern default, and it needs less configuration. VSCode's own extension samples use it, and Ionide has signaled its intent to leave webpack. The choice is low-risk, and a change back is approximately a one-file swap.

This decision is recorded so that nobody "reverts" to webpack because of the stale recommendation in #4. ADR-0003 deleted the premise of that recommendation.
