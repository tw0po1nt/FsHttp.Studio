// The response viewer (issue #18, ADR-0001): a single webview panel, reused across runs,
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

let private buildHtml (webview: Webview) (extensionUri: obj) : string =
    let scriptUri =
        webview.asWebviewUri (uri.joinPath (extensionUri, [| "dist"; "webview"; "main.js" |]))
        |> toUriString

    let n = nonce ()

    sprintf
        """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src %s data:; script-src 'nonce-%s';" />
<title>FsHttp.Studio: Response</title>
</head>
<body>
<div id="root">Running…</div>
<script nonce="%s" src="%s"></script>
</body>
</html>"""
        webview.cspSource
        n
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
