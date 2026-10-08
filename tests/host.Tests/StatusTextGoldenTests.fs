module Extension.Tests.StatusTextGoldenTests

open System.IO
open Expecto
open Companion.Envelope
open Protocol

let private states =
    [ "starting", Starting
      "ready", Ready
      "sdkNotFound", SdkNotFound
      "stopped", Stopped ]

// The counts 0, 1, and 2 give the three count rows. The wire never sends -1, but `int` admits it.
let private views =
    [ yield "noFSharpDocument", NoFSharpDocument, None
      yield "notAScript", NotAScript, None
      yield "scriptPending", ScriptPending, None
      for parseFailed in [ false; true ] do
          for blocks in [ 0; 1; 2; -1 ] do
              yield "script", Script(blocks, parseFailed), Some(blocks, parseFailed) ]

let private cases =
    [ for stateName, state in states do
          for viewName, view, script in views -> stateName, state, viewName, view, script ]

let private goldenFixture () =
    encode
        {| cases =
            [ for stateName, state, viewName, view, script in cases ->
                  {| blocks = script |> Option.map fst |> Option.toNullable
                     parseFailed = script |> Option.map snd |> Option.toNullable
                     state = stateName
                     text = statusText state view |> Option.toObj
                     view = viewName |} ] |}

[<Tests>]
let tests =
    testList
        "Status line text Golden fixture"
        [ test "the Golden fixture covers each State with each ScriptView" {
              Expect.hasLength cases (4 * 11) "four States and eleven ScriptViews"
          }

          test "a script with -1 Blocks reads as a script with no Block" {
              for _, state in states do
                  for parseFailed in [ false; true ] do
                      Expect.equal
                          (statusText state (Script(-1, parseFailed)))
                          (statusText state (Script(0, parseFailed)))
                          (sprintf "%A with a Parse failure %b" state parseFailed)
          }

          test "the Status line text matches its Golden fixture" {
              GoldenFixture.verify (Path.Combine("status", "status-text.json")) (goldenFixture ())
          } ]
