// ExTester page-object bindings for the UI harness and checks. Checks import this module
// instead of calling vscode-extension-tester directly.
//
// Bindings land when a check needs them: setup owns the workbench tells, and the core-path
// check owns CodeLens clicks, editor-group detection, and response-viewer DOM reads. Each
// binding reads a channel a person reads — the workbench UI or the webview DOM — and adds no
// seam to the shipping extension.
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
    abstract switchToFrame: unit -> JS.Promise<unit>
    abstract switchBack: unit -> JS.Promise<unit>
    abstract findWebElement: locator: obj -> JS.Promise<WebElement>
    abstract findWebElements: locator: obj -> JS.Promise<WebElement[]>

type VSBrowser =
    abstract waitForWorkbench: timeoutMs: float -> JS.Promise<unit>
    abstract takeScreenshot: name: string -> JS.Promise<unit>
    abstract driver: obj

type StatusBar =
    abstract getItem: title: string -> JS.Promise<WebElement>
    abstract getItems: unit -> JS.Promise<WebElement[]>

type EditorView =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>
    abstract getEditorGroups: unit -> JS.Promise<obj[]>

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

type ByStatic =
    abstract css: selector: string -> obj

/// One read of the response viewer's rendered DOM. Empty strings mean the node was absent; a
/// check waits with `Harness.eventually` for the tell it cares about, rather than asserting the
/// DOM tree shape.
type ResponseViewerDom =
    {
        StatusLineText: string
        StatusCodeText: string
        /// The status-band class on the status-code span (`status-2xx`, `status-4xx`, …).
        StatusBandClass: string
        UrlText: string
        /// Text of the rendered JSON body region (`.response-json`).
        JsonBodyText: string
        /// Text of the in-flight indicator label (`.pending-label`), such as `Running…`.
        InFlightLabel: string
    }

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

module TextEditor =
    [<Import("TextEditor", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : TextEditor = createInst Ctor

    /// Partial title match, same contract as ExTester's `TextEditor.getCodeLens(string)`.
    let getCodeLensByTitle (editor: TextEditor) (title: string) : JS.Promise<CodeLens> =
        emitJsExpr (editor, title) "$0.getCodeLens($1)"

    let getCodeLensByIndex (editor: TextEditor) (index: int) : JS.Promise<CodeLens> =
        emitJsExpr (editor, index) "$0.getCodeLens($1)"

module WebView =
    [<Import("WebView", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : WebView = createInst Ctor

[<Import("By", "selenium-webdriver")>]
let By: ByStatic = jsNative

let waitForWorkbench (browser: VSBrowser) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (browser, timeoutMs) "$0.waitForWorkbench($1)"

let private switchToFrameTimed (view: WebView) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (view, timeoutMs) "$0.switchToFrame($1)"

let private tryElementText (view: WebView) (selector: string) : Async<string> =
    async {
        try
            let! el = view.findWebElement (By.css selector) |> Async.AwaitPromise
            return! el.getText () |> Async.AwaitPromise
        with _ ->
            return ""
    }

let private tryElementAttribute (view: WebView) (selector: string) (name: string) : Async<string> =
    async {
        try
            let! el = view.findWebElement (By.css selector) |> Async.AwaitPromise
            return! el.getAttribute name |> Async.AwaitPromise
        with _ ->
            return ""
    }

/// Pulls the status-band class (`status-2xx`, …) out of a `class` attribute that also carries
/// `status-code`. An empty string means no band class was present.
let private statusBandFromClass (classAttr: string) : string =
    if isNull (box classAttr) || classAttr = "" then
        ""
    else
        classAttr.Split(' ')
        |> Array.filter (fun c -> c <> "")
        |> Array.tryFind (fun c -> c.StartsWith("status-") && c <> "status-code")
        |> Option.defaultValue ""

/// Finds a CodeLens by partial title in the active editor and clicks it in the same attempt.
/// Pair with `Harness.eventually`: a stale handle between find and click fails the attempt, and
/// the next poll retries both steps together.
let tryClickCodeLensByTitle (title: string) : Async<bool> =
    async {
        try
            let editor = TextEditor.create ()
            let! lens = TextEditor.getCodeLensByTitle editor title |> Async.AwaitPromise

            if isNull (box lens) then
                return false
            else
                do! lens.click () |> Async.AwaitPromise
                return true
        with _ ->
            return false
    }

/// Same find-and-click contract as `tryClickCodeLensByTitle`, keyed by zero-based index from the
/// top of the editor. Use this when two lenses share a title (two `Run request` lenses).
let tryClickCodeLensByIndex (index: int) : Async<bool> =
    async {
        try
            let editor = TextEditor.create ()
            let! lens = TextEditor.getCodeLensByIndex editor index |> Async.AwaitPromise

            if isNull (box lens) then
                return false
            else
                do! lens.click () |> Async.AwaitPromise
                return true
        with _ ->
            return false
    }

/// True when a second editor group is open beside the first — the tell that the response viewer
/// opened in a column beside the editor, not in the same column or not at all.
let tryHasSecondEditorGroup () : Async<bool> =
    async {
        try
            let view = EditorView.create ()
            let! groups = view.getEditorGroups () |> Async.AwaitPromise
            return groups.Length >= 2
        with _ ->
            return false
    }

/// Enters the response viewer's webview iframe, reads the DOM surfaces the core-path check
/// asserts on, and switches back to the workbench. Returns `None` when the frame cannot be
/// entered; otherwise returns whatever text and class content is present (empty when a node is
/// missing). Does not assert DOM tree shape, element count, or layout.
let tryReadResponseViewer (frameTimeoutMs: float) : Async<ResponseViewerDom option> =
    async {
        let view = WebView.create ()
        let mutable switched = false

        try
            do! switchToFrameTimed view frameTimeoutMs |> Async.AwaitPromise
            switched <- true

            let! statusLine = tryElementText view ".status-line"
            let! statusCode = tryElementText view ".status-code"
            let! statusClass = tryElementAttribute view ".status-code" "class"
            let! url = tryElementText view ".status-url"
            let! jsonBody = tryElementText view ".response-json"
            let! inFlight = tryElementText view ".pending-label"

            do! view.switchBack () |> Async.AwaitPromise
            switched <- false

            return
                Some
                    { StatusLineText = statusLine
                      StatusCodeText = statusCode
                      StatusBandClass = statusBandFromClass statusClass
                      UrlText = url
                      JsonBodyText = jsonBody
                      InFlightLabel = inFlight }
        with _ ->
            if switched then
                try
                    do! view.switchBack () |> Async.AwaitPromise
                with _ ->
                    ()

            return None
    }
