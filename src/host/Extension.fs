module Extension

open Fable.Core
open Fable.Core.JsInterop
open Js
open Vscode
open Node
open Protocol

let mutable private statusItem: StatusBarItem option = None
let mutable private companionHandle: Companion.Handle option = None

/// Writes the status-bar body, or hides the item when `statusText` has nothing to say
/// (docs/spec/0014-explain-missing-lenses.md, Decision 6). A no-op until `activate` registers
/// the item.
let private setStatusText (text: string option) =
    match statusItem, text with
    | Some item, Some body ->
        item.text <- "FsHttp.Studio: " + body
        item.show ()
    | Some item, None -> item.hide ()
    | None, _ -> ()

/// The status bar for a companion state. `ScriptPending` stands in until a later ticket feeds
/// the active editor's real `ScriptView` through here — this is the extension's only placeholder,
/// so that ticket has one edit site. Until then the Ready row reads `looking for requests…`
/// rather than the retired `ready` word, in every document.
let private setCompanionStatus (state: State) = setStatusText (statusText state ScriptPending)

[<Literal>]
let private getSdkLabel = "Get the .NET SDK"

[<Literal>]
let private dotnetDownloadUrl = "https://aka.ms/dotnet/download"

/// The SDK floor to fall back on when the companion's runtimeconfig is unreadable, which means
/// a broken install. It is the major version that the companion's toolchain pins (ADR-0002).
/// The shipped `Companion.runtimeconfig.json` is the primary source. This value guards only a
/// missing or corrupt package.
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
/// it needs an SDK, not only a runtime. The companion rolls forward onto any newer major
/// version, so a match at the floor or above is genuinely runnable.
let private hasSdkAtLeast (requiredMajor: int) (listSdksOutput: string) : bool =
    listSdksOutput.Split('\n')
    |> Array.exists (fun line -> tryParseSdkMajor line |> Option.exists (fun major -> major >= requiredMajor))

let activate (context: ExtensionContext) =
    let item = window.createStatusBarItem (statusBarAlignmentLeft, 100.0)
    // Register the item before the first `setCompanionStatus`, which is a no-op while
    // `statusItem` is None. Otherwise the item shows empty until the companion's first state
    // arrives, and "starting…" — the one status that says activation happened — is never seen.
    // The write itself shows the item, so no separate `show` is needed here.
    statusItem <- Some item
    setCompanionStatus Starting
    context.subscriptions.Add(box item)

    RunCommand.setExtensionUri context.extensionUri

    context.subscriptions.Add(
        box (languages.registerCodeLensProvider (nonNull (box {| language = "fsharp" |}), CodeLensProvider.provider))
    )

    context.subscriptions.Add(box (RunCommand.register ()))
    context.subscriptions.Add(box (RunCommand.registerExplain ()))
    context.subscriptions.Add(box (RunCommand.registerExplainCompanionStopped ()))

    let companionDll =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.dll" |]

    // The SDK floor is the companion's own build target. It comes from the runtimeconfig that
    // ships beside the DLL, which gives one source of truth. A change to the companion's TFM
    // therefore moves both the floor and the guidance.
    let requiredMajor =
        Node.Path.join [| context.extensionPath; "dist"; "companion"; "Companion.runtimeconfig.json" |]
        |> companionTargetMajor
        |> Option.defaultValue fallbackRequiredMajor

    let onState state =
        setCompanionStatus state
        CodeLensProvider.setReady (state = Ready)

    let startCompanion (dotnetPath: string) =
        let handle = Companion.start dotnetPath companionDll onState
        CodeLensProvider.setHandle handle
        RunCommand.setHandle handle
        companionHandle <- Some handle

    // Require an SDK that the user installed, which is the Ionide and C# Dev Kit model. This
    // replaces the earlier runtime-only acquisition, because FSI's `#r "nuget:"` restore drives
    // `dotnet msbuild`, and a runtime does not carry msbuild. Resolve the
    // `fshttpStudio.dotnetPath` override, or else take `"dotnet"` from PATH. Before the spawn,
    // confirm with `--list-sdks` that it carries an SDK at or above the companion's target major.
    let dotnetPathOverride = configuredDotnetPath ()
    let dotnetPath = dotnetPathOverride |> Option.defaultValue "dotnet"

    let requiredSdk = sprintf ".NET %d SDK or newer" requiredMajor

    // First-run guidance when no SDK at or above the floor is reachable. We deliberately own
    // this "SDK not found" path, which is the trade-off that Option B accepts, and we point the
    // user at the download page and the override. When the override is set but did not resolve
    // to an SDK, report that, instead of "none was found".
    let notifyNoSdk () =
        setCompanionStatus SdkNotFound

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

    // Bound the probe. Node kills a stalled `dotnet` when `timeout` expires, and the
    // killed-child error routes to `notifyNoSdk` like any other failure. A hung host therefore
    // cannot stall activation.
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
