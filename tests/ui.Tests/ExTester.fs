// ExTester page-object bindings for the UI harness and checks. Checks import this module
// instead of calling vscode-extension-tester directly.
//
// Bindings land when a check needs them: setup owns the workbench tells, the core-path check
// owns CodeLens titles and clicks plus the viewer-beside-the-editor tell, and product checks
// that read the response viewer share the DOM read. Every editor-facing binding is scoped to
// an editor group, because the viewer takes focus when it opens and an unscoped page object
// would resolve the wrong column's tab. Each binding reads a channel a person reads — the
// workbench UI or the webview DOM — and adds no seam to the shipping extension.
module ExTester

open Fable.Core
open Fable.Core.JsInterop

type WebElement =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>
    abstract isDisplayed: unit -> JS.Promise<bool>
    abstract getAttribute: name: string -> JS.Promise<string>

type CodeLens =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>

type TextEditor =
    abstract getCodeLenses: unit -> JS.Promise<CodeLens[]>

type WebView =
    abstract switchBack: unit -> JS.Promise<unit>
    abstract findWebElement: locator: obj -> JS.Promise<WebElement>

type VSBrowser =
    abstract openResources: paths: string[] -> JS.Promise<unit>
    abstract waitForWorkbench: timeoutMs: float -> JS.Promise<unit>
    abstract takeScreenshot: name: string -> JS.Promise<unit>
    abstract driver: obj

type StatusBar =
    abstract getItem: title: string -> JS.Promise<WebElement>
    abstract getItems: unit -> JS.Promise<WebElement[]>

/// One editor tab inside a group.
type EditorTab =
    abstract getTitle: unit -> JS.Promise<string>
    abstract isSelected: unit -> JS.Promise<bool>
    abstract select: unit -> JS.Promise<unit>

/// One editor column. A group is what scopes a page object to a column: an editor or a webview
/// built from a group reads that column's active tab, rather than whichever column happens to
/// hold focus.
type EditorGroup =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>
    abstract getOpenTabs: unit -> JS.Promise<EditorTab[]>

type EditorView =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>
    abstract getEditorGroups: unit -> JS.Promise<EditorGroup[]>
    abstract getEditorGroup: index: int -> JS.Promise<EditorGroup>

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

type Workbench =
    abstract executeCommand: command: string -> JS.Promise<unit>

type ByStatic =
    abstract css: selector: string -> obj

/// One read of the response viewer's rendered DOM. Empty strings mean the node was absent; a
/// check waits with `Harness.eventually` for the tell it cares about, rather than asserting the
/// DOM tree shape.
type ResponseViewerDom =
    {
        StatusLineText: string
        StatusCodeText: string
        /// The renderer's status class on the status-code span (`status-2xx`, `status-4xx`, …), as
        /// `Renderer.statusClass` produces it.
        StatusClass: string
        UrlText: string
        /// Text of the rendered JSON body region.
        JsonBodyText: string
        /// Text of the rendered plain-text body region (`.response-text`).
        PlainBodyText: string
        /// Text of the collapsible headers section. Empty when no response headers rendered.
        HeadersText: string
        /// Text of the webview root. Carries the full response render, or the plain error text
        /// a runtime-error update writes with no response structure.
        RootText: string
        /// Text of the Run in progress label, such as `Running…`.
        RunInProgressLabel: string
    }

/// Cross-boundary contract with the shipping renderer. Each selector must match the class name
/// `Renderer` writes and `ResponseViewer`'s stylesheet colors; the viewer tab title must match the
/// panel title in `ResponseViewer.showBeside`. A rename on either side breaks a read silently, so
/// they are named once here rather than spelled at each call site.
module private Viewer =
    let statusLineSelector = ".status-line"
    let statusCodeSelector = ".status-code"
    let urlSelector = ".status-url"
    let jsonBodySelector = ".response-json"
    let plainBodySelector = ".response-text"
    let headersSelector = ".headers"
    let rootSelector = "#root"
    let runInProgressSelector = ".pending-label"

    /// The class on the status-code span that is not the span's own `status-code`.
    let statusCodeClass = "status-code"
    let statusClassPrefix = "status-"

    let tabTitle = "FsHttp.Studio: Response"

/// The column the fixture is open in. Setup opens the fixture before anything splits the editor,
/// so it is the leftmost group.
let private fixtureGroupIndex = 0

/// The column the response viewer opens in. `ResponseViewer.showBeside` asks for the column beside
/// the active one, and the fixture holds the leftmost.
let private viewerGroupIndex = 1

/// Wait for the viewer's iframe to become the active frame. Deliberately not a check-tunable
/// deadline: it bounds one Selenium frame switch inside this binding, not a product surface a
/// check waits on. Product waits use the harness's named deadlines through `Harness.eventually`.
let private frameSwitchTimeoutMs = 5_000.

let private createInst (ctor: obj) : 'T = emitJsExpr ctor "new $0()"

/// Builds a page object scoped to one editor group. ExTester's editor constructors default to the
/// whole `EditorView`, which resolves the *focused* column's active tab — and the viewer takes
/// focus when it opens, so an unscoped editor would resolve the webview's tab.
let private createInstInGroup (ctor: obj) (group: EditorGroup) : 'T = emitJsExpr (ctor, group) "new $0($1)"

module VSBrowser =
    [<Import("VSBrowser", "vscode-extension-tester")>]
    let private imported: obj = jsNative

    let instance: VSBrowser = imported?instance

module EditorView =
    [<Import("EditorView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : EditorView = createInst Ctor

    /// Select a tab by partial title in one editor group.
    let openEditor (view: EditorView) (title: string) (groupIndex: int) : JS.Promise<obj> =
        emitJsExpr (view, title, groupIndex) "$0.openEditor($1, $2)"

    /// Close every tab in one editor group.
    let closeAllEditors (view: EditorView) (groupIndex: int) : JS.Promise<unit> =
        emitJsExpr (view, groupIndex) "$0.closeAllEditors($1)"

module StatusBar =
    [<Import("StatusBar", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : StatusBar = createInst Ctor

module ActivityBar =
    [<Import("ActivityBar", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : ActivityBar = createInst Ctor

module Workbench =
    [<Import("Workbench", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : Workbench = createInst Ctor

module TextEditor =
    [<Import("TextEditor", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let createInGroup (group: EditorGroup) : TextEditor = createInstInGroup Ctor group

    /// Partial title match, same contract as ExTester's `TextEditor.getCodeLens(string)`.
    let getCodeLensByTitle (editor: TextEditor) (title: string) : JS.Promise<CodeLens> =
        emitJsExpr (editor, title) "$0.getCodeLens($1)"

    /// Zero-based from the top of the editor. The fixture renders two lenses that share the title
    /// `Run request`, so a title match alone cannot reach the second block's lens.
    let getCodeLensByIndex (editor: TextEditor) (index: int) : JS.Promise<CodeLens> =
        emitJsExpr (editor, index) "$0.getCodeLens($1)"

module WebView =
    [<Import("WebView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let createInGroup (group: EditorGroup) : WebView = createInstInGroup Ctor group

[<Import("By", "selenium-webdriver")>]
let By: ByStatic = jsNative

let waitForWorkbench (browser: VSBrowser) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (browser, timeoutMs) "$0.waitForWorkbench($1)"

/// Opens a file in the workbench. Pair with a later `Harness.eventually` on the tab or the
/// lenses — `openResources` returns when ExTester has asked VSCode to open the path, not when
/// the editor has finished rendering it.
let openResource (browser: VSBrowser) (path: string) : JS.Promise<unit> =
    emitJsExpr (browser, path) "$0.openResources($1)"

let private switchToFrameTimed (view: WebView) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (view, timeoutMs) "$0.switchToFrame($1)"

let private editorGroup (index: int) : Async<EditorGroup> =
    let view = EditorView.create ()
    view.getEditorGroup index |> Async.AwaitPromise

/// Reads one property off the first element matching `selector`, or `""` when the element is
/// absent. Absence is a normal poll result — the viewer renders progressively — so it reads as an
/// empty string rather than an exception a check would have to catch.
let private tryElementProperty
    (view: WebView)
    (selector: string)
    (read: WebElement -> JS.Promise<string>)
    : Async<string> =
    async {
        try
            let! el = view.findWebElement (By.css selector) |> Async.AwaitPromise
            return! read el |> Async.AwaitPromise
        with _ ->
            return ""
    }

let private tryElementText (view: WebView) (selector: string) : Async<string> =
    tryElementProperty view selector (fun el -> el.getText ())

let private tryElementAttribute (view: WebView) (selector: string) (name: string) : Async<string> =
    tryElementProperty view selector (fun el -> el.getAttribute name)

/// Pulls the renderer's status class (`status-2xx`, …) out of a `class` attribute that also
/// carries `status-code`. An empty string means no status class was present.
let private statusClassFromAttribute (classAttr: string) : string =
    if isNull (box classAttr) || classAttr = "" then
        ""
    else
        classAttr.Split(' ')
        |> Array.filter (fun c -> c <> "")
        |> Array.tryFind (fun c -> c.StartsWith Viewer.statusClassPrefix && c <> Viewer.statusCodeClass)
        |> Option.defaultValue ""

/// The workspace folder name setup opens. Explorer sections use this title.
let private fixtureFolderName = "fixtures"

let private openSectionItem (section: ViewSection) (itemTitle: string) : JS.Promise<unit> =
    emitJsExpr (section, itemTitle) "$0.openItem($1)"

/// Opens a workspace file as the sole tab in the fixture column. Pair with `Harness.eventually`.
///
/// Uses the Explorer, not `openResources`. After the core-path check, focus sits on the
/// response viewer, and `openResources` opens into the focused group — which displaces the
/// viewer panel. Closing every tab in the fixture column first also avoids a second defect:
/// with several tabs open, `TextEditor` resolves the first `.editor-instance` in the group,
/// which can be a hidden tab with no CodeLens widgets even when the focused tab has them.
let tryOpenAndFocusResource (_path: string) (tabTitle: string) : Async<bool> =
    async {
        try
            let workbench = Workbench.create ()
            do! workbench.executeCommand "workbench.action.focusFirstEditorGroup" |> Async.AwaitPromise

            let view = EditorView.create ()
            do! EditorView.closeAllEditors view fixtureGroupIndex |> Async.AwaitPromise

            let bar = ActivityBar.create ()
            let! control = bar.getViewControl "Explorer" |> Async.AwaitPromise

            if isNull (box control) then
                return false
            else
                let! sideBar = control.openView () |> Async.AwaitPromise
                let! sections = sideBar.getContent().getSections () |> Async.AwaitPromise
                let mutable opened = false

                for section in sections do
                    if not opened then
                        let! title = section.getTitle () |> Async.AwaitPromise

                        if title.ToLowerInvariant().Contains fixtureFolderName then
                            do! openSectionItem section tabTitle |> Async.AwaitPromise
                            opened <- true

                if not opened then
                    return false
                else
                    do!
                        EditorView.openEditor view tabTitle fixtureGroupIndex
                        |> Async.AwaitPromise
                        |> Async.Ignore

                    let! groupAfter = editorGroup fixtureGroupIndex
                    let! titles = groupAfter.getOpenEditorTitles () |> Async.AwaitPromise

                    return
                        titles.Length = 1
                        && titles |> Array.exists (fun t -> t.Contains tabTitle)
        with _ ->
            return false
    }

/// Finds a CodeLens in the fixture's editor and clicks it in the same attempt. Pair with
/// `Harness.eventually`: a stale handle between find and click fails the attempt, and the next
/// poll retries both steps together.
let private tryClickCodeLens (find: TextEditor -> JS.Promise<CodeLens>) : Async<bool> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let editor = TextEditor.createInGroup group
            let! lens = find editor |> Async.AwaitPromise

            if isNull (box lens) then
                return false
            else
                do! lens.click () |> Async.AwaitPromise
                return true
        with _ ->
            return false
    }

/// Find-and-click by partial lens title.
let tryClickCodeLensByTitle (title: string) : Async<bool> =
    tryClickCodeLens (fun editor -> TextEditor.getCodeLensByTitle editor title)

/// Find-and-click by zero-based lens index from the top of the editor. Use this to reach the
/// second of two lenses that share a title.
let tryClickCodeLensByIndex (index: int) : Async<bool> =
    tryClickCodeLens (fun editor -> TextEditor.getCodeLensByIndex editor index)

/// The rendered titles of every CodeLens in the fixture's editor, top to bottom. Empty when the
/// editor has no lens yet, so a check can wait for the lenses to render and assert their words in
/// one `Harness.eventually`.
let tryReadCodeLensTitles () : Async<string[]> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let editor = TextEditor.createInGroup group
            let! lenses = editor.getCodeLenses () |> Async.AwaitPromise

            if isNull (box lenses) then
                return [||]
            else
                let titles = ResizeArray<string>()

                for lens in lenses do
                    let! title = lens.getText () |> Async.AwaitPromise
                    titles.Add title

                return titles.ToArray()
        with _ ->
            return [||]
    }

/// True when a second editor group is open beside the first *and* holds the response viewer's
/// tab — the tell that the viewer opened in a column beside the editor, rather than in the same
/// column, not at all, or with something else split beside the fixture.
let tryViewerBesideEditor () : Async<bool> =
    async {
        try
            let view = EditorView.create ()
            let! groups = view.getEditorGroups () |> Async.AwaitPromise

            if groups.Length <= viewerGroupIndex then
                return false
            else
                let! titles = groups[viewerGroupIndex].getOpenEditorTitles () |> Async.AwaitPromise
                return titles |> Array.exists (fun t -> t.Contains Viewer.tabTitle)
        with _ ->
            return false
    }

/// Enters the response viewer's webview iframe, reads the DOM surfaces the product checks
/// assert on, and switches back to the workbench. Returns `None` when the frame cannot be
/// entered; otherwise returns whatever text and class content is present (empty when a node is
/// missing). Does not assert DOM tree shape, element count, or layout.
let tryReadResponseViewer () : Async<ResponseViewerDom option> =
    async {
        try
            let! group = editorGroup viewerGroupIndex
            let view = WebView.createInGroup group
            let mutable switched = false

            try
                do! switchToFrameTimed view frameSwitchTimeoutMs |> Async.AwaitPromise
                switched <- true

                let! statusLine = tryElementText view Viewer.statusLineSelector
                let! statusCode = tryElementText view Viewer.statusCodeSelector
                let! statusClass = tryElementAttribute view Viewer.statusCodeSelector "class"
                let! url = tryElementText view Viewer.urlSelector
                let! jsonBody = tryElementText view Viewer.jsonBodySelector
                let! plainBody = tryElementText view Viewer.plainBodySelector
                let! headers = tryElementText view Viewer.headersSelector
                let! root = tryElementText view Viewer.rootSelector
                let! runInProgress = tryElementText view Viewer.runInProgressSelector

                // Cleared before the switch, not after: a `switchBack` that throws must not send
                // the handler below into a second one.
                switched <- false
                do! view.switchBack () |> Async.AwaitPromise

                return
                    Some
                        { StatusLineText = statusLine
                          StatusCodeText = statusCode
                          StatusClass = statusClassFromAttribute statusClass
                          UrlText = url
                          JsonBodyText = jsonBody
                          PlainBodyText = plainBody
                          HeadersText = headers
                          RootText = root
                          RunInProgressLabel = runInProgress }
            with _ ->
                if switched then
                    try
                        do! view.switchBack () |> Async.AwaitPromise
                    with _ ->
                        ()

                return None
        with _ ->
            return None
    }
