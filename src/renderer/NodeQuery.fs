module Renderer.NodeQuery

open Renderer.Core

/// Every element in the tree, in pre-order, and including the root.
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

let byTag (t: string) (node: Node) : Node list =
    descendants node |> List.filter (fun n -> tag n = Some t)

let byClass (cls: string) (node: Node) : Node list =
    descendants node |> List.filter (hasClass cls)

let rec innerText (node: Node) : string =
    match node with
    | Node.Text t -> t
    | Node.Element(_, _, children) -> children |> List.map innerText |> String.concat ""
