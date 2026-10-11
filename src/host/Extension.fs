module Extension

open Fable.Core
open Fable.Core.JsInterop
open Js
open Vscode
open Node
open Protocol

let mutable private companionHandle: Companion.Handle option = None
let mutable private session = Restart.initial

[<Literal>]
let private restartCommandId = "fshttpStudio.restartCompanion"

[<Literal>]
let private getSdkLabel = "Get the .NET SDK"

[<Literal>]
let private dotnetDownloadUrl = "https://aka.ms/dotnet/download"

/// The `fshttpStudio.dotnetPath` override. It is an explicit path to a `dotnet` executable, or
/// `None` to detect one on PATH automatically. We own this setting instead of the .NET Install
/// Tool's `existingDotnetPath`, so a user does not have to install that extension for one key.
let private configuredDotnetPath () : string option =
    let path = (workspace.getConfiguration "fshttpStudio").get "dotnetPath"

    if System.String.IsNullOrWhiteSpace path then
        None
    else
        Some path

/// `None` when the file is missing or does not parse, which means a broken install.
let private companionFrameworkVersion (runtimeConfigPath: string) : string option =
    try
        let json: obj = JS.JSON.parse (Node.fs.readFileSync (runtimeConfigPath, "utf8"))

        match emitJsExpr json "$0.runtimeOptions.framework.version": obj with
        | :? string as version -> Some version
        | _ -> None
    with _ ->
        None

let activate (context: ExtensionContext) =
    let item = window.createStatusBarItem (statusBarAlignmentLeft, 100.0)
    // `StatusBar` discards a write until it has the item.
    StatusBar.register item
    context.subscriptions.Add(box item)

    CodeLensProvider.setOnLocated StatusBar.onLocated
    context.subscriptions.Add(box (window.onDidChangeActiveTextEditor StatusBar.onActiveEditorChanged))
    StatusBar.onActiveEditorChanged window.activeTextEditor
    StatusBar.setCompanionState Starting

    RunCommand.setExtensionUri context.extensionUri

    context.subscriptions.Add(
        box (languages.registerCodeLensProvider (nonNull (box {| language = "fsharp" |}), CodeLensProvider.provider))
    )

    context.subscriptions.Add(box (RunCommand.register ()))
    context.subscriptions.Add(box (RunCommand.registerExplain ()))
    context.subscriptions.Add(box (RunCommand.registerExplainCompanionStopped ()))

    let companionDll =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.dll" |]

    // The runtimeconfig beside the DLL is the one source of the SDK floor.
    let requiredMajor =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.runtimeconfig.json" |]
        |> companionFrameworkVersion
        |> sdkFloor

    let requiredSdk = sprintf ".NET %d SDK or newer" requiredMajor

    let showNoSdkToast (dotnetPathOverride: string option) =
        let message =
            match dotnetPathOverride with
            | Some path ->
                "FsHttp.Studio's `fshttpStudio.dotnetPath` setting ("
                + path
                + ") did not resolve to a "
                + requiredSdk
                + ". Correct the path, or clear the setting to detect a `dotnet` on PATH automatically."
            | None ->
                "FsHttp.Studio needs a "
                + requiredSdk
                + " to run requests, but found none. Install one, "
                + "or set the `fshttpStudio.dotnetPath` setting to your `dotnet` executable."

        onResolved (window.showWarningMessage (message, getSdkLabel)) (fun chosen ->
            if unbox<string> chosen = getSdkLabel then
                commands.executeCommand ("vscode.open", uri.parse dotnetDownloadUrl) |> ignore)

    let lastDotnetPathOverride: string option ref = ref None

    // A Run at the cursor in the SdkNotFound state shows the toast of the latest start.
    context.subscriptions.Add(
        box (RunCommand.registerRunAtCursor (fun () -> showNoSdkToast lastDotnetPathOverride.Value))
    )

    // A Restart moves the epoch on, so every event of an earlier start or Companion is stale.
    let startSequence (epoch: int) =
        let onState state =
            if Restart.accepts session epoch then
                StatusBar.setCompanionState state
                RunCommand.setCompanionState state
                CodeLensProvider.setState state

                if state = SdkNotFound then
                    showNoSdkToast lastDotnetPathOverride.Value

        // FSI's `#r "nuget:"` restore drives `dotnet msbuild`, which a runtime-only install lacks.
        let dotnetPathOverride = configuredDotnetPath ()
        lastDotnetPathOverride.Value <- dotnetPathOverride
        let dotnetPath = dotnetPathOverride |> Option.defaultValue "dotnet"

        onState Starting

        childProcess.execFile (
            dotnetPath,
            [| "--list-sdks" |],
            nonNull (box {| timeout = 10000 |}),
            (fun err stdout _stderr ->
                if Restart.accepts session epoch then
                    if isNullish err && hasSdkAtFloor requiredMajor stdout then
                        let handle = Companion.start dotnetPath companionDll onState
                        CodeLensProvider.setHandle handle
                        RunCommand.setHandle handle
                        companionHandle <- Some handle
                    else
                        onState SdkNotFound)
        )

    let restartCompanion () =
        let next, decision = Restart.onCommand session
        session <- next

        match decision with
        | Restart.NotifyRestarting -> window.showInformationMessage Refusals.companionRestarting |> ignore
        | Restart.BeginRestart ->
            let epoch = session.Epoch
            StatusBar.setCompanionState Starting
            RunCommand.setCompanionState Starting
            CodeLensProvider.setState Starting

            let stopped =
                match companionHandle with
                | Some handle -> Companion.stopForRestart handle
                | None -> resolved ()

            companionHandle <- None

            onResolved stopped (fun () ->
                session <- Restart.onStopped session
                startSequence epoch)

    context.subscriptions.Add(
        commands.registerCommand (restartCommandId, System.Action<obj, obj>(fun _ _ -> restartCompanion ()))
    )

    startSequence session.Epoch

let deactivate () : JS.Promise<unit> =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> resolved ()
