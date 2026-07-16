# Bundle with esbuild, not webpack

v0.1 bundles both the extension host and the webview script with esbuild.

The bindings research ([#4](https://github.com/tw0po1nt/FsHttp.Explorer/issues/4)) recommended webpack and warned esbuild off — but for **one** reason only: esbuild silently broke `web-tree-sitter`'s wasm loading (its ESM→CJS shim leaves `import.meta.url` undefined, so the runtime wasm never resolves). That risk no longer exists. [ADR-0003](./0003-block-location-in-companion.md) moved block location into the companion and ships **no tree-sitter — and no other wasm — in the extension host**, so the sole thing that eliminated esbuild is gone.

With a plain Fable-ESM + webview-script bundle and `vscode` marked external, esbuild is the faster, lower-config modern default — the one VSCode's own extension samples use — and Ionide itself has signalled intent to move off webpack. The choice is low-stakes and roughly a one-file swap if it ever bites.

Recorded specifically so no one "reverts" to webpack on the strength of #4's now-stale recommendation without realising its premise was deleted by ADR-0003.
