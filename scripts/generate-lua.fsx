#load "../src/host/Refusals.fs"

open System
open System.IO
open System.Text
open System.Text.Json

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let luaDir = Path.Combine(root, "lua", "fshttp")

let quote (text: string) =
    let sb = StringBuilder("\"")

    for c in text do
        match c with
        | '\\' -> sb.Append "\\\\" |> ignore
        | '"' -> sb.Append "\\\"" |> ignore
        | '\n' -> sb.Append "\\n" |> ignore
        | '\r' -> sb.Append "\\r" |> ignore
        | _ -> sb.Append c |> ignore

    sb.Append('"').ToString()

let header (source: string) =
    sprintf "-- Generated from %s by scripts/generate-lua.fsx. Do not edit by hand.\n" source

let versionLua () =
    use doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "package.json")))
    let version = doc.RootElement.GetProperty("version").GetString()
    header "package.json" + sprintf "return %s\n" (quote version)

let refusal (r: Refusals.Refusal) =
    sprintf "{ title = %s, detail = %s }" (quote r.Title) (quote r.Detail)

let refusalsLua () =
    let sb = StringBuilder(header "src/host/Refusals.fs")
    let line (text: string) = sb.Append(text).Append('\n') |> ignore

    line "return {"
    line "    codes = {"

    for code in Refusals.catalogCodes do
        let r = Refusals.forCode code

        line (
            sprintf
                "        %s = { block_mark_title = %s, title = %s, detail = %s },"
                code
                (quote (Refusals.lensTitle code))
                (quote r.Title)
                (quote r.Detail)
        )

    line "    },"
    line (sprintf "    fallback_code = %s," (quote Refusals.fallbackCode))
    line (sprintf "    run_block_mark_title = %s," (quote Refusals.runLensTitle))
    line (sprintf "    stale_block_index = %s," (refusal (Refusals.staleBlockIndex Refusals.Neovim)))
    line (sprintf "    unbound_block_value = %s," (refusal (Refusals.unboundBlockValue "{name}")))

    line (sprintf "    companion_stopped = %s," (refusal Refusals.companionStopped))
    line (sprintf "    companion_stopped_block_mark_title = %s," (quote Refusals.companionStoppedLensTitle))
    line (sprintf "    no_blocks_parse_failure = %s," (quote Refusals.noBlocksParseFailure))
    line (sprintf "    no_blocks_parse_failure_block_mark_title = %s," (quote Refusals.noBlocksParseFailureLensTitle))
    line (sprintf "    no_blocks_empty = %s," (quote Refusals.noBlocksEmpty))
    line "}"
    sb.ToString()

let outputs =
    [ Path.Combine(luaDir, "version.lua"), versionLua ()
      Path.Combine(luaDir, "refusals.lua"), refusalsLua () ]

let check = fsi.CommandLineArgs |> Array.contains "--check"

if check then
    let stale =
        outputs
        |> List.filter (fun (path, text) -> not (File.Exists path) || File.ReadAllText path <> text)

    for (path, _) in stale do
        eprintfn "%s differs from the output of scripts/generate-lua.fsx" (Path.GetRelativePath(root, path))

    if not stale.IsEmpty then
        exit 1
else
    Directory.CreateDirectory luaDir |> ignore

    for (path, text) in outputs do
        File.WriteAllText(path, text)
