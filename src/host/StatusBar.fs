// The status-bar item, and the two pieces of state it reports: the companion's lifecycle and
// what the active document holds (docs/spec/0014-explain-missing-lenses.md, Decisions 5-6).
//
// It lives apart from `Extension.fs` because it changes for its own reasons — a new row in
// `Protocol.statusText`, a new tell about the active document — while activation changes for the
// SDK probe, the commands, and the provider registration. `Extension.activate` creates the item,
// hands it here, and wires the two callbacks; nothing else in the extension touches the item.
module StatusBar

open Vscode
open Protocol

let mutable private item: StatusBarItem option = None
let mutable private companionState: State = Starting
let mutable private scriptView: ScriptView = NoFSharpDocument

/// Writes the status-bar body, or hides the item when `statusText` has nothing to say
/// (Decision 6). A no-op until `register` hands over the item.
let private setStatusText (text: string option) =
    match item, text with
    | Some bar, Some body ->
        bar.text <- "FsHttp.Studio: " + body
        bar.show ()
    | Some bar, None -> bar.hide ()
    | None, _ -> ()

let private refreshStatus () =
    setStatusText (statusText companionState scriptView)

/// Takes over the item. Call before the first state write: every write until then is a no-op, so
/// an item registered late reports nothing about the states it missed.
let register (bar: StatusBarItem) = item <- Some bar

/// The status bar for a companion state. Keeps the last script view, so a Ready transition
/// reports what the active document holds rather than the retired `ready` word.
let setCompanionState (state: State) =
    companionState <- state
    refreshStatus ()

let private setScriptView (view: ScriptView) =
    scriptView <- view
    refreshStatus ()

/// What the status bar should show for an active document before any `locate` response arrives.
let private scriptViewFor (document: TextDocument) : ScriptView =
    if document.languageId <> "fsharp" then
        NoFSharpDocument
    elif not (isScriptFileName document.fileName) then
        NotAScript
    else
        ScriptPending

/// Follows the active document. `None` is a workbench with no active text editor at all — the
/// response viewer holds focus, say — which hides the item on the same terms as a non-F#
/// document (Decision 6).
let onActiveEditorChanged (editor: TextEditor option) =
    match editor with
    | None -> setScriptView NoFSharpDocument
    | Some active -> setScriptView (scriptViewFor active.document)

/// Mirrors a `locate` response onto the status bar when `Protocol.mirrorsActiveDocument` says it
/// belongs to the active document. This function is the interop half — reading the active editor
/// — and the rule it applies is pinned in `tests/host.Tests`.
let onLocated (document: TextDocument) (view: ScriptView) =
    let activeFileName =
        window.activeTextEditor |> Option.map (fun editor -> editor.document.fileName)

    if mirrorsActiveDocument activeFileName document.fileName then
        setScriptView view
