// Hand-rolled VSCode API interop. It covers only the parts that this project uses: the status
// bar that the walking skeleton needed, plus the CodeLens, command, and webview-panel parts
// that the CodeLens → Run → rendered response flow adds.
module Vscode

open System
open Fable.Core

type StatusBarItem =
    abstract text: string with get, set
    abstract show: unit -> unit
    abstract dispose: unit -> unit

type ExtensionContext =
    abstract extensionPath: string
    abstract extensionUri: obj
    abstract subscriptions: ResizeArray<obj>

type Disposable =
    abstract dispose: unit -> unit

// --- documents / CodeLens ------------------------------------------------------------------

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
    abstract getText: unit -> string

/// vscode.Range. The 4-number overload constructs it (startLine, startChar, endLine, endChar).
/// It is opaque otherwise, because the extension host only builds one to give to a `CodeLens`,
/// and never reads it back.
[<Import("Range", "vscode")>]
type Range(_startLine: float, _startCharacter: float, _endLine: float, _endCharacter: float) = class end

/// vscode.CodeLens. It is opaque once built, because the provider only constructs and returns
/// these, and never inspects them again.
[<Import("CodeLens", "vscode")>]
type CodeLens(_range: Range, _command: obj) = class end

type CodeLensProvider =
    /// Fires to tell VSCode to invoke `provideCodeLenses` again, even when the document itself
    /// did not change. A companion transition into or out of `Ready` is exactly that case
    /// (ADR-0003: no CodeLenses while the companion is absent).
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

// --- workspace configuration -----------------------------------------------------------------

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

// --- webview panel ---------------------------------------------------------------------------

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

type IWindow =
    abstract createStatusBarItem: alignment: float * priority: float -> StatusBarItem
    abstract createWebviewPanel: viewType: string * title: string * showOptions: float * options: obj -> WebviewPanel
    /// vscode.window.showWarningMessage(message, item). It shows one button that the user can
    /// click. The promise resolves to the clicked item's label, or to `undefined` when the user
    /// dismisses the message.
    abstract showWarningMessage: message: string * item: string -> JS.Promise<obj>
    /// vscode.window.showWarningMessage(message). No button: a refusal toast (docs/spec/0003,
    /// Decision 8) states the reason and needs no reply.
    abstract showWarningMessage: message: string -> JS.Promise<obj>

[<Import("window", "vscode")>]
let window: IWindow = jsNative

type IUri =
    abstract joinPath: baseUri: obj * [<ParamArray>] pathSegments: string[] -> obj
    abstract parse: value: string -> obj

[<Import("Uri", "vscode")>]
let uri: IUri = jsNative

/// vscode.StatusBarAlignment.Left
let statusBarAlignmentLeft = 1.0

/// vscode.ViewColumn.Beside
let viewColumnBeside = -2.0
