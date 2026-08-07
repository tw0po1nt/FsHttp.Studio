// PROTOTYPE — hand-rolled ExTester + selenium-webdriver surface. Grown from 005 with the
// pieces the companion-death slice needs: a Workbench (to reload the window), CodeLens by
// index, and lens enumeration (so "the lens is gone" is assertable, not just "click failed").
module ExTester

open System
open Fable.Core
open Fable.Core.JsInterop

type WebElement =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>

type CodeLens =
    abstract getText: unit -> JS.Promise<string>
    abstract click: unit -> JS.Promise<unit>

type TextEditor =
    abstract getCodeLenses: unit -> JS.Promise<CodeLens[]>

type WebView =
    abstract switchToFrame: unit -> JS.Promise<unit>
    abstract switchBack: unit -> JS.Promise<unit>
    abstract findWebElement: locator: obj -> JS.Promise<WebElement>

type VSBrowser =
    abstract openResources: [<ParamArray>] paths: string[] -> JS.Promise<unit>
    abstract waitForWorkbench: unit -> JS.Promise<unit>
    abstract takeScreenshot: name: string -> JS.Promise<unit>
    abstract driver: obj

type Notification =
    abstract getMessage: unit -> JS.Promise<string>

type Workbench =
    abstract executeCommand: command: string -> JS.Promise<unit>
    abstract getNotifications: unit -> JS.Promise<Notification[]>

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

[<Import("By", "selenium-webdriver")>]
let By: ByStatic = jsNative

let sleep (ms: int) : JS.Promise<unit> =
    emitJsExpr (float ms) "new Promise(r => setTimeout(r, $0))"

/// Poll `action` until it returns Some, or until `deadlineMs` elapses.
let eventually (deadlineMs: int) (intervalMs: int) (action: unit -> Async<'a option>) : Async<'a> =
    async {
        let started = DateTime.Now

        let rec loop () =
            async {
                let! hit = action ()

                match hit with
                | Some v -> return v
                | None ->
                    let elapsed = (DateTime.Now - started).TotalMilliseconds

                    if elapsed > float deadlineMs then
                        return Assert.throwJs (sprintf "eventually timed out after %d ms" deadlineMs)
                    else
                        do! sleep intervalMs |> Async.AwaitPromise
                        return! loop ()
            }

        return! loop ()
    }

/// Like `eventually`, but a timeout is an answer rather than a failure. Used for the negative
/// observations this slice has to *report* (does the lens survive the companion's death?).
let eventuallyOrNone (deadlineMs: int) (intervalMs: int) (action: unit -> Async<'a option>) : Async<'a option> =
    async {
        let started = DateTime.Now

        let rec loop () =
            async {
                let! hit = action ()

                match hit with
                | Some v -> return Some v
                | None ->
                    let elapsed = (DateTime.Now - started).TotalMilliseconds

                    if elapsed > float deadlineMs then
                        return None
                    else
                        do! sleep intervalMs |> Async.AwaitPromise
                        return! loop ()
            }

        return! loop ()
    }

let switchToFrameTimed (view: WebView) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (view, timeoutMs) "$0.switchToFrame($1)"

let waitForWorkbenchTimed (browser: VSBrowser) (timeoutMs: float) : JS.Promise<unit> =
    emitJsExpr (browser, timeoutMs) "$0.waitForWorkbench($1)"

let openResource (browser: VSBrowser) (path: string) : JS.Promise<unit> =
    emitJsExpr (browser, path) "$0.openResources($1)"
