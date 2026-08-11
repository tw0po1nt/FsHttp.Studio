module Extension.Tests.ProtocolTests

// Drives the pure logic behind the CodeLens and Run wiring as plain values, with no VSCode, no
// Fable, and no companion process. This mirrors how the renderer core's Seam-B suite isolates
// its own pure dispatch from the browser-only mounting glue.

open Expecto
open Protocol

let private range startLine startCol endLine endCol =
    { StartLine = startLine
      StartCol = startCol
      EndLine = endLine
      EndCol = endCol
      Refusal = None }

[<Tests>]
let scriptFileNameForTests =
    testList
        "scriptFileNameFor"
        [ test "a file-scheme script sends its own absolute path" {
              Expect.equal
                  (scriptFileNameFor "file" "/Users/x/api/probe.fsx")
                  (Some "/Users/x/api/probe.fsx")
                  "FSI resolves __SOURCE_DIRECTORY__ from this path"
          }

          test "an untitled buffer sends nothing, so FSI keeps its own default" {
              Expect.equal (scriptFileNameFor "untitled" "Untitled-1") None "an untitled buffer has no real path"
          }

          test "a scheme with no local path sends nothing rather than an invented directory" {
              // vscode hands these a `fileName` that looks like a path but resolves nowhere on
              // this machine. `isUntitled` is false for all three, so the scheme is the test.
              for scheme, fileName in
                  [ "vscode-vfs", "/repo/probe.fsx"
                    "git", "/Users/x/api/probe.fsx"
                    "vscode-remote", "/home/x/probe.fsx" ] do
                  Expect.equal
                      (scriptFileNameFor scheme fileName)
                      None
                      (sprintf "the %s scheme carries no local path" scheme)
          } ]

[<Tests>]
let toVscodeLineTests =
    testList
        "toVscodeLine"
        [ test "subtracts one, matching ADR-0003's FCS(1-based) -> vscode(0-based) convention" {
              Expect.equal (toVscodeLine 1) 0 "FCS line 1 is vscode line 0"
              Expect.equal (toVscodeLine 5) 4 "FCS line 5 is vscode line 4"
          } ]

[<Tests>]
let formatCompileErrorTests =
    let diag message r = { Message = message; Range = r }

    testList
        "formatCompileError"
        [ test "prefixes the message with its (line,col), shifting the 0-based column to 1-based" {
              let d = diag "The value or constructor 'auth' is not defined." (range 3 8 3 17)

              Expect.equal
                  (formatCompileError [ d ])
                  "Compile error:\n(3,9) The value or constructor 'auth' is not defined."
                  "col 8 (0-based) prints as 9, matching vscode's Ln/Col"
          }

          test "an addendum error anchored at line 1 col 0 prints as (1,1)" {
              let d = diag "The namespace or module 'FsHttp' is not defined." (range 1 0 1 0)

              Expect.equal
                  (formatCompileError [ d ])
                  "Compile error:\n(1,1) The namespace or module 'FsHttp' is not defined."
                  "top-of-script anchor prints as 1-based (1,1)"
          }

          test "lists each diagnostic on its own line under one header" {
              let d1 = diag "First error." (range 2 4 2 8)
              let d2 = diag "Second error." (range 5 0 5 6)

              Expect.equal
                  (formatCompileError [ d1; d2 ])
                  "Compile error:\n(2,5) First error.\n(5,1) Second error."
                  "one header, one line per diagnostic"
          } ]

/// Builds an ok frame with the given request bodyState triple. Shared by the three bodyState
/// cases so each test only names the state under check.
let private okFrameWithRequest (bodyState: string) (bodyBase64: string) (bodyReason: string) =
    OkFrame
        { Status = 200
          Reason = "OK"
          Headers = [ "Content-Type", "application/json" ]
          ContentType = "application/json"
          BodyBase64 = "e30="
          RequestMs = 12.5
          Request =
            Some
                { Method = "POST"
                  Url = "https://api.example.com/items?q=one%20two"
                  Headers = [ "Content-Type", "application/json" ]
                  BodyState = bodyState
                  BodyBase64 = bodyBase64
                  BodyReason = bodyReason } }

[<Tests>]
let parseRunResultTests =
    testList
        "parseRunResult"
        [ test "reads request with bodyState none" {
              match parseRunResult (okFrameWithRequest "none" "" "") with
              | RunOk(request, status, reason, headers, contentType, bodyBase64, requestMs) ->
                  Expect.equal request.Method "POST" "method"
                  Expect.equal request.Url "https://api.example.com/items?q=one%20two" "url keeps percent-escapes"
                  Expect.equal request.Headers [ "Content-Type", "application/json" ] "request headers"
                  Expect.equal request.Body NoBody "none maps to NoBody"
                  Expect.equal status 200 "status"
                  Expect.equal reason "OK" "reason"
                  Expect.equal headers [ "Content-Type", "application/json" ] "response headers"
                  Expect.equal contentType "application/json" "contentType"
                  Expect.equal bodyBase64 "e30=" "response body"
                  Expect.equal requestMs 12.5 "requestMs"
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "reads request with bodyState captured" {
              let bytes = System.Text.Encoding.UTF8.GetBytes("""{"a":1}""")
              let bodyBase64 = System.Convert.ToBase64String bytes

              match parseRunResult (okFrameWithRequest "captured" bodyBase64 "") with
              | RunOk(request, _, _, _, _, _, _) ->
                  match request.Body with
                  | Captured got -> Expect.equal got bytes "captured bytes round-trip from base64"
                  | other -> failtestf "expected Captured, got %A" other
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "reads request with bodyState notCaptured" {
              let reason = "streamed body — not captured, so that the upload is unchanged"

              match parseRunResult (okFrameWithRequest "notCaptured" "" reason) with
              | RunOk(request, _, _, _, _, _, _) ->
                  Expect.equal request.Body (NotCaptured reason) "notCaptured keeps its reason"
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "an ok frame missing request is a RunProtocolError, not a crash" {
              let frame =
                  OkFrame
                      { Status = 200
                        Reason = "OK"
                        Headers = []
                        ContentType = ""
                        BodyBase64 = ""
                        RequestMs = 1.0
                        Request = None }

              match parseRunResult frame with
              | RunProtocolError message ->
                  Expect.stringContains message "request" "the message names the missing object"
              | other -> failtestf "expected RunProtocolError, got %A" other
          }

          test "an unknown bodyState is a RunProtocolError, not a crash" {
              match parseRunResult (okFrameWithRequest "sometime" "" "") with
              | RunProtocolError message -> Expect.stringContains message "bodyState" "the message names the bad field"
              | other -> failtestf "expected RunProtocolError, got %A" other
          } ]
