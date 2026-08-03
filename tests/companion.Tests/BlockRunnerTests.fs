module Companion.Tests.BlockRunnerTests

// Seam A. It drives Run as a black box against a real local server: feed .fsx source and a block
// index, then assert the outcome. This matches the ticket's acceptance criteria directly.
// `BlockLocatorTests` drives location. `RequestHandlerTests` drives the envelope dispatch that
// sits on top of both. This file drives `BlockRunner.run` itself.

open System
open Expecto
open Companion.BlockRunner
open Companion.Tests.TestServer

/// A published FsHttp version. Reflection extraction is stable across versions 13 to 15
/// (ADR-0002). This pin is explicit, and not "latest", so the suite does not drift with a new
/// release.
let private fsHttpRef = "15.0.3"

let private pngMagic =
    [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]

let private pngBytes = Array.append pngMagic [| 1uy; 2uy; 3uy; 4uy |]

/// Asserts that a diagnostic points somewhere the editor can actually highlight. A setup
/// diagnostic may be anchored rather than native, so the line is not fixed, but it must still
/// land inside the script: a phantom line past the end fails to highlight in the UI.
let private expectLineInFile (source: string) (d: Diagnostic) =
    let lineCount = source.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length

    Expect.isTrue
        (d.Range.StartLine >= 1 && d.Range.StartLine <= lineCount)
        (sprintf "range line %d should be inside the script's %d lines" d.Range.StartLine lineCount)

// These tests are sequenced. Every case starts an FSI session that resolves
// `#r "nuget: FsHttp"` into the process-wide package-management cache. In parallel, which is
// Expecto's default, those resolutions race on the same cache files. The race gives "The
// process cannot access the file '…resolvedReferences.paths' because it is being used by
// another process", and a lost race then appears as a false CompileError. A sequence removes
// the race.
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
              // There is no `#r "nuget: FsHttp"` at all. If the companion supplied one itself,
              // `open FsHttp` here would still succeed. It does not succeed, which proves that
              // the pin the "ok" tests use comes from the source text, and not from a fallback
              // that the companion injected.
              let source = "open FsHttp\n\nhttp {\n    GET \"https://example.com\"\n}\n"
              let sourceLineCount = source.Split('\n').Length

              match run source 0 with
              | CompileError diagnostics ->
                  // The failure comes from the companion addendum's own `open FsHttp`, which has
                  // no user-source line. Its range must still anchor inside the real source, and
                  // not on a phantom line past the end (see BlockRunner.setupDiagnostic).
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
              // This source is syntactically valid, so location still finds the block below.
              // But it is ill-typed, so the failure comes from the setup's type-check. A raw
              // parse error would also break locateBlocks' own untyped parse.
              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nlet x : int = \"not an int\"\n\nhttp {\n    GET \"https://example.com\"\n}\n"
                      fsHttpRef

              match run source 0 with
              | CompileError diagnostics -> Expect.isNonEmpty diagnostics "at least one diagnostic expected"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a setup that fails to parse reports the setup's own compileError, not a phantom error naming http" {
              // `EvalInteractionNonThrowing` returns `Choice1Of2` (no exception) for a Setup
              // that fails to *parse*, and discards the failure into the diagnostics array. The
              // companion used to read that array only in the `Choice2Of2` branch, so this
              // Setup was silently treated as good, and the block below went on to report "The
              // value or constructor 'http' is not defined" instead. The Setup here is a `let`
              // followed by two lines that each start with `|>`, wrapped in a function body.
              // The wrapper is what keeps `locateBlocks`' own untyped parse successful: the
              // bare top-level form fails that parse too, so no block is located and the Run
              // never reaches the Setup at all.
              let source =
                  "let f () =\n    let x = 1\n    |> ignore\n    |> ignore\n    x\n\nhttp {\n    GET \"https://example.com\"\n}\n"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  Expect.isTrue
                      (diagnostics
                       |> List.exists (fun d -> d.Range.StartLine = 2 || d.Range.StartLine = 3))
                      "at least one diagnostic should keep its real location on the broken `let`/`|>` lines, not fall back to a phantom line"

                  for d in diagnostics do
                      expectLineInFile source d

                      Expect.stringContains
                          d.Message
                          "Setup failed to evaluate"
                          "the message should say the fault is in the Setup"

                      Expect.isFalse
                          (d.Message.Contains "http")
                          "the message must not name 'http' -- that would be the symptom of the discarded-diagnostics defect, not the true fault"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a block in a for-loop body reports the setup's own parse error, anchored in the file, not http" {
              // The `for ... do` head has no body once the setup slice ends at the block's
              // start, so the Setup fails to parse. Before the fix this fell into the same
              // discarded-diagnostics defect as the test above, and the block reported "The
              // value or constructor 'http' is not defined" instead of the true fault. Every
              // diagnostic here starts at or past the companion's addendum, so this is the case
              // that exercises the line-1 anchor, and with it the reason the message names the
              // Setup at all.
              let source =
                  "for name in [ \"pidgey\"; \"rattata\" ] do\n    http {\n        GET \"https://example.com\"\n    }\n"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  for d in diagnostics do
                      // The companion anchors an addendum-origin diagnostic at line 1 (see
                      // BlockRunner.setupDiagnostic), so this case asserts only that the
                      // location stays inside the file, not a specific line.
                      expectLineInFile source d

                      Expect.stringContains
                          d.Message
                          "Setup failed to evaluate"
                          "an anchored diagnostic must name the Setup, or line 1 reads as a fault on the user's own first line"

                      Expect.isFalse (d.Message.Contains "http") "the message must not name 'http'"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a setup that throws at run time returns runtimeError, not compileError" {
              // The Setup here compiles: it produces zero error diagnostics and fails only when
              // it runs. The diagnostics-first check must therefore fall through to the
              // exception branch. This guards the boundary from the other side: a check that
              // treated any non-success Setup as a compile error would misreport this case, and
              // the two outcomes must stay distinct.
              let source =
                  sprintf
                      "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\nlet x : int = failwith \"setup blew up\"\n\nhttp {\n    GET \"https://example.com\"\n}\n"
                      fsHttpRef

              match run source 0 with
              | RuntimeError message ->
                  Expect.stringContains message "setup blew up" "the exception message should survive"
              | other -> failtestf "expected runtimeError, got %A" other
          }

          test "a block streamed body-after-headers stays readable across repeated Runs" {
              // Regression for "The stream was already consumed. It cannot be read again."
              // FsHttp's default `ResponseHeadersRead` returns before the body, and leaves a
              // read-once content on a pooled keep-alive connection. A later Run that reuses
              // that connection previously threw, because `extractResponse` read the consumed
              // stream. FsHttp shares one static HttpClient across every Run's fresh FSI
              // session, which is what makes the reuse possible. A body that streams a moment
              // after its headers reaches this path, and an immediate body, which every other
              // case here sends, does not. This test runs three times, so it covers the Run
              // that reuses the pooled connection, and not only the first Run.
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
              // The package assemblies that `#r "nuget:"` resolves load into the process-wide
              // default AssemblyLoadContext, and they outlive each per-Run FSI session. A second
              // Run that pinned a *different* version of the same package used to collide there
              // with "Could not load type … from assembly …". The fix keeps the warm in-process
              // fast path, but routes a pin that conflicts with a loaded version to a fresh
              // worker process, whose ALC ends with it. Both Runs must pass, in either order of
              // the two versions.
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
              // A worker whose block hangs emits no frame at all, so the parent's frame read
              // would block forever without a bound. Here a request to a server that never
              // answers causes the hang. `runInWorker` caps the wait, calls Kill() on the child
              // at expiry, and maps the Run to a RuntimeError. This test drives `runInWorker`
              // directly on a short bound, so it reaches the hung path deterministically,
              // instead of through conflict routing.
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
              // Unblock the listener thread, so the server can shut down cleanly.
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

          // This case stays last in the sequenced list on purpose. It loads FsHttp
          // *version-less*, which records a `Versionless` entry in the process-global load map.
          // That entry would route every later explicitly-pinned Run above to a worker. In last
          // position, the entry cannot reach those Runs.
          test "a version-less load then a differently-pinned Run does not collide in-process" {
              // A version-less `#r "nuget: FsHttp"` resolves *some* latest version into the
              // process-wide ALC, but it names no version. If that load recorded nothing, a
              // later Run that pinned a *different* explicit version would see an empty map. It
              // would then run in-process against the poisoned ALC, and hit the original "Could
              // not load type … from assembly …" collision. A sentinel record of the
              // version-less load routes that later pinned Run to a fresh worker. Both Runs
              // must pass.
              use server =
                  new TestServer(Map [ "/png", bytesHandler 200 "image/png" [] pngBytes ])

              let versionlessSource =
                  sprintf "#r \"nuget: FsHttp\"\nopen FsHttp\n\nhttp {\n    GET \"%s/png\"\n}\n" server.BaseUrl

              match run versionlessSource 0 with
              | Ok(status, _, _, _, _) -> Expect.equal status 200 "the version-less Run should load latest and run"
              | other -> failtestf "version-less Run expected ok, got %A" other

              // 13.3.0 is not the latest FsHttp, so this explicit pin differs from the version
              // that the version-less Run loaded. That is the exact conflict that used to pass
              // through to the in-process path.
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

// Pure pin parsing, with no FSI and no server. It produces the input that `run`'s conflict
// routing keys off. It stays outside the sequenced integration list above, so it remains a fast
// unit that does not depend on order.
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
              // For `#r "nuget: FsHttp, 15.0.3, PreRelease"`, the version group must stop at the
              // comma. A capture of "15.0.3," would never equal a loaded "15.0.3", and it would
              // route an identical re-pin to an unnecessary worker.
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

// The pure routing decision that `run` keys off. These tests drive it directly against an
// explicit loaded state, and not against the process-global map. Every quadrant is therefore a
// fast, deterministic unit. This includes the quadrants that the sequenced integration tests
// cannot isolate, because earlier tests fill that map first.
[<Tests>]
let conflictTests =
    testList
        "BlockRunner.pinConflicts"
        [ test "a package not loaded here never conflicts: the first load cannot collide" {
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
              // The `Versionless` load resolved *some* unnamed latest version. We cannot prove
              // that a later explicit pin equals it, so that pin must route to a worker, and
              // must not run in-process against the poisoned ALC.
              Expect.isTrue
                  (pinConflicts (Some Versionless) (Some "13.3.0"))
                  "an explicit pin against a version-less load must route to a worker"
          }

          test "an explicit load then a version-less pin conflicts (the reverse hole)" {
              // This case is symmetric to the one above. A later version-less Run can resolve a
              // different latest version than the version already pinned into the ALC, and we
              // cannot prove otherwise. It must therefore also route to a worker, and must not
              // collide in-process.
              Expect.isTrue
                  (pinConflicts (Some(Pinned "13.3.0")) None)
                  "a version-less pin against an explicitly-loaded version must route to a worker"
          } ]
