module Companion.Tests.BlockRunnerTests

// Seam A (issue #16): drives Run as a black box against a real local server — feed .fsx
// source and a block index, assert the outcome — matching the ticket's acceptance criteria
// directly. `BlockLocatorTests` exercises location; `RequestHandlerTests` exercises the
// envelope dispatch that sits on top of both; this file exercises `BlockRunner.run` itself.

open System
open Expecto
open Companion.BlockRunner
open Companion.Tests.TestServer

/// A currently-published FsHttp version (ADR-0002: reflection extraction is stable across
/// 13-15). Pinned explicitly, and not "latest", so the suite doesn't drift with new releases.
let private fsHttpRef = "15.0.3"

let private pngMagic =
    [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

let private pngBytes = Array.append pngMagic [| 1uy; 2uy; 3uy; 4uy |]

// Sequenced: every case spins up an FSI session that resolves `#r "nuget: FsHttp"` into the
// process-wide package-management cache. Run in parallel (Expecto's default), those
// resolutions race on the same cache files ("The process cannot access the file
// '…resolvedReferences.paths' because it is being used by another process"), and a lost race
// surfaces as a spurious CompileError. Serializing them removes the race.
[<Tests>]
let tests =
    testSequenced
    <| testList
        "BlockRunner"
        [ test "ok carries status, merged headers, contentType, and a byte-intact image body" {
              use server =
                  new TestServer(Map [ "/png", bytesHandler 200 "image/png" [ "X-Custom", "hello" ] pngBytes ])

              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"%s/png\"\n}\n"
                      fsHttpRef
                      server.BaseUrl

              match run source 0 with
              | Ok(status, _reason, headers, contentType, bodyBase64) ->
                  Expect.equal status 200 "status should round-trip"
                  Expect.equal contentType "image/png" "contentType should come from the content header"

                  let headerMap = dict headers

                  Expect.equal headerMap.["X-Custom"] "hello" "a response header should be present"

                  Expect.stringContains
                      (headerMap.["Content-Type"])
                      "image/png"
                      "a content-only header should be merged in too"

                  let bytes = Convert.FromBase64String bodyBase64
                  Expect.equal bytes pngBytes "body bytes should be byte-intact (PNG magic preserved)"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a non-2xx HTTP response is a successful run, not an error" {
              use server = new TestServer(Map [ "/missing", textHandler 404 "not found" ])

              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"%s/missing\"\n}\n"
                      fsHttpRef
                      server.BaseUrl

              match run source 0 with
              | Ok(status, _, _, _, _) -> Expect.equal status 404 "the non-2xx status should come through as ok"
              | other -> failtestf "a non-2xx response should still be ok, got %A" other
          }

          test "the companion has no static FsHttp reference (ADR-0002) — the pin can only come from the user's own #r" {
              let referencedNames =
                  typeof<RunOutcome>.Assembly.GetReferencedAssemblies()
                  |> Array.map (fun a -> a.Name)

              Expect.isFalse
                  (referencedNames |> Array.contains "FsHttp")
                  "the companion project must not reference FsHttp itself, or a host reference would silently override the user's pin"
          }

          test "without the user's own #r, FsHttp resolves to nothing — there is no companion-forced fallback" {
              // No `#r "nuget: FsHttp"` at all: if the companion supplied one itself, `open
              // FsHttp` here would still succeed. It doesn't, proving the pin the "ok" tests
              // rely on came from the source text, not a fallback the companion injected.
              let source = "open FsHttp\n\nhttp {\n    GET \"https://example.com\"\n}\n"
              let sourceLineCount = source.Split('\n').Length

              match run source 0 with
              | CompileError diagnostics ->
                  // The failure comes from the companion addendum's own `open FsHttp`, which has
                  // no user-source line; its range must still be anchored inside the real source
                  // rather than a phantom line past the end (see BlockRunner.setupDiagnostic).
                  for d in diagnostics do
                      Expect.isTrue (d.Range.StartLine >= 1) "range should start at a real line"

                      Expect.isTrue
                          (d.Range.StartLine <= sourceLineCount)
                          "an addendum-origin diagnostic must be clamped inside the user's source, not a phantom line"
              | other ->
                  failtestf "expected compileError (FsHttp is unresolved without the user's own #r), got %A" other
          }

          test "setup isolation: running the second block does not fire the first block's request" {
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nlet a =\n    http {\n        GET \"%s/hit\"\n    }\n    |> Request.send\n    |> ignore\n\nhttp {\n    GET \"%s/hit\"\n}\n"
                      fsHttpRef
                      server.BaseUrl
                      server.BaseUrl

              match run source 1 with
              | Ok _ -> Expect.equal hitCounter.Value 1 "only the target block's own request should have fired"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a non-compiling target block returns compileError with a source range" {
              let source =
                  sprintf "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET undefinedBaseUrl\n}\n" fsHttpRef

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"
                  let d = diagnostics.[0]
                  Expect.isTrue (d.Range.StartLine > 0) "range should point at a real line"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a non-compiling setup returns compileError" {
              // Syntactically valid (so location still finds the block below) but ill-typed,
              // so the failure comes from setup's type-check rather than from a raw parse
              // error that would also break locateBlocks' own untyped parse.
              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nlet x : int = \"not an int\"\n\nhttp {\n    GET \"https://example.com\"\n}\n"
                      fsHttpRef

              match run source 0 with
              | CompileError diagnostics -> Expect.isNonEmpty diagnostics "at least one diagnostic expected"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a block streamed body-after-headers stays readable across repeated Runs" {
              // Regression for "The stream was already consumed. It cannot be read again.":
              // FsHttp's default `ResponseHeadersRead` returns before the body, leaving a
              // read-once content on a pooled keep-alive connection. Reusing that connection
              // on a later Run (FsHttp shares one static HttpClient across every Run's fresh
              // FSI session) previously threw when `extractResponse` read the already-consumed
              // stream. A body that streams a beat after its headers is what exercises the path;
              // an instant body (every other case here) does not. Runs three times to cover the
              // reuse-the-pooled-connection Run, not just the first.
              let body = Array.append pngMagic (Array.replicate 40000 0x5Auy)

              use server =
                  new TestServer(Map [ "/stream.png", streamingBytesHandler "image/png" body ])

              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"%s/stream.png\"\n}\n"
                      fsHttpRef
                      server.BaseUrl

              for i in 1..3 do
                  match run source 0 with
                  | Ok(status, _, _, _, bodyBase64) ->
                      Expect.equal status 200 (sprintf "Run #%d should be a successful 200" i)

                      Expect.equal
                          (Convert.FromBase64String bodyBase64)
                          body
                          (sprintf "Run #%d body should be byte-intact, not a consumed stream" i)
                  | other -> failtestf "Run #%d expected ok, got %A" i other
          }

          test "a network failure returns runtimeError, distinct from compileError" {
              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"http://127.0.0.1:1/nope\"\n}\n"
                      fsHttpRef

              match run source 0 with
              | RuntimeError _ -> ()
              | other -> failtestf "expected runtimeError, got %A" other
          } ]
