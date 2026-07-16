// Placeholder webview entry (issue #14): proves esbuild bundles this as its own
// browser-target entry, separate from the extension host's Node-target bundle
// (ADR-0005). The renderer core lands on later tickets.
module Webview.Main

open Fable.Core

do JS.console.log "FsHttp.Studio webview loaded"
