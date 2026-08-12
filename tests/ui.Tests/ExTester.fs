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

/// One copy button as the viewer paints it.
///
/// `Displayed` is the laid-out box from `getBoundingClientRect`. It claims the button is present
/// and has a size — nothing more. It is *not* a witness for Decision 2's defect: the spec
/// measured a button inside a closed `<details>` still reporting a 29x23 box with
/// `visibility: visible`, "so no measurement in code detects the defect. Only a screenshot shows
/// it". The screenshot on the PR is that evidence; this field is the weaker claim beside it.
type CopyButtonReading =
    { Key: string
      Label: string
      Displayed: bool }

/// The copy buttons, the section-shell spacing, and both collapsible sections' open state, read
/// together in one trip into the viewer frame. Kept out of `ResponseViewerDom` on purpose: it
/// costs an `executeScript` round-trip, and only the copy checks read it, while every check in
/// the suite reads the viewer DOM.
type CopySurfaceReading =
    {
        Buttons: CopyButtonReading[]
        /// Computed `margin-bottom` of each `.section-shell`, in CSS pixels, in DOM order.
        ShellMarginsPx: float[]
        RequestOpen: bool
        HeadersOpen: bool
    }

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
        /// Text of the rendered JSON *response* body region. Scoped to `.response-body`: a
        /// captured request body renders through the same classes inside the Request section.
        JsonBodyText: string
        /// Text of the rendered plain-text *response* body region (`.response-text`), scoped to
        /// `.response-body` for the same reason.
        PlainBodyText: string
        /// Text of the collapsible headers section. Empty when no response headers rendered.
        HeadersText: string
        /// Rendered text of the collapsible Request section. A `<details>` reports only its
        /// summary while collapsed, so this carries the headers and body only once the section
        /// has been expanded — which is exactly the user's own path to them.
        RequestText: string
        /// Text of the Request section's summary: `Request`, or `Request (2.1 KB)` with a
        /// captured body. Empty when no Request section rendered.
        RequestSummaryText: string
        /// Whether the Request section is expanded. `<details>` carries `open` only while it is,
        /// so this is the direct read of collapsed-by-default.
        RequestOpen: bool
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

/// The label a copy button rests at, as `Renderer.copyButtonLabel` writes it and `Dom.flash`
/// restores it. Restated here because this suite does not compile the renderer; it is a
/// cross-boundary contract on the same terms as the selectors below, and the check that watches
/// the label return to it after a flash is what keeps the two spellings honest.
let copyButtonRestingLabel = "Copy"

/// Cross-boundary contract with the shipping renderer. Each selector must match the class name
/// `Renderer` writes and `ResponseViewer`'s stylesheet colors; the viewer tab title must match the
/// panel title in `ResponseViewer.showBeside`. A rename on either side breaks a read silently, so
/// they are named once here rather than spelled at each call site.
module private Viewer =
    let statusLineSelector = ".status-line"
    let statusCodeSelector = ".status-code"
    let urlSelector = ".status-url"
    // Both body reads are scoped to `.response-body`. The Request section renders a captured
    // request body through the same `renderContent` dispatch, and therefore the same
    // `.response-json` and `.response-text` classes — and it sits *above* the response body in
    // the DOM, so an unscoped read reaches the request's body first. It reads as empty while the
    // section is collapsed, which is a read of the wrong region that looks like a viewer that has
    // not painted yet.
    let jsonBodySelector = ".response-body .response-json"
    let plainBodySelector = ".response-body .response-text"
    let headersSelector = ".headers"
    let requestSelector = ".request"

    /// The Request section's own summary. `.headers-summary` is shared with the response headers
    /// section, so it must be scoped, or a read reaches whichever of the two comes first.
    let requestSummarySelector = ".request > .headers-summary"

    let copyButtonSelector = ".copy-button"

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

/// What the workbench shows for the FsHttp.Studio status-bar item. Hidden means Decision 6
/// removed it from view (or no item carries the prefix). The product always writes the
/// `FsHttp.Studio: ` prefix into the item text.
type FsHttpStatus =
    | StatusHidden
    | StatusText of text: string
    | StatusUnreadable of reason: string

let private fsHttpStatusPrefix = "FsHttp.Studio:"

/// Reads the FsHttp.Studio status-bar item through ExTester's `StatusBar`. A hidden item does
/// not appear among `getItems`, which is how Decision 6's hide is observed.
let tryReadFsHttpStatus () : Async<FsHttpStatus> =
    async {
        try
            let bar = StatusBar.create ()
            let! items = bar.getItems () |> Async.AwaitPromise
            let mutable found: string option = None

            for item in items do
                if found.IsNone then
                    let! text = item.getText () |> Async.AwaitPromise

                    if text.StartsWith fsHttpStatusPrefix then
                        let! shown = item.isDisplayed () |> Async.AwaitPromise

                        if shown then
                            found <- Some text

            match found with
            | None -> return StatusHidden
            | Some text -> return StatusText text
        with e ->
            return StatusUnreadable e.Message
    }

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

/// True when the fixture column lists `tabTitle` among its open tabs, whether or not it is alone.
let tryFixtureColumnShowsTab (tabTitle: string) : Async<bool> =
    async {
        try
            let! group = editorGroup fixtureGroupIndex
            let! titles = group.getOpenEditorTitles () |> Async.AwaitPromise
            return titles |> Array.exists (fun t -> t.Contains tabTitle)
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
/// It also measures the size of the document, because VSCode from 1.123.0 loads every file of a
/// folder workspace twice. `extester.config.json` pins the editor below that version and records
/// the measurement.
///
/// Opens first and closes the other tabs second, rather than emptying the column first. A column
/// with no tabs left stops being a column: the response viewer then slides into the fixture
/// column's index, and the file opens beside the viewer instead of replacing it.
///
/// Opens the path rather than clicking the file in the Explorer, because `openResources` names
/// the file it opens and a click depends on where the tree has scrolled to. A doubled buffer was
/// once read as a fault in the Explorer route. It was not: the editor doubles the file whichever
/// route opens it, and the pin in `extester.config.json` is what holds it to one copy.
/// `openResources` opens into the focused column, which is why the focus command comes
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

/// Switches to the previous editor tab in the focused group. The document-aware status bar
/// resets to pending on this change, which is what the switch check waits for.
let previousEditor () : Async<unit> =
    async {
        let workbench = Workbench.create ()
        do! workbench.executeCommand "workbench.action.previousEditor" |> Async.AwaitPromise
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
///
/// Skips a `codelens-decoration` that is hidden. VSCode keeps the lens elements of the document
/// the editor showed before, hidden and with no `top`, and reuses them for the next document. A
/// read that counts them reports one lens for each block of the fixture plus one for each block
/// of the fixture before it. That reads as a provider painting twice, which it is not: the
/// remaining lenses carry the correct titles at the correct lines.
///
/// The one plain-text lens this suite reads, taken from the product rather than retyped:
/// `Ui.Tests.fsproj` links `Protocol.fs`, so the harvest below and every check that names the
/// title move together with the shipped words (docs/spec/0014-explain-missing-lenses.md,
/// Decision 2). `None` would mean the product paints no lens for `Script(0, true)` at all, which
/// no reading here could assert around, so the suite fails on the spot rather than harvesting
/// against a placeholder.
let noRequestsLensTitle =
    match Protocol.noRequestsLensTitle (Protocol.Script(0, true)) with
    | Some title -> title
    | None -> Assert.fail "Protocol.noRequestsLensTitle paints no lens for Script(0, true)"

/// A lens with a command id is an `<a id>`. A lens with a title and an empty command id is a
/// `<span>` (docs/spec/0014-explain-missing-lenses.md, Decision 2). Command lenses keep the
/// existing `a[id]` walk. Plain-text collection takes only the no-requests title: a wider span
/// harvest pulled in non-command nodes that stole Run-lens clicks on the core path.
let private lensAnchorsPrologue =
    sprintf
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
        var host = candidates[i].closest('[class*="codelens" i]');
        if (host && getComputedStyle(host).visibility !== 'hidden') { anchors.push(candidates[i]); }
    }
    var decorations = editor.querySelectorAll('[class*="codelens-decoration" i]');
    for (var d = 0; d < decorations.length; d++) {
        var host = decorations[d];
        if (getComputedStyle(host).visibility === 'hidden') { continue; }
        if (host.querySelector('a[id]')) { continue; }
        for (var c = 0; c < host.children.length; c++) {
            var child = host.children[c];
            if (child.tagName !== 'SPAN') { continue; }
            if (child.textContent.trim().indexOf('%s') >= 0) { anchors.push(child); }
        }
    }
    anchors.sort(function (l, r) {
        return l.getBoundingClientRect().top - r.getBoundingClientRect().top;
    });
    """
        noRequestsLensTitle

let private runLensScript (body: string) (arg: objnull) : Async<objnull> =
    let driver = VSBrowser.instance.driver
    let script = lensAnchorsPrologue + body

    let call: JS.Promise<objnull> =
        emitJsExpr (driver, script, arg) "$0.executeScript($1, $2)"

    call |> Async.AwaitPromise

/// Where the lenses sit, for a lens count that read wrong.
///
/// Reports each anchor with its title, the `top` its `codelens-decoration` host carries, and
/// whether that host is visible. A lens the editor kept from the document it showed before is
/// hidden and carries no `top`, so the reading separates a stale element the read should have
/// skipped from a lens the provider really painted at the wrong line.
let describeLensLayout () : Async<string> =
    async {
        try
            let body =
                """
                var out = [];
                for (var i = 0; i < anchors.length; i++) {
                    var host = anchors[i].closest('[class*="codelens" i]');
                    out.push('"' + anchors[i].textContent.trim() + '" at top '
                        + (host && host.style.top ? host.style.top : 'unset')
                        + ' ' + (host ? getComputedStyle(host).visibility : 'no host'));
                }
                return out.join('; ');
                """

            let! reading = runLensScript body (null: objnull)

            if isNull reading then
                return "no laid-out editor to measure"
            else
                let text = string reading
                return if text = "" then "no lens element in the editor" else text
        with e ->
            return sprintf "no measurement — the layout query raised: %s" e.Message
    }

/// How the editor painted a lens carrying a title: as a command VSCode can run, or as plain text.
type LensRendering =
    /// The lens's decoration holds an `<a id>`: VSCode runs a command when it is clicked.
    | RenderedAsCommandLink
    /// The lens's decoration holds the title in a `<span>` and no link at all: nothing to run.
    | RenderedAsPlainText
    /// No visible decoration carries the title.
    | TitleNotRendered
    /// The query itself did not run, which is no evidence either way.
    | RenderingUnreadable of reason: string

/// Whether the lens carrying `title` renders as a command or as plain text
/// (docs/spec/0014-explain-missing-lenses.md, Decision 2).
///
/// Reads the DOM rather than the outcome of a click: on a script that locates no block, no lens
/// could open a viewer whatever command it carried, so "the click did nothing" cannot tell a
/// plain-text lens from a lens that carries a command. The `<a id>` VSCode paints for a command
/// id is that tell, and its absence under the decoration that holds the title is what "no
/// attached command" looks like on screen.
let tryReadLensRendering (title: string) : Async<LensRendering> =
    async {
        try
            let body =
                """
                var hosts = editor.querySelectorAll('[class*="codelens-decoration" i]');
                for (var i = 0; i < hosts.length; i++) {
                    var host = hosts[i];
                    if (getComputedStyle(host).visibility === 'hidden') { continue; }
                    if (host.textContent.indexOf(arguments[0]) < 0) { continue; }
                    return host.querySelector('a[id]') ? 'link' : 'plain';
                }
                return 'absent';
                """

            let! reading = runLensScript body (box title)

            if isNull reading then
                return RenderingUnreadable "no laid-out editor to read"
            else
                match string reading with
                | "link" -> return RenderedAsCommandLink
                | "plain" -> return RenderedAsPlainText
                | _ -> return TitleNotRendered
        with e ->
            return RenderingUnreadable e.Message
    }

/// The highest line number the editor's gutter renders, for a document whose size read wrong.
///
/// Monaco renders a gutter number for a line it shows and for no line past the end of the
/// document, so this reading is a lower bound on the size of the document. It is independent of
/// `TextEditor.getText`, which reaches the document through the clipboard. A gutter that stops at
/// the size of the file, against a clipboard reading of twice that size, puts the fault in the
/// clipboard. A gutter that fills the viewport puts the fault in the document.
let describeGutterExtent () : Async<string> =
    async {
        try
            let body =
                """
                var cells = editor.querySelectorAll('.margin-view-overlays .line-numbers');
                var highest = 0;
                for (var i = 0; i < cells.length; i++) {
                    var n = parseInt(cells[i].textContent, 10);
                    if (!isNaN(n) && n > highest) { highest = n; }
                }
                return cells.length + ' gutter cells, highest line number ' + highest;
                """

            let! reading = runLensScript body (null: objnull)

            if isNull reading then
                return "no laid-out editor to measure"
            else
                return string reading
        with e ->
            return sprintf "no measurement — the gutter query raised: %s" e.Message
    }

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

/// Find-and-click by partial lens title. Prefers a command link (`<a id>`) when one matches, so
/// a plain-text span that carries the same words cannot take the click (VSCode only runs a
/// command from the link). Falls back to a plain-text span when no link matches, which is how
/// the no-requests lens is asserted to do nothing on click.
let tryClickCodeLensByTitle (title: string) : Async<bool> =
    tryClickLensAnchor
        """
        var link = null;
        var plain = null;
        for (var i = 0; i < anchors.length; i++) {
            if (anchors[i].textContent.indexOf(arguments[0]) < 0) { continue; }
            if (anchors[i].tagName === 'A' && anchors[i].id) { link = anchors[i]; break; }
            if (!plain && anchors[i].tagName === 'SPAN') { plain = anchors[i]; }
        }
        return link || plain;
        """
        (box title)

/// Find-and-click by zero-based lens index among command links only, top to bottom. Plain-text
/// spans are not clickable commands and must not shift the index of a Run or refusal lens.
let tryClickCodeLensByIndex (index: int) : Async<bool> =
    tryClickLensAnchor
        """
        var links = [];
        for (var i = 0; i < anchors.length; i++) {
            if (anchors[i].tagName === 'A' && anchors[i].id) { links.push(anchors[i]); }
        }
        if (arguments[0] >= links.length) { return null; }
        return links[arguments[0]];
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

/// The fixture editor's whole buffer, or `None` when the editor cannot be reached.
///
/// Reads through `TextEditor.getText`, which copies the document to the clipboard and reads it
/// back. That copy was once read as unreliable, because it returned the fixture twice over for a
/// document that painted the correct lenses. The copy was right and the document was wrong: the
/// lenses of the second copy sat below the viewport, and Monaco renders no lens for a line it does
/// not show. `Checks` measures the size of this reading against the file on disk.
let tryFixtureBufferText () : Async<string option> =
    async {
        try
            let! editor = fixtureEditor ()
            let! text = editor.getText () |> Async.AwaitPromise
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

/// True when the workbench is in the page.
///
/// A reload takes the workbench out of the page and builds it again. Every page object and every
/// workbench command starts by finding the workbench, so a call made between those two moments
/// raises rather than waits, and the raise reads as the command failing. A fresh companion process
/// does not settle this: the extension host and the page come back on their own schedules.
let tryWorkbenchPresent () : Async<bool> =
    async {
        try
            let driver = VSBrowser.instance.driver
            let script = "return document.querySelector('.monaco-workbench') ? 'yes' : 'no';"

            let call: JS.Promise<objnull> = emitJsExpr (driver, script) "$0.executeScript($1)"

            let! reading = call |> Async.AwaitPromise
            return not (isNull reading) && string reading = "yes"
        with _ ->
            return false
    }

/// True when a boolean HTML attribute is present. WebDriver reads a present boolean attribute
/// back as `"true"`, and an absent one as null. A missing element reads as `""` from
/// `tryElementProperty`, so both blanks must count as not set.
let private attributeIsPresent (value: string) = not (isNull (box value)) && value <> ""

/// Reads every copy button and every section shell's computed margin. Runs against whatever
/// document is current, so the caller must already be inside the viewer frame. One script keeps
/// both layout claims on one trip.
let private readCopySurface (driver: obj) : Async<CopySurfaceReading> =
    async {
        let script =
            """
            var buttons = document.querySelectorAll('.copy-button');
            var buttonOut = [];
            for (var i = 0; i < buttons.length; i++) {
                var b = buttons[i];
                var box = b.getBoundingClientRect();
                buttonOut.push({
                    key: b.getAttribute('data-copy') || '',
                    label: (b.textContent || '').trim(),
                    displayed: box.width > 0 && box.height > 0
                });
            }
            var shells = document.querySelectorAll('.section-shell');
            var margins = [];
            for (var s = 0; s < shells.length; s++) {
                margins.push(parseFloat(getComputedStyle(shells[s]).marginBottom) || 0);
            }
            var request = document.querySelector('.request');
            var headers = document.querySelector('.headers');
            return {
                buttons: buttonOut,
                margins: margins,
                requestOpen: !!(request && request.hasAttribute('open')),
                headersOpen: !!(headers && headers.hasAttribute('open'))
            };
            """

        let call: JS.Promise<obj> = emitJsExpr (driver, script) "$0.executeScript($1)"
        let! raw = call |> Async.AwaitPromise
        let buttonObjs: obj[] = unbox (raw?buttons: obj)

        let buttons =
            buttonObjs
            |> Array.map (fun (b: obj) ->
                { Key = unbox<string> (b?key: obj)
                  Label = unbox<string> (b?label: obj)
                  Displayed = unbox<bool> (b?displayed: obj) })

        let margins: float[] = unbox (raw?margins: obj)

        return
            { Buttons = buttons
              ShellMarginsPx = margins
              RequestOpen = unbox<bool> (raw?requestOpen: obj)
              HeadersOpen = unbox<bool> (raw?headersOpen: obj) }
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
                let! request = tryElementText view Viewer.requestSelector
                let! requestSummary = tryElementText view Viewer.requestSummarySelector
                let! requestOpen = tryElementAttribute view Viewer.requestSelector "open"
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
                          RequestText = request
                          RequestSummaryText = requestSummary
                          RequestOpen = attributeIsPresent requestOpen
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

/// Clicks the Request section's summary, which is how a user expands it. Returns false when the
/// frame or the summary could not be reached, so a caller pairs this with `Harness.eventually`
/// rather than treating one attempt as final.
///
/// The click goes through the summary and not through a script that sets `open`: the section
/// being reachable by clicking is part of what this claims. Unlike a CodeLens, this is ordinary
/// page content, so the element's own `click()` is a real user gesture here.
let tryExpandRequestSection () : Async<bool> =
    async {
        try
            let! group = editorGroup viewerGroupIndex
            let view = WebView.createInGroup group
            let mutable switched = false

            try
                do! switchToFrameTimed view frameSwitchTimeoutMs |> Async.AwaitPromise
                switched <- true

                let! summary = view.findWebElement (By.css Viewer.requestSummarySelector) |> Async.AwaitPromise
                do! summary.click () |> Async.AwaitPromise

                switched <- false
                do! view.switchBack () |> Async.AwaitPromise
                return true
            with _ ->
                if switched then
                    try
                        do! view.switchBack () |> Async.AwaitPromise
                    with _ ->
                        ()

                return false
        with _ ->
            return false
    }

/// What one copy-button click produced inside the viewer frame.
type CopyClickReading =
    {
        /// The button's label after the click settled on success or failure.
        Label: string
        /// The text `navigator.clipboard.writeText` was handed, whether or not the write then
        /// resolved. `None` when the witness recorded no call at all.
        ///
        /// This is the write's argument, and not a read back off the system clipboard: headless
        /// Linux here has no clipboard tool to read one with. It proves what the extension put
        /// on the clipboard, not what a paste elsewhere would produce.
        WrittenText: string option
        /// Whether that write resolved. False on the refused path Decision 8 reports as
        /// `Copy failed`.
        Granted: bool
        RequestOpen: bool
        HeadersOpen: bool
    }

/// Whether the witness lets the real `navigator.clipboard.writeText` run, or makes it reject.
///
/// `Refused` is the only way a check reaches Decision 8's failure path: the platform this suite
/// runs on grants the write, and the spec is explicit that "the failure path is not decoration".
type ClipboardGrant =
    | Granted
    | Refused

/// Installs a witness over `navigator.clipboard.writeText` in the viewer frame, and sets whether
/// the next write is granted. The real write still runs when granted; either way the text handed
/// in is stored with the outcome. Headless Linux in this suite has no clipboard tool, and a
/// resolved write is what Decision 11 measured VSCode granting.
let private installClipboardWitnessScript =
    """
    var refuses = arguments[0];
    if (!window.__fshttpCopyWitness) {
      window.__fshttpCopyWitness = true;
      var orig = navigator.clipboard.writeText.bind(navigator.clipboard);
      navigator.clipboard.writeText = function (text) {
        if (window.__fshttpCopyRefuses) {
          window.__fshttpLastCopy = { text: text, granted: false };
          return Promise.reject(new Error('ui-test: clipboard refused'));
        }
        return orig(text).then(function () {
          window.__fshttpLastCopy = { text: text, granted: true };
        }, function (err) {
          window.__fshttpLastCopy = { text: text, granted: false };
          return Promise.reject(err);
        });
      };
    }
    window.__fshttpCopyRefuses = refuses;
    window.__fshttpLastCopy = null;
    """

/// Reads the button's settled label, the witness, and both sections' open state. Returns null
/// until the write has been recorded *and* the label has left its resting text: a retry that
/// lands on a leftover `Copied` must not report before the new write is in.
let private readCopyClickScript =
    """
    var key = arguments[0];
    var resting = arguments[1];
    var button = document.querySelector('.copy-button[data-copy="' + key + '"]');
    if (!button) { return null; }
    var label = (button.textContent || '').trim();
    var copy = window.__fshttpLastCopy;
    if (copy == null || label === resting) { return null; }
    var request = document.querySelector('.request');
    var headers = document.querySelector('.headers');
    return {
      label: label,
      written: copy.text,
      granted: copy.granted,
      requestOpen: !!(request && request.hasAttribute('open')),
      headersOpen: !!(headers && headers.hasAttribute('open'))
    };
    """

/// How long one click waits inside the frame for the clipboard promise to settle and the label to
/// flash. Deliberately not a check-tunable deadline, on the same terms as `frameSwitchTimeoutMs`:
/// it bounds one in-frame interaction, and not a product surface a check waits on.
let private copySettleTimeoutMs = 3_000.

/// The gap between two reads of the settling label. Well under the 1200 ms the flash lasts, so a
/// label that has already flashed cannot be restored between two polls and missed entirely.
let private copySettlePollMs = 50

/// Clicks the copy button for `key` under `grant`, waits for its label to leave its resting text,
/// and reads the clipboard witness plus the two collapsible sections' open state. Pair with
/// `Harness.eventually`: a missed frame switch or a button that has not painted yet fails the
/// attempt.
let tryClickCopyButton (grant: ClipboardGrant) (key: string) : Async<CopyClickReading option> =
    async {
        try
            let! group = editorGroup viewerGroupIndex
            let view = WebView.createInGroup group
            let driver = VSBrowser.instance.driver
            let mutable switched = false

            try
                do! switchToFrameTimed view frameSwitchTimeoutMs |> Async.AwaitPromise
                switched <- true

                let refuses = grant = Refused

                let install: JS.Promise<objnull> =
                    emitJsExpr (driver, installClipboardWitnessScript, refuses) "$0.executeScript($1, $2)"

                do! install |> Async.AwaitPromise |> Async.Ignore

                let selector = sprintf "%s[data-copy=\"%s\"]" Viewer.copyButtonSelector key
                let! button = view.findWebElement (By.css selector) |> Async.AwaitPromise
                do! button.click () |> Async.AwaitPromise

                // The flash is asynchronous on the clipboard promise. Poll inside the frame so
                // one attempt covers click through settled label without leaving and re-entering.
                let deadline = Proc.now () + copySettleTimeoutMs

                let rec poll () =
                    async {
                        let call: JS.Promise<objnull> =
                            emitJsExpr
                                (driver, readCopyClickScript, key, copyButtonRestingLabel)
                                "$0.executeScript($1, $2, $3)"

                        let! raw = call |> Async.AwaitPromise

                        match raw with
                        | null ->
                            if Proc.now () >= deadline then
                                return None
                            else
                                do! Async.Sleep copySettlePollMs
                                return! poll ()
                        | settled -> return Some(unbox<obj> settled)
                    }

                let! settled = poll ()

                switched <- false
                do! view.switchBack () |> Async.AwaitPromise

                match settled with
                | None -> return None
                | Some raw ->
                    let writtenObj: objnull = raw?written: objnull

                    let written =
                        match writtenObj with
                        | null -> None
                        | text -> Some(unbox<string> text)

                    return
                        Some
                            { Label = unbox<string> (raw?label: obj)
                              WrittenText = written
                              Granted = unbox<bool> (raw?granted: obj)
                              RequestOpen = unbox<bool> (raw?requestOpen: obj)
                              HeadersOpen = unbox<bool> (raw?headersOpen: obj) }
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

/// Enters the viewer frame, reads the copy buttons and the shell spacing, and switches back.
/// `None` when the frame cannot be entered. Separate from `tryReadResponseViewer` so that only
/// the copy checks pay for the extra script round-trip.
let tryReadCopySurface () : Async<CopySurfaceReading option> =
    async {
        try
            let! group = editorGroup viewerGroupIndex
            let view = WebView.createInGroup group
            let mutable switched = false

            try
                do! switchToFrameTimed view frameSwitchTimeoutMs |> Async.AwaitPromise
                switched <- true

                let! surface = readCopySurface VSBrowser.instance.driver

                switched <- false
                do! view.switchBack () |> Async.AwaitPromise
                return Some surface
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

/// True when the copy button for `key` shows exactly `label`. Pair with `Harness.eventually` for
/// the label's return to its resting text after the flash.
let tryCopyButtonLabel (key: string) (label: string) : Async<bool> =
    async {
        match! tryReadCopySurface () with
        | None -> return false
        | Some surface -> return surface.Buttons |> Array.exists (fun b -> b.Key = key && b.Label = label)
    }
