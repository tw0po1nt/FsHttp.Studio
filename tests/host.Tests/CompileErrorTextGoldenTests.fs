module Extension.Tests.CompileErrorTextGoldenTests

open System.IO
open Expecto
open Companion.Envelope
open Protocol

let private diagnostic (loadedFile: string option) startLine startCol message =
    { Message = message
      Range =
        { StartLine = startLine
          StartCol = startCol
          EndLine = startLine
          EndCol = startCol + 1
          Refusal = None }
      LoadedFile = loadedFile }

let private script = diagnostic None
let private loaded path = diagnostic (Some path)

let private cases =
    [ Some "/scripts/api/probe.fsx", [ script 3 8 "In the Script." ]
      Some "/scripts/api/probe.fsx", [ script 1 0 "Anchored at the top of the Script." ]
      Some "/scripts/api/probe.fsx",
      [ loaded "/scripts/api/lib/helpers.fsx" 3 8 "In a folder of the Script directory."
        loaded "/scripts/shared/inner.fsx" 1 0 "Outside the Script directory."
        script 4 0 "In the Script." ]
      Some "/scripts/api/probe.fsx", [ loaded "/scripts/api/helpers.fsx" 2 4 "In the Script directory." ]
      Some "/scripts/api/v2/probe.fsx", [ loaded "/scripts/lib.fsx" 5 0 "Two folders up." ]
      None, [ loaded "/scripts/lib/helpers.fsx" 3 8 "The Script has no file name." ]
      Some @"C:\scripts\probe.fsx", [ loaded @"C:\scripts\lib\helpers.fsx" 3 8 "A Windows path." ]
      Some @"C:\scripts\probe.fsx", [ loaded @"D:\lib\helpers.fsx" 3 8 "On another drive." ] ]

let private goldenFixture () =
    encode
        {| cases =
            [ for scriptFileName, diagnostics in cases ->
                  {| diagnostics =
                      [ for d in diagnostics ->
                            {| loadedFile = Option.toObj d.LoadedFile
                               message = d.Message
                               startCol = d.Range.StartCol
                               startLine = d.Range.StartLine |} ]
                     scriptFileName = Option.toObj scriptFileName
                     text = formatCompileError scriptFileName diagnostics |} ] |}

[<Tests>]
let tests =
    testList
        "Compile error text Golden fixture"
        [ test "the Compile error text matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("compile-error", "compile-error-text.json")) (goldenFixture ())
          } ]
