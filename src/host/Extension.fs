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
let private getSdkLabel = "Get the .NET SDK"

[<Literal>]
let private dotnetDownloadUrl = "https://aka.ms/dotnet/download"

/// SDK floor to fall back on when the companion's runtimeconfig can't be read (a broken install) —
/// the major the companion's toolchain is pinned to (ADR-0002). The primary source is the shipped
/// `Companion.runtimeconfig.json`; this only guards a missing/corrupt package.
[<Literal>]
let private fallbackRequiredMajor = 10

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

/// The major .NET version the companion was built for, read from the `Companion.runtimeconfig.json`
/// that ships beside the DLL (`framework.version` "10.0.0" → 10). This is the single source of the
/// SDK floor: bumping the companion's target framework moves it with no other edits. `None` when the
/// packaged file can't be read or parsed, which is a broken install.
let private companionTargetMajor (runtimeConfigPath: string) : int option =
    try
        let json: obj = JS.JSON.parse (Node.fs.readFileSync (runtimeConfigPath, "utf8"))
        let version: string = emitJsExpr json "$0.runtimeOptions.framework.version"

        match version.Split('.') |> Array.tryHead |> Option.map System.Int32.TryParse with
        | Some(true, major) -> Some major
        | _ -> None
    with _ ->
        None

/// True when `dotnet --list-sdks` reports at least one SDK with major version ≥ `requiredMajor` —
/// the floor the companion needs for FSI's `#r "nuget:"` restore (an SDK, not just a runtime). The
/// companion rolls forward onto any newer major, so a floor-or-above match is genuinely runnable.
let private hasSdkAtLeast (requiredMajor: int) (listSdksOutput: string) : bool =
    listSdksOutput.Split('\n')
    |> Array.exists (fun line -> tryParseSdkMajor line |> Option.exists (fun major -> major >= requiredMajor))

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

    // The SDK floor is the companion's own build target, read from the runtimeconfig shipped beside
    // the DLL — one source of truth, so a companion TFM bump moves the floor and the guidance with it.
    let requiredMajor =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.runtimeconfig.json" |]
        |> companionTargetMajor
        |> Option.defaultValue fallbackRequiredMajor

    let onState state =
        setStatusText (Companion.statusText state)
        CodeLensProvider.setReady (state = Companion.Ready)

    let startCompanion (dotnetPath: string) =
        let handle = Companion.start dotnetPath companionDll onState
        CodeLensProvider.setHandle handle
        RunCommand.setHandle handle
        companionHandle <- Some handle

    // Require a user-installed .NET SDK (the Ionide/C# Dev Kit model), replacing the earlier
    // runtime-only acquisition: FSI's `#r "nuget:"` restore drives `dotnet msbuild`, which a runtime
    // lacks. Resolve the `fshttpStudio.dotnetPath` override, else `"dotnet"` off PATH; confirm it
    // carries an SDK at or above the companion's target major via `--list-sdks` before spawning.
    let dotnetPathOverride = configuredDotnetPath ()
    let dotnetPath = dotnetPathOverride |> Option.defaultValue "dotnet"

    let requiredSdk = sprintf ".NET %d SDK or newer" requiredMajor

    // First-run guidance when no in-floor SDK is reachable: we deliberately own this "SDK not found"
    // tail — the trade-off Option B accepts — pointing at the download page and the override. When
    // the override is set but didn't resolve to an SDK, say so rather than "none was found".
    let notifyNoSdk () =
        setStatusText (Companion.statusText Companion.SdkNotFound)

        let message =
            match dotnetPathOverride with
            | Some path ->
                "FsHttp.Studio's `fshttpStudio.dotnetPath` setting ("
                + path
                + ") didn't resolve to a "
                + requiredSdk
                + ". Check the path, or clear the setting to auto-detect a `dotnet` on PATH."
            | None ->
                "FsHttp.Studio needs a "
                + requiredSdk
                + " to run requests, but none was found. Install one, "
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
            if isNullish err && hasSdkAtLeast requiredMajor stdout then
                startCompanion dotnetPath
            else
                notifyNoSdk ())
    )

let deactivate () =
    match companionHandle with
    | Some handle -> Companion.stop handle
    | None -> ()
