module StatusBar

open Vscode
open Protocol

let mutable private item: StatusBarItem option = None
let mutable private companionState: State = Starting
let mutable private scriptView: ScriptView = NoFSharpDocument

/// Writes the status-bar body, or hides the item when `statusText` has nothing to say
/// A no-op until `register` hands over the item.
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
/// reports what the active document contains rather than the retired `ready` word.
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

/// Follows the active document. `None` is a workbench with no active text editor at all, such
/// as one where the response viewer has focus. That hides the item on the same terms as a
/// non-F# document.
let onActiveEditorChanged (editor: TextEditor option) =
    match editor with
    | None -> setScriptView NoFSharpDocument
    | Some active -> setScriptView (scriptViewFor active.document)

/// Mirrors a `locate` response onto the status bar when `Protocol.mirrorsActiveDocument` says it
/// belongs to the active document. This function is the interop half, and it reads the active
/// editor.
let onLocated (document: TextDocument) (view: ScriptView) =
    let activeFileName =
        window.activeTextEditor |> Option.map (fun editor -> editor.document.fileName)

    if mirrorsActiveDocument activeFileName document.fileName then
        setScriptView view
