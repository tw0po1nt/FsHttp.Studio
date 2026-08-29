module Extension

open Fable.Core
open Fable.Core.JsInterop
open Js
open Vscode
open Node
open Protocol

let mutable private companionHandle: Companion.Handle option = None

[<Literal>]
let private getSdkLabel = "Get the .NET SDK"

[<Literal>]
let private dotnetDownloadUrl = "https://aka.ms/dotnet/download"

/// Reached only when the shipped `Companion.runtimeconfig.json` is missing or corrupt.
[<Literal>]
let private fallbackRequiredMajor = 10

/// Reacts to a fulfilled JS promise without a promise CE. The single `showWarningMessage` that
/// the SDK-not-found guidance raises uses it.
[<Emit("$0.then($1)")>]
let private onResolved (_p: JS.Promise<'T>) (_onOk: 'T -> unit) : unit = jsNative

/// The `fshttpStudio.dotnetPath` override. It is an explicit path to a `dotnet` executable, or
/// `None` to detect one on PATH automatically. We own this setting instead of the .NET Install
/// Tool's `existingDotnetPath`, so a user does not have to install that extension for one key.
let private configuredDotnetPath () : string option =
    let path = (workspace.getConfiguration "fshttpStudio").get "dotnetPath"

    if System.String.IsNullOrWhiteSpace path then
        None
    else
        Some path

/// The major version of the SDK that a `dotnet --list-sdks` line names (`10.0.100 [/path]` →
/// `10`). Returns `None` for a blank line, or for a line that this function cannot parse.
let private tryParseSdkMajor (listSdksLine: string) : int option =
    match listSdksLine.Trim().Split(' ') |> Array.tryHead with
    | Some version when version <> "" ->
        match version.Split('.') |> Array.tryHead |> Option.map System.Int32.TryParse with
        | Some(true, major) -> Some major
        | _ -> None
    | _ -> None

/// The major .NET version that the companion targets. It comes from the
/// `Companion.runtimeconfig.json` that ships beside the DLL (`framework.version` "10.0.0" →
/// 10). This is the single source of the SDK floor, so a change to the companion's target
/// framework moves the floor with no other edits. Returns `None` when this function cannot read
/// or parse the packaged file, which means a broken install.
let private companionTargetMajor (runtimeConfigPath: string) : int option =
    try
        let json: obj = JS.JSON.parse (Node.fs.readFileSync (runtimeConfigPath, "utf8"))
        let version: string = emitJsExpr json "$0.runtimeOptions.framework.version"

        match version.Split('.') |> Array.tryHead |> Option.map System.Int32.TryParse with
        | Some(true, major) -> Some major
        | _ -> None
    with _ ->
        None

/// True when `dotnet --list-sdks` reports at least one SDK with a major version ≥
/// `requiredMajor`. That is the floor the companion needs for FSI's `#r "nuget:"` restore, and
/// that restore needs a full SDK. The companion rolls forward onto any newer major
/// version, so a match at the floor or above is genuinely runnable.
let private hasSdkAtLeast (requiredMajor: int) (listSdksOutput: string) : bool =
    listSdksOutput.Split('\n')
    |> Array.exists (fun line -> tryParseSdkMajor line |> Option.exists (fun major -> major >= requiredMajor))

let activate (context: ExtensionContext) =
    let item = window.createStatusBarItem (statusBarAlignmentLeft, 100.0)
    // `StatusBar` discards a write until it holds the item.
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
        |> companionTargetMajor
        |> Option.defaultValue fallbackRequiredMajor

    let onState state =
        StatusBar.setCompanionState state
        CodeLensProvider.setReady (state = Ready)

    let startCompanion (dotnetPath: string) =
        let handle = Companion.start dotnetPath companionDll onState
        CodeLensProvider.setHandle handle
        RunCommand.setHandle handle
        companionHandle <- Some handle

    // FSI's `#r "nuget:"` restore drives `dotnet msbuild`, which a runtime-only install lacks.
    let dotnetPathOverride = configuredDotnetPath ()
    let dotnetPath = dotnetPathOverride |> Option.defaultValue "dotnet"

    let requiredSdk = sprintf ".NET %d SDK or newer" requiredMajor

    let notifyNoSdk () =
        StatusBar.setCompanionState SdkNotFound

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

    childProcess.execFile (
        dotnetPath,
        [| "--list-sdks" |],
        nonNull (box {| timeout = 10000 |}),
        (fun err stdout _stderr ->
            if isNullish err && hasSdkAtLeast requiredMajor stdout then
                startCompanion dotnetPath
            else
                notifyNoSdk ())
    )

let deactivate () =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> ()
