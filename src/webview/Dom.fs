// The browser-only half of Seam B. It materializes the renderer core's pure `Node` tree into
// real DOM. The shell-agnostic core decides and tests the *shape*. This module only walks the
// tree, creates elements, sets attributes, and appends text. It is the low-risk glue that Seam
// B's testing plan leaves to the manual smoke, and not to an automated suite.
module Webview.Dom

open Browser
open Browser.Types
open Renderer.Core

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

/// Renders a response envelope, and replaces the contents of `parent` with it. This is the one
/// call that the native panel needs to paint a Run's result.
let renderInto (parent: HTMLElement) (env: ResponseEnvelope) : unit =
    parent.innerHTML <- ""
    parent.appendChild (mount (render env)) |> ignore
