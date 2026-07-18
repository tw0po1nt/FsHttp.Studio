module Companion.Tests.BlockRunnerTests

// Seam A: drives Run as a black box against a real local server — feed .fsx
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

          test "two Runs in one process with different FsHttp pins both succeed (ALC isolation)" {
              // `#r "nuget:"`-resolved package assemblies load into the process-wide default
              // AssemblyLoadContext and outlive each per-Run FSI session. A second Run pinning a
              // *different* version of the same package used to collide there ("Could not load
              // type … from assembly …"). Option 1's fix keeps the warm in-process fast path but
              // routes a pin that conflicts with an already-loaded version to a fresh worker
              // process, whose ALC dies with it. Both Runs must come back green regardless of the
              // order the two versions are exercised in.
              use server =
                  new TestServer(Map [ "/png", bytesHandler 200 "image/png" [] pngBytes ])

              let sourceFor (version: string) =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"%s/png\"\n}\n"
                      version
                      server.BaseUrl

              match run (sourceFor "15.0.3") 0 with
              | Ok(status, _, _, _, _) -> Expect.equal status 200 "first pin (15.0.3) should run"
              | other -> failtestf "first pin expected ok, got %A" other

              match run (sourceFor "13.3.0") 0 with
              | Ok(status, _, _, _, bodyBase64) ->
                  Expect.equal status 200 "second, differently-pinned Run (13.3.0) should also run"

                  Expect.equal
                      (Convert.FromBase64String bodyBase64)
                      pngBytes
                      "the conflict-routed Run's body should be byte-intact"
              | other -> failtestf "second pin expected ok (no ALC collision), got %A" other
          }

          test "a worker that never produces a frame is bounded to a runtimeError, not a wedge" {
              // A worker whose block hangs — here a request to a server that never answers —
              // emits no frame at all, so the parent's frame read would block forever without a
              // bound. `runInWorker` caps the wait and Kill()s the child on expiry, mapping the
              // Run to a RuntimeError. Driven directly against `runInWorker` on a short bound so
              // the hung path is exercised deterministically rather than via conflict routing.
              use release = new System.Threading.ManualResetEventSlim(false)
              use server = new TestServer(Map [ "/hang", hangingHandler release ])

              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"%s/hang\"\n}\n"
                      fsHttpRef
                      server.BaseUrl

              let sw = Diagnostics.Stopwatch.StartNew()
              let outcome = runInWorker 5000 source 0
              sw.Stop()
              // Unblock the listener thread so the server can shut down cleanly.
              release.Set()

              match outcome with
              | RuntimeError _ -> ()
              | other -> failtestf "expected a bounded runtimeError from the hung worker, got %A" other

              Expect.isLessThan
                  sw.Elapsed.TotalMilliseconds
                  60000.0
                  "the hung worker must return on a bound, not wedge the Run indefinitely"
          }

          test "a network failure returns runtimeError, distinct from compileError" {
              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nhttp {\n    GET \"http://127.0.0.1:1/nope\"\n}\n"
                      fsHttpRef

              match run source 0 with
              | RuntimeError _ -> ()
              | other -> failtestf "expected runtimeError, got %A" other
          }

          // Kept last in this sequenced list on purpose: it loads FsHttp *version-less*, recording a
          // `Versionless` entry in the process-global load map that would route every later
          // explicitly-pinned Run above to a worker. Run last, that pollution can't reach them.
          test "a version-less load then a differently-pinned Run does not collide in-process" {
              // A version-less `#r "nuget: FsHttp"` resolves *some* latest into the process-wide
              // ALC but names no version. If that load recorded nothing, a later Run pinning a
              // *different* explicit version would see an empty map, run in-process against the
              // already-poisoned ALC, and hit the original "Could not load type … from assembly …"
              // collision. Recording the version-less load as a sentinel routes that later pinned
              // Run to a fresh worker instead. Both Runs must come back green.
              use server =
                  new TestServer(Map [ "/png", bytesHandler 200 "image/png" [] pngBytes ])

              let versionlessSource =
                  sprintf "#r \"nuget: FsHttp\"\nopen FsHttp\n\nhttp {\n    GET \"%s/png\"\n}\n" server.BaseUrl

              match run versionlessSource 0 with
              | Ok(status, _, _, _, _) -> Expect.equal status 200 "the version-less Run should load latest and run"
              | other -> failtestf "version-less Run expected ok, got %A" other

              // 13.3.0 is not the latest FsHttp, so this explicit pin differs from whatever the
              // version-less Run loaded — the exact conflict that used to slip through in-process.
              let pinnedSource =
                  sprintf "#r \"nuget: FsHttp, 13.3.0\"\nopen FsHttp\n\nhttp {\n    GET \"%s/png\"\n}\n" server.BaseUrl

              match run pinnedSource 0 with
              | Ok(status, _, _, _, bodyBase64) ->
                  Expect.equal status 200 "the later differently-pinned Run should run without an ALC collision"

                  Expect.equal
                      (Convert.FromBase64String bodyBase64)
                      pngBytes
                      "the conflict-routed Run's body should be byte-intact"
              | other ->
                  failtestf "differently-pinned Run after a version-less load expected ok (no collision), got %A" other
          } ]

// Pure pin parsing (no FSI, no server) — the input `run`'s conflict routing keys off. Kept out of
// the sequenced integration list above so it stays a fast, order-independent unit.
[<Tests>]
let pinTests =
    testList
        "BlockRunner.extractPins"
        [ test "an explicit pin parses to (package, Some version)" {
              Expect.equal
                  (extractPins "#r \"nuget: FsHttp, 15.0.3\"")
                  [ "FsHttp", Some "15.0.3" ]
                  "a single explicit pin should carry its version"
          }

          test "a version-less pin carries no version" {
              Expect.equal
                  (extractPins "#r \"nuget: FsHttp\"")
                  [ "FsHttp", None ]
                  "a version-less #r should parse to (package, None)"
          }

          test "a trailing option after the version is not folded into the version" {
              // `#r "nuget: FsHttp, 15.0.3, PreRelease"`: the version group must stop at the comma,
              // not capture "15.0.3," — which would never equal a loaded "15.0.3" and would route
              // an identical re-pin to a needless worker.
              Expect.equal
                  (extractPins "#r \"nuget: FsHttp, 15.0.3, PreRelease\"")
                  [ "FsHttp", Some "15.0.3" ]
                  "the version must be '15.0.3', with the trailing option and its comma excluded"
          }

          test "multiple pins parse in source order" {
              Expect.equal
                  (extractPins "#r \"nuget: FsHttp, 15.0.3\"\n#r \"nuget: Newtonsoft.Json, 13.0.3\"")
                  [ "FsHttp", Some "15.0.3"; "Newtonsoft.Json", Some "13.0.3" ]
                  "each pin should appear once, in source order"
          } ]

// The pure routing decision `run` keys off, exercised directly against explicit loaded-state rather
// than the process-global map — so every quadrant (including the ones the sequenced integration
// tests can't isolate, because earlier tests pre-populate that map) is a fast, deterministic unit.
[<Tests>]
let conflictTests =
    testList
        "BlockRunner.pinConflicts"
        [ test "a package not loaded here never conflicts — the first load can't collide" {
              Expect.isFalse (pinConflicts None (Some "15.0.3")) "an explicit pin against nothing loaded is fine"
              Expect.isFalse (pinConflicts None None) "a version-less pin against nothing loaded is fine"
          }

          test "two explicit pins conflict exactly when they name different versions" {
              Expect.isFalse
                  (pinConflicts (Some(Pinned "15.0.3")) (Some "15.0.3"))
                  "the same explicit version re-pinned stays in-process"

              Expect.isTrue
                  (pinConflicts (Some(Pinned "15.0.3")) (Some "13.3.0"))
                  "a different explicit version is the original collision — route to a worker"
          }

          test "version-less then version-less stays in-process (same latest resolves)" {
              Expect.isFalse
                  (pinConflicts (Some Versionless) None)
                  "two version-less Runs resolve the same latest, so no new version is loaded"
          }

          test "a version-less load then an explicit pin conflicts (the reported hole)" {
              // The `Versionless` load resolved *some* unnamed latest; a later explicit pin can't be
              // proven equal to it, so it must be routed to a worker rather than run in-process
              // against the already-poisoned ALC.
              Expect.isTrue
                  (pinConflicts (Some Versionless) (Some "13.3.0"))
                  "an explicit pin against a version-less load must route to a worker"
          }

          test "an explicit load then a version-less pin conflicts (the reverse hole)" {
              // Symmetric to the above: a later version-less Run may resolve a different latest than
              // the version already pinned into the ALC, and we can't prove it won't — so it too must
              // route to a worker rather than collide in-process.
              Expect.isTrue
                  (pinConflicts (Some(Pinned "13.3.0")) None)
                  "a version-less pin against an explicitly-loaded version must route to a worker"
          } ]
