// Hand-rolled VSCode API interop — only the slice the walking skeleton touches
// (status bar, extension context). CodeLens/webview bindings arrive with the
// tickets that need them (per the #4 bindings research).
module Vscode

open Fable.Core

type StatusBarItem =
    abstract text: string with get, set
    abstract show: unit -> unit
    abstract dispose: unit -> unit

type ExtensionContext =
    abstract extensionPath: string
    abstract subscriptions: ResizeArray<obj>

type IWindow =
    abstract createStatusBarItem: alignment: float * priority: float -> StatusBarItem

[<Import("window", "vscode")>]
let window: IWindow = jsNative

/// vscode.StatusBarAlignment.Left
let statusBarAlignmentLeft = 1.0
