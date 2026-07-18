// Hand-rolled VSCode API interop — only the slice this project touches: the status bar the
// walking skeleton needed, plus the CodeLens, command, and webview-panel slices the
// CodeLens → Run → rendered response flow adds.
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

type TextDocument =
    abstract fileName: string
    abstract getText: unit -> string

/// vscode.Range — constructed via the 4-number overload (startLine, startChar, endLine,
/// endChar); opaque otherwise, since the extension host only ever builds one to hand to a
/// `CodeLens` and never reads it back.
[<Import("Range", "vscode")>]
type Range(_startLine: float, _startCharacter: float, _endLine: float, _endCharacter: float) = class end

/// vscode.CodeLens — likewise opaque once built: the provider only ever constructs and returns
/// these, never inspects them again.
[<Import("CodeLens", "vscode")>]
type CodeLens(_range: Range, _command: obj) = class end

type CodeLensProvider =
    /// Fires to tell VSCode to re-invoke `provideCodeLenses` even though the document itself
    /// hasn't changed — the companion transitioning to/from `Ready` is exactly that case
    /// (ADR-0003: no CodeLenses while the companion is absent).
    abstract onDidChangeCodeLenses: obj
    abstract provideCodeLenses: document: TextDocument * token: obj -> JS.Promise<ResizeArray<CodeLens>>

/// vscode.EventEmitter<T> — used to back a provider's `onDidChangeCodeLenses`.
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

[<Import("commands", "vscode")>]
let commands: ICommands = jsNative

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

[<Import("window", "vscode")>]
let window: IWindow = jsNative

type IUri =
    abstract joinPath: baseUri: obj * [<ParamArray>] pathSegments: string[] -> obj

[<Import("Uri", "vscode")>]
let uri: IUri = jsNative

/// vscode.StatusBarAlignment.Left
let statusBarAlignmentLeft = 1.0

/// vscode.ViewColumn.Beside
let viewColumnBeside = -2.0
