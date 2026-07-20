// The `▶ Run request` CodeLens (ADR-0003): one lens per block the companion locates
// in a `.fsx` script, and none at all on `.fs` or while the companion is down — ADR-0003's "no
// companion ⇒ no lenses" by construction, made legible by the status-bar item rather than
// looking like silent breakage.
module CodeLensProvider

open Fable.Core
open Fable.Core.JsInterop
open Vscode
open Protocol

/// The command a lens's click invokes; `RunCommand.fs` registers the handler under this id and
/// `package.json`'s `contributes.commands` declares it.
[<Literal>]
let commandId = "fshttpStudio.runBlock"

let private emitter = EventEmitter<unit>()

let mutable private handle: Companion.Handle option = None
let mutable private ready = false

/// Called once `Extension.fs` has spawned the companion, so `provideCodeLenses` has something
/// to send `locate` requests to.
let setHandle (h: Companion.Handle) = handle <- Some h

/// Called on every companion state transition. Fires `onDidChangeCodeLenses` only on an actual
/// ready/not-ready flip, so VSCode doesn't re-query on every unrelated status tick.
let setReady (isReady: bool) =
    if isReady <> ready then
        ready <- isReady
        emitter.fire ()

let private buildCodeLens (document: TextDocument) (i: int) (r: BlockRange) : CodeLens =
    let line = float (toVscodeLine r.StartLine)
    let col = float r.StartCol
    let range = Range(line, col, line, col)

    let command: obj =
        createObj
            [ "title" ==> "▶ Run request"
              "command" ==> commandId
              "arguments" ==> [| box document; box i |] ]

    CodeLens(range, command)

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
