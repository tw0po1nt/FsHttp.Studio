module Extension

open Vscode
open Node

let mutable private statusItem: StatusBarItem option = None
let mutable private companionHandle: Companion.Handle option = None

let private setStatusText (text: string) =
    match statusItem with
    | Some item -> item.text <- "FsHttp.Studio: " + text
    | None -> ()

let activate (context: ExtensionContext) =
    let item = window.createStatusBarItem (statusBarAlignmentLeft, 100.0)
    setStatusText (Companion.statusText Companion.Starting)
    item.show ()
    context.subscriptions.Add(box item)
    statusItem <- Some item

    RunCommand.setExtensionUri context.extensionUri

    context.subscriptions.Add(
        box (languages.registerCodeLensProvider (nonNull (box {| language = "fsharp" |}), CodeLensProvider.provider))
    )

    context.subscriptions.Add(box (RunCommand.register ()))

    let companionDll =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.dll" |]

    let onState state =
        setStatusText (Companion.statusText state)
        CodeLensProvider.setReady (state = Companion.Ready)

    let handle = Companion.start companionDll onState
    CodeLensProvider.setHandle handle
    RunCommand.setHandle handle
    companionHandle <- Some handle

let deactivate () =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> ()
