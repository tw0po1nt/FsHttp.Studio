// The response viewer (ADR-0001): a single webview panel, reused across runs,
// opened Beside the editor. This module owns creating/revealing that one panel and posting
// messages into it; the webview side (`webview/Main.fs`) owns turning those messages into
// rendered DOM via the renderer core.
module ResponseViewer

open Fable.Core.JsInterop
open Vscode

let mutable private panel: WebviewPanel option = None

/// A fresh nonce per HTML build, so the CSP's `script-src 'nonce-…'` only ever allows the one
/// script tag this module itself writes.
let private nonce () : string =
    emitJsExpr (nonNull (box 0)) "Array.from({length: 32}, () => Math.floor(Math.random() * 16).toString(16)).join('')"

let private toUriString (u: obj) : string = unbox<string> ((u?toString ()): obj)

// The renderer core commits only to semantic class names (`status-2xx`, `json-string`, `header-row`,
// …) and — by design (see Renderer.fs) — leaves the palette to the shell. This *is* that palette. It
// is expressed in VSCode theme variables (`--vscode-*`) so the panel tracks the user's editor theme
// (light/dark/high-contrast) for free, with literal fallbacks for the few token colours a theme may
// leave undefined. Kept as a plain string literal (not run through `sprintf`) so its `%` and `{}`
// characters need no escaping; the nonce is attached where the `<style>` tag is assembled.
let private responseStyles =
    """
body {
  font-family: var(--vscode-font-family);
  font-size: var(--vscode-font-size, 13px);
  color: var(--vscode-foreground);
  margin: 0;
}
#root { padding: 12px 14px; }

/* in-flight Run indicator (webview/Main.fs showRunning) */
.pending {
  display: flex;
  align-items: center;
  gap: 10px;
  color: var(--vscode-descriptionForeground);
}
.pending-pulse {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--vscode-progressBar-background, var(--vscode-charts-blue, #3794ff));
  animation: pending-pulse 1s ease-in-out infinite;
}
.pending-label { font-variant-numeric: tabular-nums; }
@keyframes pending-pulse {
  0%, 100% { opacity: 0.35; transform: scale(0.8); }
  50%      { opacity: 1;    transform: scale(1.1); }
}
@media (prefers-reduced-motion: reduce) {
  .pending-pulse { animation: none; opacity: 0.8; }
}

/* status line */
.status-line {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  padding: 8px 10px;
  margin-bottom: 12px;
  border: 1px solid var(--vscode-panel-border, rgba(128,128,128,0.25));
  border-radius: 6px;
  background: var(--vscode-editorWidget-background, rgba(128,128,128,0.06));
}
.status-method {
  font-weight: 600;
  font-size: 0.82em;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  padding: 2px 8px;
  border-radius: 4px;
  background: var(--vscode-badge-background);
  color: var(--vscode-badge-foreground);
}
.status-url {
  flex: 1 1 auto;
  min-width: 0;
  font-family: var(--vscode-editor-font-family, monospace);
  word-break: break-all;
}
.status-code { font-weight: 600; white-space: nowrap; }
.status-2xx { color: var(--vscode-charts-green,  #3fb950); }
.status-3xx { color: var(--vscode-charts-blue,   #3794ff); }
.status-4xx { color: var(--vscode-charts-orange, #d18616); }
.status-5xx { color: var(--vscode-charts-red,    #f14c4c); }
.status-other { color: var(--vscode-descriptionForeground); }
.status-time, .status-size {
  color: var(--vscode-descriptionForeground);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

/* collapsible sections */
summary { cursor: pointer; user-select: none; list-style: none; }
summary::-webkit-details-marker { display: none; }

/* headers */
.headers {
  margin-bottom: 12px;
  border: 1px solid var(--vscode-panel-border, rgba(128,128,128,0.25));
  border-radius: 6px;
  overflow: hidden;
}
.headers-summary {
  padding: 6px 10px;
  font-weight: 600;
  background: var(--vscode-editorWidget-background, rgba(128,128,128,0.06));
}
.headers-summary::before {
  content: "\25B8";
  display: inline-block;
  width: 1.1em;
  opacity: 0.7;
  transition: transform 0.12s ease;
}
.headers[open] > .headers-summary::before { transform: rotate(90deg); }
.header-row {
  display: grid;
  grid-template-columns: minmax(120px, 30%) 1fr;
  gap: 12px;
  padding: 4px 10px;
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: 0.92em;
  border-top: 1px solid var(--vscode-panel-border, rgba(128,128,128,0.15));
}
.header-name {
  font-weight: 500;
  word-break: break-word;
  color: var(--vscode-debugTokenExpression-name, var(--vscode-symbolIcon-propertyForeground, #9cdcfe));
}
.header-value { word-break: break-word; }

/* response body container */
.response-body {
  border: 1px solid var(--vscode-panel-border, rgba(128,128,128,0.25));
  border-radius: 6px;
  overflow: auto;
}
.response-text, .hex-dump {
  margin: 0;
  padding: 10px 12px;
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: var(--vscode-editor-font-size, 13px);
  white-space: pre;
  overflow-x: auto;
}
.response-image { display: block; max-width: 100%; padding: 10px; }
.response-html { width: 100%; height: 70vh; border: 0; background: #fff; }
.response-binary { padding: 2px 0; }
.binary-note { padding: 8px 12px; font-style: italic; color: var(--vscode-descriptionForeground); }

/* JSON tree */
.response-json {
  padding: 10px 12px;
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: var(--vscode-editor-font-size, 13px);
  line-height: 1.5;
}
.json-entry { padding-left: 1.1em; }
.json-summary { color: var(--vscode-descriptionForeground); }
.json-summary::before {
  content: "\25B8";
  display: inline-block;
  width: 1.1em;
  opacity: 0.6;
  transition: transform 0.12s ease;
}
.json-node[open] > .json-summary::before { transform: rotate(90deg); }
.json-summary:hover { color: var(--vscode-foreground); }
.json-key    { color: var(--vscode-debugTokenExpression-name,    #9cdcfe); }
.json-string { color: var(--vscode-debugTokenExpression-string,  #ce9178); }
.json-number { color: var(--vscode-debugTokenExpression-number,  #b5cea8); }
.json-bool   { color: var(--vscode-debugTokenExpression-boolean, #569cd6); }
.json-null   { color: var(--vscode-debugTokenExpression-boolean, #569cd6); opacity: 0.85; }
"""

let private buildHtml (webview: Webview) (extensionUri: obj) : string =
    let scriptUri =
        webview.asWebviewUri (uri.joinPath (extensionUri, [| "dist"; "webview"; "main.js" |]))
        |> toUriString

    let n = nonce ()
    let styleTag = sprintf "<style nonce=\"%s\">%s</style>" n responseStyles

    sprintf
        """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src %s data:; style-src 'nonce-%s'; script-src 'nonce-%s';" />
<title>FsHttp.Studio: Response</title>
%s
</head>
<body>
<div id="root">Running…</div>
<script nonce="%s" src="%s"></script>
</body>
</html>"""
        webview.cspSource
        n
        n
        styleTag
        n
        scriptUri

/// Creates the single response viewer panel on first use, or reveals the existing one — never
/// more than one panel exists at a time (spec's "single response viewer panel").
let showBeside (extensionUri: obj) : WebviewPanel =
    match panel with
    | Some p ->
        p.reveal ()
        p
    | None ->
        let options: obj = nonNull (box {| enableScripts = true |})

        let p =
            window.createWebviewPanel (
                "fshttpStudio.responseViewer",
                "FsHttp.Studio: Response",
                viewColumnBeside,
                options
            )

        p.webview.html <- buildHtml p.webview extensionUri
        p.onDidDispose (fun () -> panel <- None) |> ignore
        panel <- Some p
        p

/// Posts a message into the panel's webview, a no-op if the panel has since been closed (a
/// stale in-flight Run completing after the user dismissed the panel).
let post (message: obj) : unit =
    match panel with
    | Some p -> p.webview.postMessage message
    | None -> ()
