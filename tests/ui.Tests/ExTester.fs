// ExTester page-object bindings for the UI harness and checks. Checks import this module
// instead of calling vscode-extension-tester directly.
//
// Only the page objects that setup and the setup self-check drive live here. A later check adds
// the binding it needs against the signature it actually calls, rather than inheriting an
// unexercised guess.
module ExTester

open Fable.Core
open Fable.Core.JsInterop

type WebElement =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>
    abstract isDisplayed: unit -> JS.Promise<bool>

type VSBrowser =
    abstract waitForWorkbench: timeoutMs: float -> JS.Promise<unit>
    abstract takeScreenshot: name: string -> JS.Promise<unit>
    abstract driver: obj

type StatusBar =
    abstract getItem: title: string -> JS.Promise<WebElement>
    abstract getItems: unit -> JS.Promise<WebElement[]>

type EditorView =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>

type ViewSection =
    abstract getTitle: unit -> JS.Promise<string>

type ViewContent =
    abstract getSections: unit -> JS.Promise<ViewSection[]>

type SideBarView =
    abstract getContent: unit -> ViewContent

type ViewControl =
    abstract openView: unit -> JS.Promise<SideBarView>

type ActivityBar =
    abstract getViewControl: title: string -> JS.Promise<ViewControl>

let private createInst (ctor: obj) : 'T = emitJsExpr ctor "new $0()"

module VSBrowser =
    [<Import("VSBrowser", "vscode-extension-tester")>]
    let private imported: obj = jsNative

    let instance: VSBrowser = imported?instance

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

let waitForWorkbench (browser: VSBrowser) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (browser, timeoutMs) "$0.waitForWorkbench($1)"
