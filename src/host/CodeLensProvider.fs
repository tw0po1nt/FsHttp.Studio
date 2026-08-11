// The `▶ Run request` CodeLens (ADR-0003). It shows one lens for each block that the companion
// locates in a `.fsx` script, and no lens at all on a `.fs` file.
//
// A companion that is gone changes what the lenses say, and does not clear them. Every block the
// last locate found keeps a lens reading `⊘ Cannot run: the companion stopped`. That is ADR-0003's
// "no companion, no *runnable* lenses". A lens that still promised a Run would promise what
// nothing can keep. A lens that vanished would read as a failure to find the block
// (docs/spec/0003-lens-tells-the-truth.md, user story 4). The status-bar item reports the same
// state, so neither surface contradicts the other.
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
/// under this id. Not declared in `package.json`'s `contributes.commands` (docs/spec/0003,
/// Decision 8): it needs to work for a lens click only, and never appear in the command palette.
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
                if not (document.fileName.EndsWith(".fsx")) then
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

                            // Re-read `ready` after the await. A companion that exits with this
                            // locate in flight abandons it to an empty list
                            // (docs/spec/0004-run-path-robustness.md, Decision 6), and that empty
                            // list is not a reading of the script. Storing it would erase the
                            // ranges the stopped lenses stand on, and returning it would paint
                            // the vanishing that this provider no longer does. The exit handler
                            // sets the state before it flushes the queue, so `ready` is already
                            // false here when that happens.
                            if ready then
                                lastLocated.[document.fileName] <- ranges
                                return ranges |> List.mapi (buildCodeLens document) |> ResizeArray
                            else
                                return stoppedLenses document
                        }

            Async.StartAsPromise computation }
