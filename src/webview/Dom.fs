// The browser-only half of Seam B: materialise the renderer core's pure `Node` tree into real
// DOM. The *shape* is decided (and tested) in the shell-agnostic core; this only walks the tree
// creating elements, setting attributes, and appending text — the low-risk glue left to the
// manual smoke rather than an automated suite (Seam B's testing plan).
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

/// Turns a rendered `Node` tree into a detached DOM node ready to append into the panel.
let mount (node: Node) : Types.Node = mountNode node

/// Renders a response envelope and replaces `parent`'s contents with it — the one call the native
/// panel needs to paint a Run's result.
let renderInto (parent: HTMLElement) (env: ResponseEnvelope) : unit =
    parent.innerHTML <- ""
    parent.appendChild (mount (render env)) |> ignore
