// ExTester page-object bindings for the UI harness and checks. Checks import this module
// instead of calling vscode-extension-tester directly.
module ExTester

open Fable.Core
open Fable.Core.JsInterop

type WebElement =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>
    abstract isDisplayed: unit -> JS.Promise<bool>

type CodeLens =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>

type EditorTab =
    abstract getTitle: unit -> JS.Promise<string>
    abstract select: unit -> JS.Promise<unit>

type TextEditor =
    abstract getTitle: unit -> JS.Promise<string>
    abstract getCodeLenses: unit -> JS.Promise<CodeLens[]>
    abstract getText: unit -> JS.Promise<string>
    abstract setText: text: string -> JS.Promise<unit>

type Editor =
    abstract getText: unit -> JS.Promise<string>

type WebView =
    abstract switchToFrame: timeoutMs: float -> JS.Promise<unit>
    abstract switchBack: unit -> JS.Promise<unit>
    abstract findWebElement: locator: obj -> JS.Promise<WebElement>

type VSBrowser =
    abstract waitForWorkbench: timeoutMs: float -> JS.Promise<unit>
    abstract takeScreenshot: name: string -> JS.Promise<unit>
    abstract driver: obj

type Workbench =
    abstract executeCommand: command: string -> JS.Promise<unit>
    abstract getNotifications: unit -> JS.Promise<WorkbenchNotification[]>

and WorkbenchNotification =
    abstract getMessage: unit -> JS.Promise<string>
    abstract dismiss: unit -> JS.Promise<unit>
    abstract takeAction: action: string -> JS.Promise<unit>

type StatusBar =
    abstract getItem: title: string -> JS.Promise<WebElement option>
    abstract getItems: unit -> JS.Promise<WebElement[]>
    abstract openNotificationsCenter: unit -> JS.Promise<obj>

type EditorView =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>
    abstract getTabByTitle: title: string -> JS.Promise<EditorTab>
    abstract getActiveTab: unit -> JS.Promise<EditorTab option>
    abstract openEditor: title: string -> JS.Promise<Editor>

type ActivityBar =
    abstract getViewControl: title: string -> JS.Promise<obj option>
    abstract getViewControls: unit -> JS.Promise<obj[]>

type SideBarView =
    abstract getContent: unit -> JS.Promise<obj>
    abstract getTitle: unit -> JS.Promise<string>

type BottomBarPanel =
    abstract openProblemsView: unit -> JS.Promise<obj>
    abstract openTerminalView: unit -> JS.Promise<obj>

type ByStatic =
    abstract css: selector: string -> obj
    abstract xpath: selector: string -> obj

let private createInst (ctor: obj) : 'T = emitJsExpr ctor "new $0()"

module VSBrowser =
    [<Import("VSBrowser", "vscode-extension-tester")>]
    let private imported: obj = jsNative

    let instance: VSBrowser = imported?instance

module WebView =
    [<Import("WebView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : WebView = createInst Ctor

module Workbench =
    [<Import("Workbench", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : Workbench = createInst Ctor

module TextEditor =
    [<Import("TextEditor", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : TextEditor = createInst Ctor

    let getCodeLensByTitle (editor: TextEditor) (title: string) : JS.Promise<CodeLens> =
        emitJsExpr (editor, title) "$0.getCodeLens($1)"

    let getCodeLensByIndex (editor: TextEditor) (index: int) : JS.Promise<CodeLens> =
        emitJsExpr (editor, index) "$0.getCodeLens($1)"

module EditorView =
    [<Import("EditorView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : EditorView = createInst Ctor

module StatusBar =
    [<Import("StatusBar", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : StatusBar = createInst Ctor

module ActivityBar =
    [<Import("ActivityBar", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : ActivityBar = createInst Ctor

module SideBarView =
    [<Import("SideBarView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : SideBarView = createInst Ctor

module BottomBarPanel =
    [<Import("BottomBarPanel", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : BottomBarPanel = createInst Ctor

[<Import("By", "selenium-webdriver")>]
let By: ByStatic = jsNative

let waitForWorkbench (browser: VSBrowser) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (browser, timeoutMs) "$0.waitForWorkbench($1)"

let switchToFrame (view: WebView) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (view, timeoutMs) "$0.switchToFrame($1)"

let openResource (browser: VSBrowser) (path: string) : JS.Promise<unit> =
    emitJsExpr (browser, path) "$0.openResources($1)"
