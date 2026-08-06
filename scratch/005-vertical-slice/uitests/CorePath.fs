// PROTOTYPE — core path's first Run: open fixture, click lens, assert viewer DOM.
module CorePath

open Fable.Core
open Fable.Mocha
open ExTester

let private lensTitle = "▶ Run request"
let private probeMarker = "vertical-slice-005"

let private tryGetLens () =
    async {
        try
            let editor = TextEditor.create ()
            let! lens = TextEditor.getCodeLensByTitle editor lensTitle |> Async.AwaitPromise
            let! text = lens.getText () |> Async.AwaitPromise

            if text.Contains(lensTitle) then
                return Some lens
            else
                return None
        with _ ->
            return None
    }

let private tryReadViewer () =
    async {
        let view = WebView.create ()
        let mutable switched = false

        try
            do! switchToFrameTimed view 5000. |> Async.AwaitPromise
            switched <- true

            // Prefer the happy-path status node; fall back to the whole root so a runtime
            // error text still fails the assertion with a useful message.
            let! rootText =
                async {
                    try
                        let! el = view.findWebElement (By.css ".status-code") |> Async.AwaitPromise
                        return! el.getText () |> Async.AwaitPromise
                    with _ ->
                        let! el = view.findWebElement (By.css "#root") |> Async.AwaitPromise
                        return! el.getText () |> Async.AwaitPromise
                }

            let! bodyText =
                async {
                    try
                        let! el = view.findWebElement (By.css ".response-body") |> Async.AwaitPromise
                        return! el.getText () |> Async.AwaitPromise
                    with _ ->
                        return rootText
                }

            do! view.switchBack () |> Async.AwaitPromise
            switched <- false

            if rootText.Contains("Runtime error") || rootText.Contains("Running") then
                return None
            elif rootText.Contains("200") then
                return Some(rootText, bodyText)
            else
                return None
        with _ ->
            if switched then
                try
                    do! view.switchBack () |> Async.AwaitPromise
                with _ ->
                    ()

            return None
    }

let private corePathTest =
    async {
        let browser = VSBrowser.instance
        do! waitForWorkbenchTimed browser 120_000. |> Async.AwaitPromise

        // Give the companion time to spawn and publish CodeLenses for the open .fsx.
        do! sleep 3_000 |> Async.AwaitPromise

        let! lens = eventually 90_000 1_000 tryGetLens
        let! title = lens.getText () |> Async.AwaitPromise
        Expect.stringContains title lensTitle "CodeLens title in the workbench"
        do! lens.click () |> Async.AwaitPromise

        let! statusText, bodyText = eventually 120_000 2_000 tryReadViewer
        Expect.stringContains statusText "200" "status code in the response viewer DOM"
        Expect.stringContains bodyText probeMarker "JSON probe marker in the response viewer DOM"
    }

let tests =
    testList
        "005 core path vertical slice"
        [ testCaseAsync "Run request renders JSON body and status in the viewer" corePathTest ]

Mocha.runTests tests |> ignore
