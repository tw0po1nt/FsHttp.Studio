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

[<Literal>]
let private getSdkLabel = "Get .NET 10 SDK"

[<Literal>]
let private dotnetDownloadUrl = "https://aka.ms/dotnet/download"

/// React to a settled JS promise's fulfilment without pulling in a promise CE — used for the
/// single `showWarningMessage` the SDK-not-found guidance raises.
[<Emit("$0.then($1)")>]
let private onResolved (_p: JS.Promise<'T>) (_onOk: 'T -> unit) : unit = jsNative

/// JS `== null` (null *or* undefined) — how `execFile` signals success on its error argument.
[<Emit("$0 == null")>]
let private isNullish (_x: obj) : bool = jsNative

/// The `fshttpStudio.dotnetPath` override: an explicit path to a `dotnet` executable, or `None`
/// to auto-detect one on PATH. We own this setting rather than leaning on the .NET Install Tool's
/// `existingDotnetPath`, so users needn't install that extension just for a config key.
let private configuredDotnetPath () : string option =
    let path = (workspace.getConfiguration "fshttpStudio").get "dotnetPath"

    if System.String.IsNullOrWhiteSpace path then
        None
    else
        Some path

/// The major version of the SDK a `dotnet --list-sdks` line names (`10.0.100 [/path]` → `10`), or
/// `None` for a blank or unparseable line.
let private tryParseSdkMajor (listSdksLine: string) : int option =
    match listSdksLine.Trim().Split(' ') |> Array.tryHead with
    | Some version when version <> "" ->
        match version.Split('.') |> Array.tryHead |> Option.map System.Int32.TryParse with
        | Some(true, major) -> Some major
        | _ -> None
    | _ -> None

/// True when `dotnet --list-sdks` reports at least one SDK with major version ≥ 10 — the floor
/// the companion needs for FSI's `#r "nuget:"` restore (an SDK, not just a runtime).
let private hasSdk10OrLater (listSdksOutput: string) : bool =
    listSdksOutput.Split('\n')
    |> Array.exists (fun line -> tryParseSdkMajor line |> Option.exists (fun major -> major >= 10))

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

    // Require a user-installed .NET 10 SDK (the Ionide/C# Dev Kit model), replacing the earlier
    // runtime-only acquisition: FSI's `#r "nuget:"` restore drives `dotnet msbuild`, which a runtime
    // lacks. Resolve the `fshttpStudio.dotnetPath` override, else `"dotnet"` off PATH; confirm it
    // carries a ≥ 10.0 SDK via `--list-sdks` before spawning the companion against it, otherwise guide.
    let dotnetPathOverride = configuredDotnetPath ()
    let dotnetPath = dotnetPathOverride |> Option.defaultValue "dotnet"

    // First-run guidance when no ≥ 10.0 SDK is reachable: we deliberately own this "SDK not found"
    // tail — the trade-off Option B accepts — pointing at the download page and the override. When
    // the override is set but didn't resolve to an SDK, say so rather than "none was found".
    let notifyNoSdk () =
        setStatusText (Companion.statusText Companion.SdkNotFound)

        let message =
            match dotnetPathOverride with
            | Some path ->
                "FsHttp.Studio needs a .NET 10 SDK to run requests, but the `fshttpStudio.dotnetPath` setting ("
                + path
                + ") didn't resolve to one. Check the path, or clear the setting to auto-detect a `dotnet` on PATH."
            | None ->
                "FsHttp.Studio needs a .NET 10 SDK to run requests, but none was found. Install one, "
                + "or set the `fshttpStudio.dotnetPath` setting to your `dotnet` executable."

        onResolved (window.showWarningMessage (message, getSdkLabel)) (fun chosen ->
            if unbox<string> chosen = getSdkLabel then
                commands.executeCommand ("vscode.open", uri.parse dotnetDownloadUrl) |> ignore)

    // Bound the probe: a wedged `dotnet` is killed on `timeout` expiry, and the killed-child error
    // routes to `notifyNoSdk` like any other failure — so a hung host can't stall activation.
    childProcess.execFile (
        dotnetPath,
        [| "--list-sdks" |],
        nonNull (box {| timeout = 10000 |}),
        (fun err stdout _stderr ->
            if isNullish err && hasSdk10OrLater stdout then
                startCompanion dotnetPath
            else
                notifyNoSdk ())
    )

let deactivate () =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> ()
