module Extension

open Fable.Core
open Fable.Core.JsInterop
open Vscode
open Node

let mutable private statusItem: StatusBarItem option = None
let mutable private companionHandle: Companion.Handle option = None

let private setStatusText (text: string) =
    match statusItem with
    | Some item -> item.text <- "FsHttp.Studio: " + text
    | None -> ()

/// Settle a JS promise without pulling in a promise CE — the extension only needs to react to
/// the .NET Install Tool's single `dotnet.acquire` call.
[<Emit("$0.then($1, $2)")>]
let private onSettled (_p: JS.Promise<'T>) (_onOk: 'T -> unit) (_onErr: obj -> unit) : unit = jsNative

/// Reads the `dotnetPath` off the Install Tool's `IDotnetAcquireResult`.
[<Emit("$0.dotnetPath")>]
let private dotnetPathOf (_result: obj) : string = jsNative

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

    let startCompanion (dotnetPath: string) =
        let handle = Companion.start dotnetPath companionDll onState
        CodeLensProvider.setHandle handle
        RunCommand.setHandle handle
        companionHandle <- Some handle

    // Acquire a .NET 10 runtime on demand via ms-dotnettools.vscode-dotnet-runtime (ADR / #57):
    // the Install Tool downloads it on first use behind its own progress notification and returns
    // the `dotnet` host path to run our framework-dependent companion. `10.0` tracks global.json
    // (10.0.200); users with their own .NET 10 can point the Install Tool at it via its
    // `dotnetAcquisitionExtension.existingDotnetPath` setting (documented in the README).
    let acquireArgs =
        {| version = "10.0"
           mode = "runtime"
           requestingExtensionId = context.extension.id |}

    onSettled
        (commands.executeCommand ("dotnet.acquire", nonNull (box acquireArgs)))
        (fun result -> startCompanion (dotnetPathOf result))
        (fun _ -> setStatusText (Companion.statusText Companion.SdkNotFound))

let deactivate () =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> ()
