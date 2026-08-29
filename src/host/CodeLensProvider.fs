module CodeLensProvider

open Fable.Core
open Fable.Core.JsInterop
open Vscode
open Protocol

/// The command that a click on a runnable lens invokes. `RunCommand.fs` registers the handler
/// under this id.
[<Literal>]
let commandId = "fshttpStudio.runBlock"

/// The command that a click on a refused lens invokes. `RunCommand.fs` registers the handler
/// under this id. Deliberately absent from `package.json`'s `contributes.commands`, so it stays
/// out of the command palette.
[<Literal>]
let explainCommandId = "fshttpStudio.explainBlockRefusal"

/// The command that a click on a stopped-companion lens invokes. It is separate from
/// `explainCommandId` because that handler asks the companion to locate the block again, and the
/// companion is exactly what is missing here. Not declared in `package.json`'s
/// `contributes.commands`, for the same reason `explainCommandId` is not.
[<Literal>]
let explainStoppedCommandId = "fshttpStudio.explainCompanionStopped"

let private emitter = EventEmitter<unit>()

let mutable private handle: Companion.Handle option = None
let mutable private ready = false

/// `Extension` owns the status bar item and decides whether the document is still the active
/// editor's document.
let mutable private onLocated: (TextDocument -> ScriptView -> unit) option = None

/// The ranges the last successful `locate` returned for each script, keyed by the document's own
/// file name. A stopped companion's lenses stand on this. A locate needs the companion, so the
/// only positions left once the companion is gone are the ones it already reported.
///
/// A script that no locate ever covered has no entry here, and therefore no lens. A companion
/// that is still starting has answered no locate, so it paints nothing and no lens flickers on
/// the way to ready.
///
/// A ready companion never reads this table, because it locates the script again on every query.
/// The entries therefore only serve the *next* stop, and `setReady true` clears all of them.
/// Nothing else removes an entry. What a session holds is one range list for each script that the
/// user opened since the companion last became ready, which is small.
let private lastLocated =
    System.Collections.Generic.Dictionary<string, BlockRange list>()

/// Called after `Extension.fs` spawns the companion, so `provideCodeLenses` has a target for
/// its `locate` requests.
let setHandle (h: Companion.Handle) = handle <- Some h

/// Called from `Extension.fs` so each `locate` response can refresh the status bar without this
/// module touching the item.
let setOnLocated (callback: TextDocument -> ScriptView -> unit) = onLocated <- Some callback

let private reportLocated (document: TextDocument) (view: ScriptView) =
    match onLocated with
    | Some callback -> callback document view
    | None -> ()

/// Called on every companion state transition. It fires `onDidChangeCodeLenses` only on a real
/// change between ready and not-ready, so VSCode does not re-query on an unrelated status tick.
///
/// Becoming ready clears the remembered ranges. They came from a companion that is gone, and a
/// live one answers for itself. Keeping them would let a stop after this point paint lenses from
/// a parse two companions old.
let setReady (isReady: bool) =
    if isReady <> ready then
        ready <- isReady

        if isReady then
            lastLocated.Clear()

        emitter.fire ()

/// One lens at a block's own start, carrying the given title and the command a click invokes.
/// Every lens this module paints goes through here, so a title can never arrive at a position
/// that a different lens computed.
let private lensAt (document: TextDocument) (i: int) (r: BlockRange) (title: string) (command: string) : CodeLens =
    let line = float (toVscodeLine r.StartLine)
    let col = float r.StartCol
    let range = Range(line, col, line, col)

    let commandObj: obj =
        createObj
            [ "title" ==> title
              "command" ==> command
              "arguments" ==> [| box document; box i |] ]

    CodeLens(range, commandObj)

let private buildCodeLens (document: TextDocument) (i: int) (r: BlockRange) : CodeLens =
    match r.Refusal with
    | Some code -> lensAt document i r (Refusals.lensTitle code) explainCommandId
    | None -> lensAt document i r "▶ Run request" commandId

/// An empty command id makes VSCode paint the title as plain text, so a click runs nothing.
let private plainTextLens (title: string) : CodeLens =
    let range = Range(0.0, 0.0, 0.0, 0.0)

    let commandObj: obj = createObj [ "title" ==> title; "command" ==> "" ]

    CodeLens(range, commandObj)

let private noLenses () : Async<ResizeArray<CodeLens>> = async { return ResizeArray() }

/// The answer for a script while the companion is gone: one stopped lens for each block the last
/// locate remembered, and no lens at all for a script no locate ever covered.
///
/// Every remembered block reads the same way here. A block's own refusal is still true, but it is
/// no longer the reason the user cannot run: nothing in this script can run, whatever its
/// position, and the one action that changes that is a reload of the window.
let private stoppedLenses (document: TextDocument) : ResizeArray<CodeLens> =
    match lastLocated.TryGetValue document.fileName with
    | true, ranges ->
        ranges
        |> List.mapi (fun i r -> lensAt document i r Refusals.companionStoppedLensTitle explainStoppedCommandId)
        |> ResizeArray
    | _ -> ResizeArray()

let provider: CodeLensProvider =
    { new CodeLensProvider with
        member _.onDidChangeCodeLenses = emitter.event

        member _.provideCodeLenses(document, _token) =
            let computation =
                if not (isScriptFileName document.fileName) then
                    noLenses ()
                elif not ready then
                    async { return stoppedLenses document }
                else
                    match handle with
                    | None -> noLenses ()
                    | Some h ->
                        async {
                            let! located = Companion.locate h (document.getText ())
                            let ranges = located.Ranges

                            // A companion that exits mid-locate abandons this call to an empty
                            // list, which is not a reading of the script.
                            if ready then
                                lastLocated.[document.fileName] <- ranges

                                let view = Script(ranges.Length, located.ParseFailed)
                                reportLocated document view

                                match noRequestsLensTitle view with
                                | Some title -> return ResizeArray([ plainTextLens title ])
                                | None -> return ranges |> List.mapi (buildCodeLens document) |> ResizeArray
                            else
                                return stoppedLenses document
                        }

            Async.StartAsPromise computation }
