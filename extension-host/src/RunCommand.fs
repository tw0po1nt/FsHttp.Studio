// The Run command a CodeLens click invokes (issue #18): opens/reveals the response viewer
// showing `Running…`, re-locates the block (a fresh locate/run pair per Run, matching the
// companion's own "fresh session per Run" philosophy) to pull its method/URL for the status
// line, then runs it and posts the outcome into the panel. Latest-click-wins: a generation
// counter discards a superseded run's result rather than racing it onto the panel.
module RunCommand

open Fable.Core.JsInterop
open Vscode
open Protocol

let mutable private handle: Companion.Handle option = None
let mutable private extensionUri: obj option = None
let mutable private generation = 0

let setHandle (h: Companion.Handle) = handle <- Some h
let setExtensionUri (u: obj) = extensionUri <- Some u

let private runningMessage: obj = createObj [ "tag" ==> "running" ]

let private errorMessage (message: string) : obj =
    createObj [ "tag" ==> "error"; "message" ==> message ]

let private resultMessage
    (method: string)
    (url: string)
    (elapsedMs: float)
    (status: int)
    (reason: string)
    (headers: (string * string) list)
    (contentType: string)
    (bodyBase64: string)
    : obj =
    let envelope: obj =
        createObj
            [ "method" ==> method
              "url" ==> url
              "status" ==> status
              "reason" ==> reason
              "headers" ==> (headers |> List.map (fun (k, v) -> [| k; v |]) |> List.toArray)
              "contentType" ==> contentType
              "bodyBase64" ==> bodyBase64
              "elapsedMs" ==> elapsedMs ]

    createObj [ "tag" ==> "result"; "envelope" ==> envelope ]

let private runOne (h: Companion.Handle) (document: TextDocument) (blockIndex: int) (myGeneration: int) : Async<unit> =
    async {
        let source = document.getText ()
        let! ranges = Companion.locate h source

        let method, url =
            match List.tryItem blockIndex ranges with
            | Some r -> extractMethodAndUrl (sliceRange source r)
            | None -> "", ""

        let started: float = emitJsExpr (nonNull (box 0)) "Date.now()"
        let! result = Companion.run h source blockIndex
        let elapsed: float = (emitJsExpr (nonNull (box 0)) "Date.now()") - started

        if myGeneration = generation then
            match result with
            | RunOk(status, reason, headers, contentType, bodyBase64) ->
                ResponseViewer.post (resultMessage method url elapsed status reason headers contentType bodyBase64)
            | RunCompileError diagnostics ->
                let text = diagnostics |> List.map (fun d -> d.Message) |> String.concat "\n"
                ResponseViewer.post (errorMessage (sprintf "Compile error:\n%s" text))
            | RunRuntimeError message -> ResponseViewer.post (errorMessage (sprintf "Runtime error: %s" message))
            | RunProtocolError message -> ResponseViewer.post (errorMessage message)
    }

/// Registers the command a `▶ Run request` CodeLens invokes, called with the same `TextDocument`
/// the lens was computed against and the block's 0-based index into that document's located
/// blocks (matching `CodeLensProvider.fs`'s `arguments`).
let register () : Disposable =
    commands.registerCommand (
        CodeLensProvider.commandId,
        System.Action<obj, obj>(fun documentArg indexArg ->
            match handle with
            | None -> ()
            | Some h ->
                let document = unbox<TextDocument> documentArg
                let blockIndex = unbox<int> indexArg

                generation <- generation + 1
                let myGeneration = generation

                match extensionUri with
                | Some u -> ResponseViewer.showBeside u |> ignore
                | None -> ()

                ResponseViewer.post runningMessage

                runOne h document blockIndex myGeneration |> Async.StartImmediate)
    )
