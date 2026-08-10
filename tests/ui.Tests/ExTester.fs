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
    /// True when the group's active tab carries unsaved changes.
    abstract isDirty: unit -> JS.Promise<bool>
    /// The full buffer text of the group's active editor.
    abstract getText: unit -> JS.Promise<string>

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

/// One editor column. A group is what scopes a page object to a column: an editor or a webview
/// built from a group reads that column's active tab, rather than whichever column happens to
/// hold focus.
type EditorGroup =
    abstract getOpenEditorTitles: unit -> JS.Promise<string[]>

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

/// One toast or center notification, as ExTester's `Notification` page object exposes it.
type Notification =
    abstract getMessage: unit -> JS.Promise<string>
    /// ExTester's `NotificationType` string: `"info"`, `"warning"`, `"error"`, or `"any"`.
    abstract getType: unit -> JS.Promise<string>
    abstract dismiss: unit -> JS.Promise<unit>

type Workbench =
    abstract executeCommand: command: string -> JS.Promise<unit>
    abstract getNotifications: unit -> JS.Promise<Notification[]>

type ProblemsView =
    abstract setFilter: pattern: string -> JS.Promise<unit>
    /// The visible rows, as ExTester's `Marker` page objects. Typed as opaque because the only
    /// assertion made on them is how many there are — a row's own text is never read.
    abstract getAllVisibleMarkers: markerType: string -> JS.Promise<obj[]>

type BottomBarPanel =
    abstract openProblemsView: unit -> JS.Promise<ProblemsView>
    abstract toggle: openPanel: bool -> JS.Promise<unit>

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
        /// Text of the Refused Run heading (`.refused-title`). Empty when no refusal rendered.
        RefusalTitleText: string
        /// Text of the Refused Run body paragraph (`.refused-detail`). Empty when no refusal
        /// rendered.
        RefusalDetailText: string
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
    let refusalTitleSelector = ".refused-title"
    let refusalDetailSelector = ".refused-detail"

    /// The class on the status-code span that is not the span's own `status-code`.
    let statusCodeClass = "status-code"
    let statusClassPrefix = "status-"

    let tabTitle = "FsHttp.Studio: Response"

/// The column the fixture is open in. Setup opens the fixture before anything splits the editor,
/// so it is the leftmost group.
let private fixtureGroupIndex = 0

/// The workbench command that focuses the fixture column. VSCode names these commands by ordinal,
/// so this must move with `fixtureGroupIndex`. A column this has no command for fails loudly here
/// rather than silently focusing the wrong one.
let private focusFixtureGroupCommand =
    match fixtureGroupIndex with
    | 0 -> "workbench.action.focusFirstEditorGroup"
    | other -> failwithf "no focus command wired for fixture column %d" other

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

module BottomBarPanel =
    [<Import("BottomBarPanel", "vscode-extension-tester")>]
    let private Ctor: obj = jsNative

    let create () : BottomBarPanel = createInst Ctor

/// ExTester's `MarkerType` wire values. Passed to `ProblemsView.getAllVisibleMarkers`.
module MarkerType =
    let any = "any"

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

    /// Replaces one 1-based line in the editor. ExTester rewrites the whole buffer through the
    /// clipboard to do this, so the call is a full replace rather than a surgical edit.
    let setTextAtLine (editor: TextEditor) (line: int) (text: string) : JS.Promise<unit> =
        emitJsExpr (editor, line, text) "$0.setTextAtLine($1, $2)"

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

/// True when the fixture column's tabs are exactly one tab, and that tab is `tabTitle`. The
/// claim both the sole-tab open's precondition and its verdict are written against.
let private holdsOnly (tabTitle: string) (titles: string[]) =
    titles.Length = 1 && titles |> Array.exists (fun t -> t.Contains tabTitle)

/// Empties the fixture column and reaches the file through the Explorer. The slow path of
/// `tryOpenAsSoleTabInFixtureColumn`, which documents why the Explorer rather than
/// `openResources`, and what the emptying costs.
///
/// After `openItem` opens the file, this path must not call `openEditor`. A second open of a
/// tab the column already holds can concatenate the buffer into itself. If the tab title is not
/// visible yet, return false and let `Harness.eventually` retry.
let private tryOpenThroughExplorer (view: EditorView) (tabTitle: string) : Async<bool> =
    async {
        let workbench = Workbench.create ()

        do! workbench.executeCommand focusFixtureGroupCommand |> Async.AwaitPromise

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
                let! groupAfter = editorGroup fixtureGroupIndex
                let! titles = groupAfter.getOpenEditorTitles () |> Async.AwaitPromise
                return holdsOnly tabTitle titles
    }

/// Opens a workspace file as the *sole* tab in the fixture column, closing whatever else that
/// column held. Pair with `Harness.eventually`.
///
/// Takes a tab title, not a path: it reaches the file through the Explorer rather than
/// `openResources`, because once the viewer is open focus sits on it and `openResources` opens
/// into the focused group — displacing the viewer panel. Emptying the column first avoids a
/// second defect: with several tabs open, `TextEditor` resolves the first `.editor-instance` in
/// the group, which can be a hidden tab carrying no CodeLens widgets even when the focused tab
/// has them.
///
/// Idempotent when the column already holds exactly that one tab: a second call returns without
/// reopening. That matters because this binding is polled: reopening a file the column already
/// holds can concatenate the buffer into itself — the same defect `openResources` has when
/// polled — and a doubled buffer renders doubled lenses.
///
/// Session-state cost, deliberate and named here because it is not reversed: this discards the
/// fixture tab setup proved live (`Harness` FixtureOpen) along with any earlier check's fixture.
/// The workspace folder — the state the extension's activation depends on — is untouched. A later
/// check must open its own fixture through this binding rather than assume a tab is still there.
let tryOpenAsSoleTabInFixtureColumn (tabTitle: string) : Async<bool> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let! titles = group.getOpenEditorTitles () |> Async.AwaitPromise

            if holdsOnly tabTitle titles then
                return true
            else
                let view = EditorView.create ()
                return! tryOpenThroughExplorer view tabTitle
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

/// Closes the response viewer column when it is open. Pair with `Harness.eventually`: returns
/// true once no viewer tab remains beside the editor, including the case where none was open.
let tryCloseResponseViewer () : Async<bool> =
    async {
        try
            let view = EditorView.create ()
            let! groups = view.getEditorGroups () |> Async.AwaitPromise

            if groups.Length > viewerGroupIndex then
                do! EditorView.closeAllEditors view viewerGroupIndex |> Async.AwaitPromise

            let! stillOpen = tryViewerBesideEditor ()
            return not stillOpen
        with _ ->
            return false
    }

/// Finds a standalone warning toast whose text is exactly `message`. `None` when no notification
/// list is available, or none matches.
let private tryFindWarningNotification (message: string) : Async<Notification option> =
    async {
        let workbench = Workbench.create ()
        let! notifications = workbench.getNotifications () |> Async.AwaitPromise

        if isNull (box notifications) then
            return None
        else
            let mutable found = None

            for notification in notifications do
                if found.IsNone then
                    let! text = notification.getMessage () |> Async.AwaitPromise
                    let! notificationType = notification.getType () |> Async.AwaitPromise

                    if notificationType = "warning" && text = message then
                        found <- Some notification

            return found
    }

/// True when a standalone warning toast shows exactly `message`. Reads the notification UI, not a
/// `showWarningMessage` call site.
let tryWarningNotification (message: string) : Async<bool> =
    async {
        try
            let! found = tryFindWarningNotification message
            return found.IsSome
        with _ ->
            return false
    }

/// Finds a warning toast whose text is exactly `message` and dismisses it in the same attempt.
/// Pair with `Harness.eventually`: a stale handle between find and dismiss fails the attempt.
let tryDismissWarningNotification (message: string) : Async<bool> =
    async {
        try
            let! found = tryFindWarningNotification message

            match found with
            | Some notification ->
                do! notification.dismiss () |> Async.AwaitPromise
                return true
            | None -> return false
        with _ ->
            return false
    }

/// Opens the Problems view, filters it to `fixtureFileName`, and returns true when no visible
/// markers remain for that filter. Pair with `Harness.eventually` after a positive tell that the
/// Run settled — absence alone is not meaningful.
///
/// Recorded softness, so a green run is not over-read: only FsHttp.Studio is installed in the
/// suite's VSCode. No F# language service is present, so nothing else contributes diagnostics to
/// a `.fsx` file, and an empty Problems view is weaker evidence here than in a user's editor. It
/// proves FsHttp.Studio contributes no diagnostic. It cannot prove FsHttp.Studio's output would
/// not *look* like a fault beside another extension's. The stronger half of that intent lives in
/// the viewer assertion, which reads a channel with no such weakness.
///
/// Opens the bottom panel as a side effect. A caller that wants the panel closed again must close
/// it — see `tryCloseBottomPanel`.
let tryNoProblemsForFixture (fixtureFileName: string) : Async<bool> =
    async {
        try
            let panel = BottomBarPanel.create ()
            let! problems = panel.openProblemsView () |> Async.AwaitPromise
            do! problems.setFilter fixtureFileName |> Async.AwaitPromise
            let! markers = problems.getAllVisibleMarkers MarkerType.any |> Async.AwaitPromise

            if isNull (box markers) then
                return true
            else
                return markers.Length = 0
        with _ ->
            return false
    }

/// Closes the bottom panel, so a check that opened the Problems view hands the next check the
/// session state it inherited. Returns false when the panel cannot be reached; a caller asserting
/// something else should not redden on that.
let tryCloseBottomPanel () : Async<bool> =
    async {
        try
            let panel = BottomBarPanel.create ()
            do! panel.toggle false |> Async.AwaitPromise
            return true
        with _ ->
            return false
    }

/// The fixture column's active TextEditor, or a thrown failure when the group cannot be reached.
let private fixtureEditor () : Async<TextEditor> =
    async {
        let! group = editorGroup fixtureGroupIndex
        return TextEditor.createInGroup group
    }

/// An editor tab's dirty flag and full buffer text — the pair every buffer claim below reads, and
/// the pair `trySetFixtureLine` reads twice.
let private bufferState (editor: TextEditor) : Async<bool * string> =
    async {
        let! dirty = editor.isDirty () |> Async.AwaitPromise
        let! text = editor.getText () |> Async.AwaitPromise
        return dirty, text
    }

/// True when the fixture editor's buffer state satisfies `holds`. An unreachable editor is a false,
/// not a throw, so `Harness.eventually` can retry it.
let private tryFixtureBuffer (holds: bool -> string -> bool) : Async<bool> =
    async {
        try
            let! editor = fixtureEditor ()
            let! dirty, text = bufferState editor
            return holds dirty text
        with _ ->
            return false
    }

/// Replaces one 1-based line in the fixture editor with `text`. Idempotent when the buffer already
/// holds `text` on that path: a poll that finds the broken text present and the tab dirty returns
/// without rewriting. Pair with `Harness.eventually` — the tell is dirty plus the broken fragment,
/// not the paste itself.
///
/// Does not save. The fixture on disk is never written.
let trySetFixtureLine (line: int) (text: string) : Async<bool> =
    async {
        try
            let! editor = fixtureEditor ()
            let! dirty, current = bufferState editor

            if dirty && current.Contains text then
                return true
            else
                do! TextEditor.setTextAtLine editor line text |> Async.AwaitPromise
                let! dirtyAfter, textAfter = bufferState editor
                return dirtyAfter && textAfter.Contains text
        with _ ->
            return false
    }

/// True when the fixture editor's tab is dirty and its buffer contains `fragment`.
let tryFixtureBufferHolds (fragment: string) : Async<bool> =
    tryFixtureBuffer (fun dirty text -> dirty && text.Contains fragment)

/// True when the fixture editor's tab is clean and its buffer does not contain `fragment`.
let tryFixtureBufferLacks (fragment: string) : Async<bool> =
    tryFixtureBuffer (fun dirty text -> (not dirty) && not (text.Contains fragment))

/// Focuses the fixture column and reverts its active file from disk through the workbench's own
/// revert-file command. Pair with `Harness.eventually` on `tryFixtureBufferLacks` — the command
/// returns when VSCode has been asked to revert, not when the tab is clean. Does not save.
let tryRevertFixtureFile () : Async<bool> =
    async {
        try
            let workbench = Workbench.create ()

            do! workbench.executeCommand focusFixtureGroupCommand |> Async.AwaitPromise
            do! workbench.executeCommand "workbench.action.files.revert" |> Async.AwaitPromise
            return true
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
                let! refusalTitle = tryElementText view Viewer.refusalTitleSelector
                let! refusalDetail = tryElementText view Viewer.refusalDetailSelector

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
                          RunInProgressLabel = runInProgress
                          RefusalTitleText = refusalTitle
                          RefusalDetailText = refusalDetail }
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
