/// Each F# suite that writes a Golden fixture links this file. The Lua core suite reads the same
/// folder.
module GoldenFixture

open System
open System.IO
open System.Text
open Expecto

let folder = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "golden"))

[<Literal>]
let UpdateFlag = "UPDATE_GOLDEN_FIXTURES"

let private updateRequested () =
    Environment.GetEnvironmentVariable UpdateFlag = "1"

let private firstDifference (expected: byte[]) (actual: byte[]) =
    Seq.zip expected actual
    |> Seq.tryFindIndex (fun (e, a) -> e <> a)
    |> Option.defaultValue (min expected.Length actual.Length)

/// Compares `actual` with the committed Golden fixture at `relativePath` under `folder`, and
/// fails on a difference. With `UPDATE_GOLDEN_FIXTURES=1`, it writes `actual` to the Golden fixture.
let verify (relativePath: string) (actual: byte[]) =
    let path = Path.Combine(folder, relativePath)

    if updateRequested () then
        Directory.CreateDirectory(nonNull (Path.GetDirectoryName path)) |> ignore
        File.WriteAllBytes(path, actual)
    elif not (File.Exists path) then
        failtestf "The Golden fixture %s does not exist. Run the tests with %s=1 to write it." relativePath UpdateFlag
    else
        let expected = File.ReadAllBytes path

        if expected <> actual then
            failtestf
                "The output differs from the Golden fixture %s at byte %d.\nExpected:\n%s\nActual:\n%s\nRun the tests with %s=1 to write the fixture again."
                relativePath
                (firstDifference expected actual)
                (Encoding.UTF8.GetString expected)
                (Encoding.UTF8.GetString actual)
                UpdateFlag
