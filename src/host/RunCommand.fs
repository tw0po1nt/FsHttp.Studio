module RunCommand

open Fable.Core
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
/// next click with no window reload. `0` means do not inject a bound.
let private configuredRequestTimeoutMs () : int =
    let n = (workspace.getConfiguration "fshttpStudio").getNumber "requestTimeoutMs"
    let finite: bool = emitJsExpr n "Number.isFinite($0)"

    if finite && n >= 0.0 then
        int n
    else
        defaultRequestTimeoutMs

/// Two millisecond durations. A bare float pair in a parameter list can be transposed with no
/// compiler help.
type private Timing = { RequestMs: float; TotalMs: float }

/// Headers as the viewer update carries them: an array of two-element `[name; value]` arrays.
/// The request half and the response half of the update use the same shape, so they read it back
/// through one `toHeaders` in the webview and must be written by one function here.
let private headersToWire (headers: (string * string) list) : string[][] =
    headers |> List.map (fun (name, value) -> [| name; value |]) |> List.toArray

/// The three-state body fields the webview's `toEnvelope` reads. Mirrors the
/// `bodyState` / `bodyBase64` / `bodyReason` triple on the companion wire, and spells the state
/// names through `Protocol`, which is where this end of the host names them once.
let private requestBodyFields (body: CapturedBody) : string * string * string =
    match body with
    | NoBody -> Protocol.NoneState, "", ""
    | Captured bytes -> Protocol.CapturedState, System.Convert.ToBase64String bytes, ""
    | NotCaptured reason -> Protocol.NotCapturedState, "", reason

let private resultUpdate (request: RequestData) (timing: Timing) (response: ResponseData) : obj =
    let bodyState, bodyBase64, bodyReason = requestBodyFields request.Body

    let requestObj: obj =
        createObj
            [ "method" ==> request.Method
              "url" ==> request.Url
              "headers" ==> headersToWire request.Headers
              "contentType" ==> Protocol.requestContentType request.Headers
              "bodyState" ==> bodyState
              "bodyBase64" ==> bodyBase64
              "bodyReason" ==> bodyReason ]

    let envelope: obj =
        createObj
            [ "request" ==> requestObj
              "status" ==> response.Status
              "reason" ==> response.Reason
              "headers" ==> headersToWire response.Headers
              "contentType" ==> response.ContentType
              "bodyBase64" ==> response.BodyBase64
              "requestMs" ==> timing.RequestMs
              "totalMs" ==> timing.TotalMs ]

    createObj [ "tag" ==> "result"; "envelope" ==> envelope ]

let mutable private companionState = Starting

let private runOne
    (h: Companion.Handle)
    (document: TextDocument)
    (source: string)
    (blockIndex: int)
    (myGeneration: int)
    : Async<unit> =
    async {
        // Only a `file`-scheme script has the absolute path FSI needs for `__SOURCE_DIRECTORY__`.
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

                ResponseViewer.post (resultUpdate request timing response)
            | RunCompileError diagnostics ->
                ResponseViewer.post (errorUpdate (formatCompileError scriptFileName diagnostics))
            | RunRuntimeError message -> ResponseViewer.post (errorUpdate (sprintf "Runtime error: %s" message))
            | RunProtocolError message -> ResponseViewer.post (errorUpdate message)
            | RunRefused(code, name) ->
                let refusal = Refusals.forRefused code name
                ResponseViewer.post (refusedUpdate refusal.Title refusal.Detail)
    }

let private startRun (h: Companion.Handle) (document: TextDocument) (source: string) (blockIndex: int) =
    generation <- generation + 1
    let myGeneration = generation

    match extensionUri with
    | Some u -> ResponseViewer.showBeside u |> ignore
    | None -> ()

    ResponseViewer.post runningUpdate

    runOne h document source blockIndex myGeneration |> Async.StartImmediate

let private showRefusalToast (code: string) =
    window.showWarningMessage ((Refusals.forCode code).Detail) |> ignore

let private showCompanionStoppedToast () =
    window.showWarningMessage Refusals.companionStopped.Detail |> ignore

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

                startRun h document (document.getText ()) blockIndex)
    )

let private runOrRefuse
    (h: Companion.Handle)
    (document: TextDocument)
    (source: string)
    (ranges: BlockRange list)
    (i: int)
    =
    match ranges.[i].Refusal with
    | Some code -> showRefusalToast code
    | None -> startRun h document source i

/// The label that `:FsHttp run` writes: `<glyph> <line>: <first source line>`.
let private quickPickItem (sourceLines: string[]) (r: BlockRange) : obj =
    let firstLine =
        if r.StartLine >= 1 && r.StartLine <= sourceLines.Length then
            sourceLines.[r.StartLine - 1].Trim()
        else
            ""

    let label glyph =
        sprintf "%s %d: %s" glyph r.StartLine firstLine

    match r.Refusal with
    | Some code ->
        createObj
            [ "label" ==> label Refusals.refusalGlyph
              "detail" ==> (Refusals.forCode code).Title ]
    | None -> createObj [ "label" ==> label Refusals.runGlyph ]

let private pickBlock (h: Companion.Handle) (document: TextDocument) (source: string) (ranges: BlockRange list) =
    let sourceLines = source.Replace("\r\n", "\n").Split('\n')
    let items = ranges |> List.map (quickPickItem sourceLines) |> List.toArray

    Js.onResolved (window.showQuickPick items) (fun picked ->
        match picked with
        | null -> ()
        | _ ->
            items
            |> Array.findIndex (fun item -> obj.ReferenceEquals(item, picked))
            |> runOrRefuse h document source ranges)

[<Literal>]
let runAtCursorCommandId = "fshttpStudio.runRequestAtCursor"

/// The editor text and the 1-based line of the primary cursor at the call.
[<NoComparison>]
type private CursorSnapshot =
    { Document: TextDocument
      Source: string
      CursorLine: int }

let private takeSnapshot (editor: TextEditor) : CursorSnapshot =
    { Document = editor.document
      Source = editor.document.getText ()
      CursorLine = editor.selection.active.line + 1 }

let private runAtCursor (h: Companion.Handle) (snapshot: CursorSnapshot) : Async<unit> =
    async {
        let! located = Companion.locate h snapshot.Source

        match located.Ranges, located.ParseFailed with
        | [], true -> window.showWarningMessage Refusals.noBlocksParseFailure |> ignore
        | [], false -> window.showInformationMessage Refusals.noBlocksEmpty |> ignore
        | ranges, _ ->
            match blockAtCursor snapshot.CursorLine ranges with
            | Some i -> runOrRefuse h snapshot.Document snapshot.Source ranges i
            | None -> pickBlock h snapshot.Document snapshot.Source ranges
    }

let mutable private wait: (CursorSnapshot * (unit -> unit)) option = None

let private endWait () =
    match wait with
    | Some(_, closeNotification) ->
        wait <- None
        closeNotification ()
    | None -> ()

let private waitNotificationOptions: obj =
    createObj
        [ "location" ==> progressLocationNotification
          "title" ==> "Waiting for the FsHttp.Studio companion to start"
          "cancellable" ==> true ]

let private waitForCompanion (snapshot: CursorSnapshot) =
    match wait with
    | Some(_, closeNotification) -> wait <- Some(snapshot, closeNotification)
    | None ->
        let closed, closeNotification = Js.deferred<unit> ()
        wait <- Some(snapshot, closeNotification)

        window.withProgress (
            waitNotificationOptions,
            System.Func<obj, CancellationToken, JS.Promise<unit>>(fun _ token ->
                token.onCancellationRequested (fun _ -> endWait ()) |> ignore
                closed)
        )
        |> ignore

/// Activation shows the SDK toast on SdkNotFound, so the wait shows no toast in that state.
let setCompanionState (state: State) =
    companionState <- state

    match wait, state, handle with
    | Some(snapshot, _), Ready, Some h ->
        endWait ()
        runAtCursor h snapshot |> Async.StartImmediate
    | Some _, Ready, None
    | Some _, SdkNotFound, _ -> endWait ()
    | Some _, Stopped, _ ->
        endWait ()
        showCompanionStoppedToast ()
    | _ -> ()

/// `showNoSdk` is the SDK toast of activation, which this module compiles before.
let registerRunAtCursor (showNoSdk: unit -> unit) : Disposable =
    commands.registerCommand (
        runAtCursorCommandId,
        System.Action<obj, obj>(fun _ _ ->
            match window.activeTextEditor with
            | Some editor when isScriptFileName editor.document.fileName ->
                match companionState, handle with
                | SdkNotFound, _ -> showNoSdk ()
                | Stopped, _ -> showCompanionStoppedToast ()
                | Starting, _ -> waitForCompanion (takeSnapshot editor)
                | Ready, Some h -> runAtCursor h (takeSnapshot editor) |> Async.StartImmediate
                | Ready, None -> ()
            | _ -> window.showInformationMessage Refusals.runAtCursorNeedsScript |> ignore)
    )

/// Touches neither the response viewer nor the generation counter, because no Run starts.
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
                    let! located = Companion.locate h (document.getText ())

                    match List.tryItem blockIndex located.Ranges |> Option.bind (fun r -> r.Refusal) with
                    | Some code -> showRefusalToast code
                    | None -> ()
                }
                |> Async.StartImmediate)
    )

/// Takes the lens arguments and ignores them, because every lens passes the pair.
let registerExplainCompanionStopped () : Disposable =
    commands.registerCommand (
        CodeLensProvider.explainStoppedCommandId,
        System.Action<obj, obj>(fun _documentArg _indexArg -> showCompanionStoppedToast ())
    )
