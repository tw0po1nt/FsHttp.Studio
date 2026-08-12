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

let private okResponse =
    { Status = 200
      Reason = "OK"
      Headers = [ "Content-Type", "application/json" ]
      ContentType = "application/json"
      BodyBase64 = "e30="
      RequestMs = 12.5 }

/// The wire's three body states, each built by the one field it populates. A test that names a
/// state cannot then set a field the companion never sets for it.
let private noBodyOnWire =
    { State = "none"
      Base64 = ""
      Reason = "" }

let private capturedOnWire base64 : WireBody =
    { State = "captured"
      Base64 = base64
      Reason = "" }

let private notCapturedOnWire reason : WireBody =
    { State = "notCaptured"
      Base64 = ""
      Reason = reason }

/// Builds an ok envelope carrying the given wire body. Shared by the bodyState cases so each test
/// only names the state under check.
let private okEnvelopeWithBody (body: WireBody) =
    OkEnvelope(
        okResponse,
        Some
            { Method = "POST"
              Url = "https://api.example.com/items?q=one%20two"
              Headers = [ "Content-Type", "application/json" ]
              Body = body }
    )

[<Tests>]
let parseRunResultTests =
    testList
        "parseRunResult"
        [ test "reads request with bodyState none" {
              match parseRunResult (okEnvelopeWithBody noBodyOnWire) with
              | RunOk(request, response) ->
                  Expect.equal request.Method "POST" "method"
                  Expect.equal request.Url "https://api.example.com/items?q=one%20two" "url keeps percent-escapes"
                  Expect.equal request.Headers [ "Content-Type", "application/json" ] "request headers"
                  Expect.equal request.Body NoBody "none maps to NoBody"
                  Expect.equal response okResponse "the response half passes through unchanged"
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "reads request with bodyState captured" {
              let bytes = System.Text.Encoding.UTF8.GetBytes("""{"a":1}""")

              match parseRunResult (okEnvelopeWithBody (capturedOnWire (System.Convert.ToBase64String bytes))) with
              | RunOk(request, _) ->
                  match request.Body with
                  | Captured got -> Expect.equal got bytes "captured bytes round-trip from base64"
                  | other -> failtestf "expected Captured, got %A" other
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "reads request with bodyState notCaptured" {
              let reason = "streamed body — not captured, so that the upload is unchanged"

              match parseRunResult (okEnvelopeWithBody (notCapturedOnWire reason)) with
              | RunOk(request, _) -> Expect.equal request.Body (NotCaptured reason) "notCaptured keeps its reason"
              | other -> failtestf "expected RunOk, got %A" other
          }

          test "an ok envelope missing request is a RunProtocolError, not a crash" {
              match parseRunResult (OkEnvelope(okResponse, None)) with
              | RunProtocolError message ->
                  Expect.stringContains message "request" "the message names the missing object"
              | other -> failtestf "expected RunProtocolError, got %A" other
          }

          test "an unknown bodyState is a RunProtocolError, not a crash" {
              let body = { noBodyOnWire with State = "sometime" }

              match parseRunResult (okEnvelopeWithBody body) with
              | RunProtocolError message -> Expect.stringContains message "bodyState" "the message names the bad field"
              | other -> failtestf "expected RunProtocolError, got %A" other
          }

          test "a captured body that is not valid base64 is a RunProtocolError, not a crash" {
              match parseRunResult (okEnvelopeWithBody (capturedOnWire "not base64 at all!")) with
              | RunProtocolError message -> Expect.stringContains message "base64" "the message names the bad encoding"
              | other -> failtestf "expected RunProtocolError, got %A" other
          } ]

[<Tests>]
let requestContentTypeTests =
    testList
        "requestContentType"
        [ test "reads the Content-Type the companion collected" {
              let headers =
                  [ "Accept", "*/*"; "Content-Type", "application/json"; "X-Trace", "abc" ]

              Expect.equal
                  (requestContentType headers)
                  "application/json"
                  "the Content-Type value is what the Request section dispatches the body on"
          }

          test "matches the header name whatever its case" {
              // The name on the wire is whatever the server or FsHttp wrote, so an exact match
              // would silently render a JSON body as plain text.
              Expect.equal (requestContentType [ "content-type", "application/json" ]) "application/json" "lowercase"
              Expect.equal (requestContentType [ "CONTENT-TYPE", "text/plain" ]) "text/plain" "uppercase"
          }

          test "a request with no Content-Type yields the empty string" {
              // A GET, most often. The renderer's dispatch already treats "" as an unknown type.
              Expect.equal (requestContentType [ "Accept", "*/*" ]) "" "no Content-Type header"
              Expect.equal (requestContentType []) "" "no headers at all"
          }

          test "takes the first Content-Type when the request carries more than one" {
              let headers = [ "Content-Type", "application/json"; "Content-Type", "text/plain" ]

              Expect.equal (requestContentType headers) "application/json" "the first wins, and the read is total"
          } ]

/// Decision 5's status-bar table and Decision 6's visibility rule, driven as pure values
/// (docs/spec/0014-explain-missing-lenses.md, Seam 3).
[<Tests>]
let statusTextTests =
    testList
        "statusText"
        [ test "hides the item when there is no F# document, whatever the companion state is" {
              for state in [ Starting; Ready; SdkNotFound; Stopped ] do
                  Expect.equal
                      (statusText state NoFSharpDocument)
                      None
                      (sprintf "%A + NoFSharpDocument hides the item" state)
          }

          test "a companion state other than Ready outranks the script view" {
              // A Script view that would otherwise report a count still reports the companion's
              // own state. Starting, SdkNotFound, and Stopped each win over the count.
              let scriptWithRequests = Script(2, false)

              Expect.equal (statusText Starting scriptWithRequests) (Some "starting…") "Starting outranks"

              Expect.equal
                  (statusText SdkNotFound scriptWithRequests)
                  (Some ".NET SDK not found")
                  "SdkNotFound outranks"

              Expect.equal (statusText Stopped scriptWithRequests) (Some "companion stopped") "Stopped outranks"
          }

          test "maps each Ready row of Decision 5 to its text" {
              Expect.equal (statusText Ready NotAScript) (Some "not an .fsx script") "Ready + not .fsx"

              Expect.equal
                  (statusText Ready ScriptPending)
                  (Some "looking for requests…")
                  "Ready + .fsx, no response yet"

              Expect.equal (statusText Ready (Script(1, false))) (Some "1 request") "Ready + clean + 1"
              Expect.equal (statusText Ready (Script(2, false))) (Some "2 requests") "Ready + clean + N > 1"
              Expect.equal (statusText Ready (Script(0, false))) (Some "no requests found") "Ready + clean + 0"

              Expect.equal
                  (statusText Ready (Script(0, true)))
                  (Some "no requests found — syntax error")
                  "Ready + failed + 0"

              Expect.equal
                  (statusText Ready (Script(3, true)))
                  (Some "3 requests — a syntax error can hide others")
                  "Ready + failed + N >= 1"
          }

          test "one request is singular, and two are plural" {
              Expect.equal (statusText Ready (Script(1, false))) (Some "1 request") "singular"
              Expect.equal (statusText Ready (Script(2, false))) (Some "2 requests") "plural"
          }

          test "a count below zero reads as none found, and never hides the item" {
              // `int` admits a negative the wire never sends. Decision 6 gives `None` one meaning
              // — hide the item — so a malformed count must not blank the status bar.
              Expect.equal (statusText Ready (Script(-1, false))) (Some "no requests found") "clean, below zero"

              Expect.equal
                  (statusText Ready (Script(-1, true)))
                  (Some "no requests found — syntax error")
                  "failed parse, below zero"
          } ]

[<Tests>]
let noRequestsLensTitleTests =
    testList
        "noRequestsLensTitle"
        [ test "returns Some only for Script(0, true)" {
              Expect.equal
                  (noRequestsLensTitle (Script(0, true)))
                  (Some "⊘ No requests found: this script has a syntax error")
                  "zero blocks and a failed parse"
          }

          test "a count below zero reads as zero, as it does in statusText" {
              Expect.equal
                  (noRequestsLensTitle (Script(-1, true)))
                  (Some "⊘ No requests found: this script has a syntax error")
                  "below zero and a failed parse"
          }

          test "returns None for every other ScriptView" {
              Expect.equal (noRequestsLensTitle (Script(0, false))) None "clean empty script"
              Expect.equal (noRequestsLensTitle (Script(2, true))) None "partial loss stays off the lens"
              Expect.equal (noRequestsLensTitle ScriptPending) None "no response yet"
              Expect.equal (noRequestsLensTitle NotAScript) None "not a script"
              Expect.equal (noRequestsLensTitle NoFSharpDocument) None "no F# document"
          } ]

/// The one test of what counts as a Script. The CodeLens provider and the status bar both go
/// through it, so a change here moves both surfaces together.
[<Tests>]
let isScriptFileNameTests =
    testList
        "isScriptFileName"
        [ test "an .fsx path is a Script" {
              Expect.isTrue (isScriptFileName "/w/fixtures/one.fsx") "a script in a folder"
              Expect.isTrue (isScriptFileName "one.fsx") "a bare file name"
          }

          test "the compiled F# surfaces are not Scripts" {
              Expect.isFalse (isScriptFileName "/w/Library.fs") "a module"
              Expect.isFalse (isScriptFileName "/w/Library.fsi") "a signature"
              Expect.isFalse (isScriptFileName "/w/notes.md") "not F# at all"
          }

          test "the extension has to end the name" {
              Expect.isFalse (isScriptFileName "/w/one.fsx.bak") "a backup beside a script"
              Expect.isFalse (isScriptFileName "/w/fsx/Library.fs") "a folder named for the extension"
          } ]

/// Decision 3's compatibility rule for an absent `parseFailed` on the `blocks` envelope. The
/// interop lookup that reads the property stays in `Companion.locate`. This suite drives the
/// pure decide step only (docs/spec/0014-explain-missing-lenses.md, Seam 3, item 12).
[<Tests>]
let parseFailedFromWireTests =
    testList
        "parseFailedFromWire"
        [ test "an absent value decodes to false" {
              Expect.isFalse (parseFailedFromWire None) "an old companion that omits the property"
          }

          test "a present false decodes to false" { Expect.isFalse (parseFailedFromWire (Some false)) "a clean source" }

          test "a present true decodes to true" { Expect.isTrue (parseFailedFromWire (Some true)) "a broken source" } ]

/// Decision 5's guard on which `locate` response reaches the status bar. It lives here because the
/// UI suite cannot drive it: a second visible script does not locate again on demand, so a check
/// that opened one held whether the guard was there or not.
[<Tests>]
let mirrorsActiveDocumentTests =
    testList
        "mirrorsActiveDocument"
        [ test "a response for the active document is mirrored" {
              Expect.isTrue (mirrorsActiveDocument (Some "/w/one.fsx") "/w/one.fsx") "the same document"
          }

          test "a response for another document is dropped" {
              Expect.isFalse
                  (mirrorsActiveDocument (Some "/w/one.fsx") "/w/many.fsx")
                  "a second visible script's own response"

              Expect.isFalse
                  (mirrorsActiveDocument (Some "/w/a/one.fsx") "/w/b/one.fsx")
                  "the same file name in two folders is two documents"
          }

          test "no active text editor drops every response" {
              Expect.isFalse (mirrorsActiveDocument None "/w/one.fsx") "the viewer holds focus"
          } ]
