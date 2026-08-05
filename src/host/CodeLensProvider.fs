// The `▶ Run request` CodeLens (ADR-0003). It shows one lens for each block that the companion
// locates in a `.fsx` script. It shows no lens on a `.fs` file, and no lens while the companion
// is down. That is ADR-0003's "no companion, no lenses" by construction. The status-bar item
// makes the state legible, so it does not look like a silent failure.
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

let private emitter = EventEmitter<unit>()

let mutable private handle: Companion.Handle option = None
let mutable private ready = false

/// Called after `Extension.fs` spawns the companion, so `provideCodeLenses` has a target for
/// its `locate` requests.
let setHandle (h: Companion.Handle) = handle <- Some h

/// Called on every companion state transition. It fires `onDidChangeCodeLenses` only on a real
/// change between ready and not-ready, so VSCode does not re-query on an unrelated status tick.
let setReady (isReady: bool) =
    if isReady <> ready then
        ready <- isReady
        emitter.fire ()

let private buildCodeLens (document: TextDocument) (i: int) (r: BlockRange) : CodeLens =
    let line = float (toVscodeLine r.StartLine)
    let col = float r.StartCol
    let range = Range(line, col, line, col)

    let title, command =
        match r.Refusal with
        | Some code -> (Refusals.forCode code).Title, explainCommandId
        | None -> "▶ Run request", commandId

    let commandObj: obj =
        createObj
            [ "title" ==> title
              "command" ==> command
              "arguments" ==> [| box document; box i |] ]

    CodeLens(range, commandObj)

let private noLenses () : Async<ResizeArray<CodeLens>> = async { return ResizeArray() }

let provider: CodeLensProvider =
    { new CodeLensProvider with
        member _.onDidChangeCodeLenses = emitter.event

        member _.provideCodeLenses(document, _token) =
            let computation =
                if not ready || not (document.fileName.EndsWith(".fsx")) then
                    noLenses ()
                else
                    match handle with
                    | None -> noLenses ()
                    | Some h ->
                        async {
                            let! ranges = Companion.locate h (document.getText ())
                            return ranges |> List.mapi (buildCodeLens document) |> ResizeArray
                        }

            Async.StartAsPromise computation }
