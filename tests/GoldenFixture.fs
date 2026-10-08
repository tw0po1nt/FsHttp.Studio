/// The Lua core suite reads each Golden fixture that these F# suites write.
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
                "The output differs from the Golden fixture %s at byte %d.\nExpected:\n%s\nActual:\n%s\nRun the tests with %s=1 to write the Golden fixtures again."
                relativePath
                (firstDifference expected actual)
                (Encoding.UTF8.GetString expected)
                (Encoding.UTF8.GetString actual)
                UpdateFlag
