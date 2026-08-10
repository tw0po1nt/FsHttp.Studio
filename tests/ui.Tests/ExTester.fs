// ExTester page-object bindings for the UI harness and checks. Checks import this module
// instead of calling vscode-extension-tester directly.
//
// Bindings land when a check needs them: setup owns the workbench tells, the core-path check
// owns CodeLens titles and clicks plus the viewer-beside-the-editor tell, product checks that
// read the response viewer share the DOM read, and the companion-death check owns the window
// reload. Every editor-facing binding is scoped to an editor group, because the viewer takes
// focus when it opens and an unscoped page object would resolve the wrong column's tab. Each
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

/// Asks VSCode to open a file. Returns when ExTester has asked, not when the editor has finished
/// rendering it, so pair it with a later wait on the tab or the buffer.
let private openResource (browser: VSBrowser) (path: string) : JS.Promise<unit> =
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

/// True when the fixture column's tabs are exactly one tab, and that tab is `tabTitle`. The
/// claim both the sole-tab open's precondition and its verdict are written against.
let private holdsOnly (tabTitle: string) (titles: string[]) =
    titles.Length = 1 && titles |> Array.exists (fun t -> t.Contains tabTitle)

/// What one attempt to open the fixture did. The open is the one step in this suite that must not
/// be repeated, so the outcome says whether it was reached rather than folding into a boolean a
/// caller would poll.
type FixtureOpen =
    /// VSCode was asked to open the file. The tab may not have rendered yet, and the column can
    /// still hold other tabs — `tryCloseOtherTabsInFixtureColumn` settles both.
    | FixtureOpenRequested
    /// The open raised, so the column's state is unknown. Not safe to retry: a second open of a
    /// file the column already holds concatenates the buffer into itself.
    | FixtureOpenRaised of reason: string

/// True when the fixture column holds `tabTitle` and nothing else.
///
/// Reads and never writes, which is what makes it the poll a check waits on after
/// `openFixtureAsSoleTab`. The open itself must happen exactly once, so it cannot be the thing
/// being polled.
let tryFixtureColumnHoldsOnly (tabTitle: string) : Async<bool> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let! titles = group.getOpenEditorTitles () |> Async.AwaitPromise
            return holdsOnly tabTitle titles
        with _ ->
            return false
    }

/// Opens a workspace file in the fixture column, beside whatever that column already holds.
/// `tryCloseOtherTabsInFixtureColumn` makes it the sole tab afterwards.
///
/// **Reach `FixtureOpenRequested` exactly once per fixture.** Opening a file the column already
/// holds concatenates the buffer into itself, and a doubled buffer renders doubled lenses — which
/// reads as a provider that over-detects rather than as a driver that opened twice.
/// `Checks.openFixtureAsSoleTab` composes the open and the waits, and is what a check should call.
///
/// Opens first and closes the other tabs second, rather than emptying the column first. A column
/// with no tabs left stops being a column: the response viewer then slides into the fixture
/// column's index, and the file opens beside the viewer instead of replacing it.
///
/// Opens the path rather than clicking the file in the Explorer. Reaching a fixture through the
/// Explorer was measured loading the file into the buffer twice on about half of all runs — a
/// 59-line buffer of a 30-line file, reported clean, with the file on disk unchanged. VSCode holds
/// that buffer as the file's own content, so the workbench revert command treats it as nothing to
/// revert. `openResources` opens into the focused column, which is why the focus command comes
/// first: without it the open lands on whichever column last held focus, which is the response
/// viewer for every check after the core path.
let openFixtureInColumn (path: string) : Async<FixtureOpen> =
    async {
        try
            let workbench = Workbench.create ()
            let browser = VSBrowser.instance

            do! workbench.executeCommand focusFixtureGroupCommand |> Async.AwaitPromise
            do! openResource browser path |> Async.AwaitPromise

            return FixtureOpenRequested
        with e ->
            return FixtureOpenRaised(sprintf "opening %s raised: %s" path e.Message)
    }

/// Closes every tab in the fixture column except its active one, and reports whether the column
/// now holds `tabTitle` and nothing else.
///
/// Safe to poll, which is the point: closing the other tabs is idempotent, and it cannot empty the
/// column, so the response viewer can never slide into the fixture column's index. A column
/// holding one tab keeps exactly one `.editor-instance` laid out, which is what a CodeLens read
/// needs — an inactive tab leaves a second editor in the page carrying no lens.
///
/// Waits for the fixture tab to appear before it closes anything. The Explorer's click makes the
/// file it opens the column's active tab, so closing the others while that tab is still on its way
/// would close the tab this is waiting for, and nothing reopens it.
let tryCloseOtherTabsInFixtureColumn (tabTitle: string) : Async<bool> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let! titles = group.getOpenEditorTitles () |> Async.AwaitPromise

            if holdsOnly tabTitle titles then
                return true
            elif titles |> Array.exists (fun t -> t.Contains tabTitle) |> not then
                return false
            else
                let workbench = Workbench.create ()

                do! workbench.executeCommand focusFixtureGroupCommand |> Async.AwaitPromise

                do!
                    workbench.executeCommand "workbench.action.closeOtherEditors"
                    |> Async.AwaitPromise

                return! tryFixtureColumnHoldsOnly tabTitle
        with _ ->
            return false
    }

/// Finds a CodeLens in the fixture's editor and clicks it in the same attempt. Pair with
/// `Harness.eventually`: a stale handle between find and click fails the attempt, and the next
/// poll retries both steps together.
/// Collects the fixture editor's CodeLens anchors into `anchors`, ordered top to bottom, and
/// returns `null` when the editor is not in the page yet. Every lens binding shares this prologue.
///
/// It reads the DOM rather than building an ExTester `TextEditor`. That constructor waits for the
/// editor to become visible, and the wait was observed to expire after about 5 s per poll while
/// the page held one editor at 852x691, `display=block`, `visibility=visible`, `opacity=1`, and
/// uncovered. The wait, and not the workbench, was wrong. A read that costs 5 s also exhausted a
/// 45 s deadline in about 9 polls, which hid how often it was failing.
///
/// Ordered by the anchor's own top edge, not by DOM order: the lens for the second block must be
/// index 1, and VSCode paints lenses in view zones whose DOM order carries no such promise.
///
/// Picks the first *laid-out* `.editor-instance` rather than the first one in the DOM. A column
/// holding more than one tab keeps the inactive editors in the page at zero size, and the DOM
/// order of the group carries no promise that the active tab comes first. Reading a hidden editor
/// finds no lens and reports it as a provider that painted nothing.
let private lensAnchorsPrologue =
    """
    var editor = null;
    var instances = document.querySelectorAll('.editor-instance');
    for (var e = 0; e < instances.length && !editor; e++) {
        var box = instances[e].getBoundingClientRect();
        if (box.width > 0 && box.height > 0) { editor = instances[e]; }
    }
    if (!editor) { return null; }
    var anchors = [];
    var candidates = editor.querySelectorAll('a[id]');
    for (var i = 0; i < candidates.length; i++) {
        if (candidates[i].closest('[class*="codelens" i]')) { anchors.push(candidates[i]); }
    }
    anchors.sort(function (l, r) {
        return l.getBoundingClientRect().top - r.getBoundingClientRect().top;
    });
    """

let private runLensScript (body: string) (arg: objnull) : Async<objnull> =
    let driver = VSBrowser.instance.driver
    let script = lensAnchorsPrologue + body

    let call: JS.Promise<objnull> =
        emitJsExpr (driver, script, arg) "$0.executeScript($1, $2)"

    call |> Async.AwaitPromise

/// Selects one lens anchor with `selectBody` and clicks it through the driver.
///
/// The script only finds the anchor and hands the element back. The click must come from the
/// driver, which presses and releases a real pointer over the element. VSCode runs a CodeLens
/// command from the editor's own `onMouseUp`, so a `click()` called inside the page invokes
/// nothing: that call dispatches one untrusted `click` event and no `mousedown` or `mouseup`. The
/// lens then reads as clicked while the command never runs, and every check that waits on what
/// the command does times out with a healthy editor on screen.
let private tryClickLensAnchor (selectBody: string) (arg: objnull) : Async<bool> =
    async {
        try
            let! selected = runLensScript selectBody arg

            if isNull selected then
                return false
            else
                let anchor = unbox<WebElement> selected
                do! anchor.click () |> Async.AwaitPromise
                return true
        with _ ->
            return false
    }

/// Find-and-click by partial lens title.
let tryClickCodeLensByTitle (title: string) : Async<bool> =
    tryClickLensAnchor
        """
        for (var i = 0; i < anchors.length; i++) {
            if (anchors[i].textContent.indexOf(arguments[0]) >= 0) { return anchors[i]; }
        }
        return null;
        """
        (box title)

/// Find-and-click by zero-based lens index from the top of the editor. Use this to reach the
/// second of two lenses that share a title.
let tryClickCodeLensByIndex (index: int) : Async<bool> =
    tryClickLensAnchor
        """
        if (arguments[0] >= anchors.length) { return null; }
        return anchors[arguments[0]];
        """
        (box index)

/// The rendered titles of every CodeLens in the fixture's editor, top to bottom. Empty when the
/// editor has no lens yet, so a check can wait for the lenses to render and assert their words in
/// one `Harness.eventually`.
/// What one CodeLens read saw. A read that throws is not an editor with no lenses: ExTester raises
/// when the editor group or the editor itself is not addressable yet, and reporting that as zero
/// lenses makes a query that never ran indistinguishable from a provider that painted nothing.
/// Those two failures need different repairs, so the read reports which one it was.
type LensRead =
    | LensTitles of titles: string[]
    | LensReadFailed of reason: string

/// Asks the page what the editor elements actually are, for a read that raised. ExTester's
/// `TextEditor` waits for `.editor-instance` to become visible and reports only that the wait
/// expired, which does not say whether the element is missing, collapsed, or covered. Each of
/// those needs a different repair, so the failure carries the answer rather than a guess.
let private describeEditorInstances () : Async<string> =
    async {
        try
            let driver = VSBrowser.instance.driver

            let script =
                """
                var nodes = document.querySelectorAll('.editor-instance');
                var parts = [];
                for (var i = 0; i < nodes.length; i++) {
                    var n = nodes[i];
                    var r = n.getBoundingClientRect();
                    var s = window.getComputedStyle(n);
                    var mid = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
                    parts.push(
                        '#' + i +
                        ' ' + Math.round(r.width) + 'x' + Math.round(r.height) +
                        ' display=' + s.display +
                        ' visibility=' + s.visibility +
                        ' opacity=' + s.opacity +
                        ' covered=' + (mid && !n.contains(mid) && mid !== n));
                }
                return nodes.length + ' .editor-instance [' + parts.join('; ') + ']';
                """

            let described: JS.Promise<obj> = emitJsExpr (driver, script) "$0.executeScript($1)"
            let! result = described |> Async.AwaitPromise
            return unbox<string> result
        with e ->
            return sprintf "the editor-instance probe itself raised: %s" e.Message
    }

let tryReadCodeLensTitles () : Async<LensRead> =
    async {
        try
            let! read =
                runLensScript
                    """
                    var out = [];
                    for (var i = 0; i < anchors.length; i++) { out.push(anchors[i].textContent.trim()); }
                    return out;
                    """
                    (null: objnull)

            if isNull read then
                let! editors = describeEditorInstances ()
                return LensReadFailed(sprintf "no laid-out editor in the page — %s" editors)
            else
                return LensTitles(unbox<string[]> read)
        with e ->
            let! editors = describeEditorInstances ()
            return LensReadFailed(sprintf "%s — page shows %s" e.Message editors)
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

/// The fixture buffer's size and dirty flag, for a failure that has to tell a buffer the product
/// mis-read from a buffer that is not the fixture. A lens count that reads double is either, and
/// the two need different repairs: a doubled buffer is the driver having opened the file twice,
/// and a correct buffer is the provider having painted twice.
let describeFixtureBuffer () : Async<string> =
    async {
        try
            let! editor = fixtureEditor ()
            let! dirty, text = bufferState editor
            let lines = text.Split '\n' |> Array.length

            return sprintf "a %s buffer of %i lines" (if dirty then "dirty" else "clean") lines
        with e ->
            return sprintf "a buffer that could not be read: %s" e.Message
    }

/// The fixture editor's whole buffer, or `None` when it cannot be read.
///
/// The fixture column has been observed holding a fixture twice over — a 59-line buffer of a
/// 30-line file, reported clean, with the file on disk unchanged. A doubled buffer carries a
/// second copy of every block, so the provider paints a lens above each one and an exact lens
/// count reads double. Two independent reads agree on the doubling: the buffer through the
/// clipboard, and a DOM lens count scoped to one `.editor-instance`.
///
/// A caller must not compare this text to the file byte for byte. The buffer arrives through the
/// clipboard, which is free to normalize line endings and trailing whitespace. Compare line
/// counts, or compare a half of the buffer against another half.
let tryFixtureBufferText () : Async<string option> =
    async {
        try
            let! editor = fixtureEditor ()
            let! _, text = bufferState editor
            return Some text
        with _ ->
            return None
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

/// Asks VSCode to reload the window. Returns when ExTester has asked, not when the reload has
/// finished — the companion-death check waits on a fresh companion process for that. Pre-reload
/// element handles go stale; every later lookup must be fresh.
let reloadWindow () : Async<unit> =
    async {
        let workbench = Workbench.create ()
        do! workbench.executeCommand "Developer: Reload Window" |> Async.AwaitPromise
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
