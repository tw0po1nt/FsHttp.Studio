// Black-box helpers for asserting the *shape* of a rendered `Node` tree: find elements by tag or
// class, read attributes, and flatten text — the vocabulary that checks "the JSON path dispatched
// to a tree", "the image is an <img> with a data: src", and so on, without reaching into the
// renderer's internals. It lives beside the core (not in a test project) so both consumers — the
// .NET Seam-B Expecto suite and the Fable JS runtime smoke — share one copy. Referenced only by
// those two, it is tree-shaken from the shipped webview bundle.
module Renderer.NodeQuery

open Renderer.Core

/// Every element in the tree, pre-order, including the root.
let rec descendants (node: Node) : Node list =
    match node with
    | Node.Text _ -> []
    | Node.Element(_, _, children) -> node :: List.collect descendants children

let tag (node: Node) : string option =
    match node with
    | Node.Element(t, _, _) -> Some t
    | Node.Text _ -> None

let attr (name: string) (node: Node) : string option =
    match node with
    | Node.Element(_, attrs, _) -> attrs |> List.tryPick (fun (n, v) -> if n = name then Some v else None)
    | Node.Text _ -> None

let private classes (node: Node) : string list =
    match attr "class" node with
    | Some value -> value.Split(' ') |> Array.filter (fun s -> s <> "") |> Array.toList
    | None -> []

let hasClass (cls: string) (node: Node) : bool = classes node |> List.contains cls

/// All elements with the given tag, anywhere in the tree.
let byTag (t: string) (node: Node) : Node list =
    descendants node |> List.filter (fun n -> tag n = Some t)

/// All elements carrying the given CSS class, anywhere in the tree.
let byClass (cls: string) (node: Node) : Node list =
    descendants node |> List.filter (hasClass cls)

/// The concatenated text of a node and everything beneath it.
let rec innerText (node: Node) : string =
    match node with
    | Node.Text t -> t
    | Node.Element(_, _, children) -> children |> List.map innerText |> String.concat ""
