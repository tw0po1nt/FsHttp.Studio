// The Run command that a CodeLens click invokes. It opens or reveals the response viewer, which
// shows `Running…`. It then runs the block and posts the outcome into the panel. Method and URL
// for the status line arrive on the run result, so this path does not locate the block again
// (docs/spec/0012-request-as-sent.md, Decision 11). The latest click wins: a generation counter
// discards the result of a superseded Run, instead of a race onto the panel.
module RunCommand

open Fable.Core.JsInterop
open Vscode
open Protocol

let mutable private handle: Companion.Handle option = None
let mutable private extensionUri: obj option = None
let mutable private generation = 0

let setHandle (h: Companion.Handle) = handle <- Some h
let setExtensionUri (u: obj) = extensionUri <- Some u

let private runningUpdate: obj = createObj [ "tag" ==> "running" ]

let private errorUpdate (message: string) : obj =
    createObj [ "tag" ==> "error"; "message" ==> message ]

let private refusedUpdate (title: string) (detail: string) : obj =
    createObj [ "tag" ==> "refused"; "title" ==> title; "detail" ==> detail ]

/// The shipped default for `fshttpStudio.requestTimeoutMs`. Used when the setting is missing
/// or not a finite number, so a corrupt config cannot leave the Run unbounded by accident.
[<Literal>]
let private defaultRequestTimeoutMs = 30000

/// Reads `fshttpStudio.requestTimeoutMs` for this Run. A change to the setting applies on the
/// next click with no window reload. `0` means do not inject a bound
/// (docs/spec/0004-run-path-robustness.md, Decision 1).
let private configuredRequestTimeoutMs () : int =
    let n = (workspace.getConfiguration "fshttpStudio").getNumber "requestTimeoutMs"
    let finite: bool = emitJsExpr n "Number.isFinite($0)"

    if finite && n >= 0.0 then
        int n
    else
        defaultRequestTimeoutMs

/// The two numbers the status line shows. They are both durations in milliseconds, so a bare
/// pair of floats side by side in a parameter list can be transposed with no compiler help.
/// `RequestMs` is the companion's invocation bracket, and `TotalMs` is this module's bracket
/// around `Companion.run` (docs/spec/0004-run-path-robustness.md, Decision 7).
type private Timing = { RequestMs: float; TotalMs: float }

let private resultUpdate
    (method: string)
    (url: string)
    (timing: Timing)
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
              "requestMs" ==> timing.RequestMs
              "totalMs" ==> timing.TotalMs ]

    createObj [ "tag" ==> "result"; "envelope" ==> envelope ]

let private runOne (h: Companion.Handle) (document: TextDocument) (blockIndex: int) (myGeneration: int) : Async<unit> =
    async {
        let source = document.getText ()
        // A `file`-scheme script's `fileName` is the absolute path FSI needs for
        // `__SOURCE_DIRECTORY__`. Anything else has no real local path, so the Run sends none.
        let scriptFileName = scriptFileNameFor document.uri.scheme document.fileName

        let started: float = emitJsExpr (nonNull (box 0)) "Date.now()"
        let timeoutMs = configuredRequestTimeoutMs ()
        let! result = Companion.run h source blockIndex scriptFileName timeoutMs
        let totalMs: float = (emitJsExpr (nonNull (box 0)) "Date.now()") - started

        if myGeneration = generation then
            match result with
            | RunOk(request, response) ->
                let timing =
                    { RequestMs = response.RequestMs
                      TotalMs = totalMs }

                ResponseViewer.post (
                    resultUpdate
                        request.Method
                        request.Url
                        timing
                        response.Status
                        response.Reason
                        response.Headers
                        response.ContentType
                        response.BodyBase64
                )
            | RunCompileError diagnostics -> ResponseViewer.post (errorUpdate (formatCompileError diagnostics))
            | RunRuntimeError message -> ResponseViewer.post (errorUpdate (sprintf "Runtime error: %s" message))
            | RunProtocolError message -> ResponseViewer.post (errorUpdate message)
            // `Refusals` is the one module that owns every shipped refusal sentence
            // (docs/spec/0003, Decision 2), including which codes its `catalog` does not carry.
            | RunRefused(code, name) ->
                let refusal = Refusals.forRefused code name
                ResponseViewer.post (refusedUpdate refusal.Title refusal.Detail)
    }

/// Registers the command that a `▶ Run request` CodeLens invokes. The caller passes the same
/// `TextDocument` that the lens was computed against, and the block's 0-based index into that
/// document's located blocks. These match the `arguments` in `CodeLensProvider.fs`.
let register () : Disposable =
    commands.registerCommand (
        CodeLensProvider.commandId,
        System.Action<obj, obj>(fun documentArg indexArg ->
            match (documentArg, indexArg, handle) with
            | null, _, _
            | _, null, _
            | _, _, None -> ()
            | doc, idx, Some h ->
                let document = unbox<TextDocument> doc
                let blockIndex = unbox<int> idx

                generation <- generation + 1
                let myGeneration = generation

                match extensionUri with
                | Some u -> ResponseViewer.showBeside u |> ignore
                | None -> ()

                ResponseViewer.post runningUpdate

                runOne h document blockIndex myGeneration |> Async.StartImmediate)
    )

/// Registers the command that a `⊘ Cannot run: …` CodeLens invokes (docs/spec/0003, Decision 8).
/// It reads the block's own refusal code and shows a warning toast with the matching text. It
/// touches neither the response viewer nor the generation counter: no Run was ever going to
/// start.
let registerExplain () : Disposable =
    commands.registerCommand (
        CodeLensProvider.explainCommandId,
        System.Action<obj, obj>(fun documentArg indexArg ->
            match (documentArg, indexArg, handle) with
            | null, _, _
            | _, null, _
            | _, _, None -> ()
            | doc, idx, Some h ->
                let document = unbox<TextDocument> doc
                let blockIndex = unbox<int> idx

                async {
                    let! ranges = Companion.locate h (document.getText ())

                    match List.tryItem blockIndex ranges |> Option.bind (fun r -> r.Refusal) with
                    | Some code -> window.showWarningMessage ((Refusals.forCode code).Detail) |> ignore
                    | None -> ()
                }
                |> Async.StartImmediate)
    )

/// Registers the command that a `⊘ Cannot run: the companion stopped` CodeLens invokes
/// (ADR-0003). It reads nothing and asks nothing: the companion that a locate would go to is the
/// process that stopped, and the lens already knows the only thing left to say. It takes the
/// lens arguments and ignores them, because every lens passes the pair.
let registerExplainCompanionStopped () : Disposable =
    commands.registerCommand (
        CodeLensProvider.explainStoppedCommandId,
        System.Action<obj, obj>(fun _documentArg _indexArg ->
            window.showWarningMessage Refusals.companionStopped.Detail |> ignore)
    )
