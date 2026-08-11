// The browser-only half of Seam B. It materializes the renderer core's pure `Node` tree into
// real DOM. The shell-agnostic core decides and tests the *shape*. This module only walks the
// tree, creates elements, sets attributes, and appends text. It also owns the one delegated
// click listener that drives the copy buttons: the payload stays in `copyText`, and this module
// only writes it to the clipboard and flashes the button label
// (docs/spec/0013-copy-buttons.md, Decisions 8 and 10).
module Webview.Dom

open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types
open Renderer.Core

let mutable private current: ResponseEnvelope option = None
let mutable private attached = false

let rec private mountNode (node: Node) : Types.Node =
    match node with
    | Node.Text text -> document.createTextNode text :> Types.Node
    | Node.Element(tag, attrs, children) ->
        let element = document.createElement tag

        for name, value in attrs do
            element.setAttribute (name, value)

        for child in children do
            element.appendChild (mountNode child) |> ignore

        element :> Types.Node

/// Turns a rendered `Node` tree into a detached DOM node, ready to append into the panel.
let mount (node: Node) : Types.Node = mountNode node

/// Puts a short-lived label on the button, then restores the previous text. Success and failure
/// both use this path, so the user never sees a silent clipboard outcome
/// (docs/spec/0013-copy-buttons.md, Decision 8).
let private flash (button: HTMLElement) (text: string) =
    let original = button.textContent
    button.textContent <- text
    window.setTimeout ((fun () -> button.textContent <- original), 1200) |> ignore

/// `navigator.clipboard` is not in Fable's Browser.Navigator bindings. Reach it the same way
/// `ResponseViewer.nonce` reaches `crypto.getRandomValues`.
let private writeClipboard (text: string) (onOk: unit -> unit) (onFail: unit -> unit) : unit =
    emitJsExpr (text, onOk, onFail) "navigator.clipboard.writeText($0).then($1, $2)"

/// The nearest `[data-copy]` ancestor of the event target, including the target itself. A click
/// on the button's text node has no `closest`, so climb to the parent element first.
let private copyButtonFromEvent (ev: Event) : HTMLElement option =
    match box ev.target with
    | null -> None
    | target ->
        let matched: objnull =
            emitJsExpr target "($0.nodeType === 3 ? $0.parentElement : $0).closest('[data-copy]')"

        match matched with
        | null -> None
        | button -> Some(unbox<HTMLElement> button)

let private handleClick (ev: Event) =
    match current, copyButtonFromEvent ev with
    | Some env, Some button ->
        match copyText env (button.getAttribute "data-copy") with
        | Some text -> writeClipboard text (fun () -> flash button "Copied") (fun () -> flash button "Copy failed")
        | None -> ()
    | _ -> ()

/// Renders a response envelope, and replaces the contents of `parent` with it. This is the one
/// call that the native panel needs to paint a Run's result. The click listener attaches once to
/// `parent` and survives every re-render, because `innerHTML` only replaces the children.
let renderInto (parent: HTMLElement) (env: ResponseEnvelope) : unit =
    current <- Some env

    if not attached then
        parent.addEventListener ("click", handleClick)
        attached <- true

    parent.innerHTML <- ""
    parent.appendChild (mount (render env)) |> ignore
