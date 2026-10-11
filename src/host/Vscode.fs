module Vscode

open System
open Fable.Core

type StatusBarItem =
    abstract text: string with get, set
    abstract show: unit -> unit
    abstract hide: unit -> unit
    abstract dispose: unit -> unit

type ExtensionContext =
    abstract extensionPath: string
    abstract extensionUri: obj
    abstract subscriptions: ResizeArray<obj>

type Disposable =
    abstract dispose: unit -> unit

/// vscode.Uri, narrowed to the one part this project reads. `scheme` is `"file"` for a script
/// that lives on the local filesystem, and something else (`untitled`, `vscode-vfs`, `git`, a
/// remote provider) for one that does not.
type Uri =
    abstract scheme: string

type TextDocument =
    abstract fileName: string
    /// The script's own URI. Only a `file` scheme carries a real local path in `fileName`, which
    /// is what a Run needs for `__SOURCE_DIRECTORY__` (see `Protocol.scriptFileNameFor`).
    abstract uri: Uri
    /// vscode.TextDocument.languageId. `"fsharp"` for `.fs`, `.fsx`, and `.fsi` buffers.
    abstract languageId: string
    abstract getText: unit -> string

/// vscode.Position, narrowed to the 0-based line.
type Position =
    abstract line: int

/// vscode.Selection, narrowed to the end of the selection that has the caret.
type Selection =
    abstract active: Position

/// vscode.TextEditor. Narrowed to the document the editor shows, which is what a document-aware
/// status bar reads when the active editor changes, and to the primary selection.
type TextEditor =
    abstract document: TextDocument
    abstract selection: Selection

/// vscode.Range. The 4-number overload constructs it (startLine, startChar, endLine, endChar).
/// It is opaque otherwise, because the extension host only builds one to give to a `CodeLens`,
/// and never reads it back.
[<Import("Range", "vscode")>]
type Range(_startLine: float, _startCharacter: float, _endLine: float, _endCharacter: float) = class end

/// vscode.CodeLens. It is opaque once built, because the provider only constructs and returns
/// these, and inspects none of them again.
[<Import("CodeLens", "vscode")>]
type CodeLens(_range: Range, _command: obj) = class end

type CodeLensProvider =
    /// Fire this when the companion enters or leaves `Ready`, because no document change
    /// accompanies that transition.
    abstract onDidChangeCodeLenses: obj
    abstract provideCodeLenses: document: TextDocument * token: obj -> JS.Promise<ResizeArray<CodeLens>>

/// vscode.EventEmitter&lt;T&gt;. It backs a provider's `onDidChangeCodeLenses`.
[<Import("EventEmitter", "vscode")>]
type EventEmitter<'T>() =
    member _.event: obj = jsNative
    member _.fire(_data: 'T) : unit = jsNative

type ILanguages =
    abstract registerCodeLensProvider: selector: obj * provider: CodeLensProvider -> Disposable

[<Import("languages", "vscode")>]
let languages: ILanguages = jsNative

type ICommands =
    abstract registerCommand: command: string * callback: System.Action<obj, obj> -> Disposable
    abstract executeCommand: command: string * arg: obj -> JS.Promise<obj>

[<Import("commands", "vscode")>]
let commands: ICommands = jsNative

/// vscode.WorkspaceConfiguration. `get` reads the `fshttpStudio.dotnetPath` override as a
/// string. The setting declares a `""` default, so this reads back as a string, and the string
/// is empty when the user has not set the override. The caller treats a blank string as "not
/// configured". `getNumber` reads numeric settings such as `requestTimeoutMs`.
type WorkspaceConfiguration =
    abstract get: section: string -> string

    [<Emit("$0.get($1)")>]
    abstract getNumber: section: string -> float

type IWorkspace =
    abstract getConfiguration: section: string -> WorkspaceConfiguration

[<Import("workspace", "vscode")>]
let workspace: IWorkspace = jsNative

type Webview =
    abstract html: string with get, set
    abstract cspSource: string
    abstract postMessage: message: obj -> unit
    abstract onDidReceiveMessage: listener: (obj -> unit) -> Disposable
    abstract asWebviewUri: localResource: obj -> obj

type WebviewPanel =
    abstract webview: Webview
    abstract reveal: unit -> unit
    abstract onDidDispose: listener: (unit -> unit) -> Disposable
    abstract dispose: unit -> unit

/// vscode.CancellationToken, narrowed to the event that fires when the user cancels.
type CancellationToken =
    abstract onCancellationRequested: listener: (obj -> unit) -> Disposable

type IWindow =
    abstract createStatusBarItem: alignment: float * priority: float -> StatusBarItem
    abstract createWebviewPanel: viewType: string * title: string * showOptions: float * options: obj -> WebviewPanel
    /// The editor that has focus, or `None` when no editor is active.
    abstract activeTextEditor: TextEditor option
    /// Fires when the active editor changes. The listener receives `None` when focus leaves every
    /// editor. Register the returned `Disposable` on `ExtensionContext.subscriptions`.
    abstract onDidChangeActiveTextEditor: listener: (TextEditor option -> unit) -> Disposable
    /// vscode.window.showWarningMessage(message, item). It shows one button that the user can
    /// click. The promise resolves to the clicked item's label, or to `undefined` when the user
    /// dismisses the message.
    abstract showWarningMessage: message: string * item: string -> JS.Promise<obj>
    /// vscode.window.showWarningMessage(message). No button, for a toast that needs no reply.
    abstract showWarningMessage: message: string -> JS.Promise<obj>
    /// vscode.window.showInformationMessage(message).
    abstract showInformationMessage: message: string -> JS.Promise<obj>
    /// vscode.window.showQuickPick(items). Each item is an object with `label` and an optional
    /// `detail`. The promise resolves to the picked item, or to `undefined` on cancel.
    abstract showQuickPick: items: obj[] -> JS.Promise<obj | null>

    /// vscode.window.withProgress(options, task). The progress shows until the promise of the
    /// task settles.
    abstract withProgress:
        options: obj * task: System.Func<obj, CancellationToken, JS.Promise<unit>> -> JS.Promise<unit>

[<Import("window", "vscode")>]
let window: IWindow = jsNative

type IUri =
    abstract joinPath: baseUri: obj * [<ParamArray>] pathSegments: string[] -> obj
    abstract parse: value: string -> obj

[<Import("Uri", "vscode")>]
let uri: IUri = jsNative

/// vscode.StatusBarAlignment.Left
let statusBarAlignmentLeft = 1.0

/// vscode.ProgressLocation.Notification
let progressLocationNotification = 15.0

/// vscode.ViewColumn.Beside
let viewColumnBeside = -2.0
