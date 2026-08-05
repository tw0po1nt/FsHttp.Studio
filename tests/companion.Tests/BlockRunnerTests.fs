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

/// A script with the preamble every case here needs: the suite's own pinned `#r`, and the `open`
/// that puts `http` in scope. `body` is the part a case actually varies. One helper keeps the pin
/// identical across cases, and a case that pins a *different* version writes its own source, so
/// that the conflict it drives stays visible in the test itself.
let private script (body: string) =
    sprintf "#r \"nuget: FsHttp, %s\"\nopen FsHttp\n\n%s" fsHttpRef body

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

              let source = script (sprintf "http {\n    GET \"%s/png\"\n}\n" server.BaseUrl)

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

              let source = script (sprintf "http {\n    GET \"%s/missing\"\n}\n" server.BaseUrl)

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
                  script (
                      sprintf
                          "let a =\n    http {\n        GET \"%s/hit\"\n    }\n    |> Request.send\n    |> ignore\n\nhttp {\n    GET \"%s/hit\"\n}\n"
                          server.BaseUrl
                          server.BaseUrl
                  )

              match run source 1 with
              | Ok _ -> Expect.equal hitCounter.Value 1 "only the target block's own request should have fired"
              | other -> failtestf "expected ok, got %A" other
          }

          // The Run reaches the block where the user wrote it (docs/spec/0002), instead of
          // extracting it. Each of these shapes sends exactly one request, which is the
          // isolation criterion that Decision 4 protects.
          for name, sourceFor in
              [ "bare, top-level block", fun (url: string) -> sprintf "http {\n    GET \"%s\"\n}\n" url
                "block in a nested module",
                fun url ->
                    sprintf
                        "module Outer =\n    module Inner =\n        http {\n            GET \"%s\"\n        }\n"
                        url
                "let-bound block", fun url -> sprintf "let pikachu =\n    http {\n        GET \"%s\"\n    }\n" url
                "()-callable function's block",
                fun url -> sprintf "let getSnorlax () =\n    http {\n        GET \"%s\"\n    }\n" url ] do
              test (sprintf "%s: reaches the block and sends exactly one request" name) {
                  let hitCounter = ref 0
                  use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

                  let source = script (sourceFor (server.BaseUrl + "/hit"))

                  match run source 0 with
                  | Ok(status, _, _, _, _) ->
                      Expect.equal status 200 (sprintf "%s should run to a successful response" name)
                      Expect.equal hitCounter.Value 1 (sprintf "%s should send exactly one request" name)
                  | other -> failtestf "%s: expected ok, got %A" name other
              }

          test "a backtick-quoted binding name keeps its backticks in the invocation (R2 route)" {
              // `Ident.idText` carries a name's text and never its backticks, so this binding's
              // derived name arrived as the bare `get pikachu`. Qualified and piped, that reads
              // as an application of `get` to `pikachu`, and a block the user wrote perfectly
              // well failed to compile. `BlockLocator` spells the name back for an invocation,
              // and `qualifyInvocation` splits on the `" ()"` arity suffix rather than on the
              // first space, so a name that holds a space stays one term.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf
                          "module Outer =\n    let ``get pikachu`` =\n        http {\n            GET \"%s/hit\"\n        }\n"
                          server.BaseUrl
                  )

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "a backtick-quoted binding should reach its block"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a private binding runs: `private` is blanked on the target's own path (Decision 6)" {
              // Each invocation is a separate FSI interaction, so an un-blanked `private` binding
              // would fail with "not accessible from this code location". `PrivateSpans` carries
              // the keyword's own span; this proves the Run actually blanks it.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (sprintf "let private secret =\n    http {\n        GET \"%s/hit\"\n    }\n" server.BaseUrl)

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "a private binding should still run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a block in a `module private` runs: the module's own `private` is blanked too (Decision 6)" {
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (sprintf "module private Vault =\n    http {\n        GET \"%s/hit\"\n    }\n" server.BaseUrl)

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "a block in a private module should still run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "an internal binding runs with no treatment (Decision 6's measurement)" {
              // `internal` needs no blanking: an `internal` binding is accessible from a later
              // FSI interaction. This test records that measurement -- it must pass with
              // `PrivateSpans` staying empty for this shape (already asserted in
              // PositionMatrixTests), and not because of any blanking here.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf "let internal semiSecret =\n    http {\n        GET \"%s/hit\"\n    }\n" server.BaseUrl
                  )

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "an internal binding should run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "an attributed binding runs: the keyword search does not stop at the attribute line" {
              // `[<Obsolete>]` puts the declaration's own range on the attribute line, above the
              // `let`. A search that started from that range's first line would find neither the
              // `private` keyword nor the type annotation, and would blank neither. So the binding
              // carries both: an un-blanked `private` fails with "not accessible from this code
              // location", and an un-blanked `Response` contradicts the truncated value. Either
              // failure means the search anchored on the attribute line instead of the `let`.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf
                          "[<System.Obsolete(\"prototype\")>]\nlet private attributed : Response =\n    http {\n        GET \"%s/hit\"\n    }\n    |> Request.send\n"
                          server.BaseUrl
                  )

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "an attributed binding should run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a type annotation the truncation contradicts does not stop the Run (Decision 7)" {
              // The Setup boundary (Decision 1) drops the trailing `|> Request.send`, so the
              // annotated binding keeps a `Response` annotation describing the truncated `http { }`
              // value, not the untruncated pipe. Left un-blanked, the Setup would report a
              // false compile error against a binding the user wrote correctly.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf
                          "let x : Response =\n    http {\n        GET \"%s/hit\"\n    }\n    |> Request.send\n"
                          server.BaseUrl
                  )

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "the annotated binding should still run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request, not two"
              | other -> failtestf "expected ok, got %A" other
          }

          test "the clicked block survives a sibling whose blank span contains it (Hazard 1)" {
              // extra.fsx case 24. The inner block's own `Blank` span is `nested`'s whole
              // declaration, which *contains* the outer block being run. Blanking that span as a
              // sibling would delete the very block the user clicked. `containsBlock` must skip
              // it, not just filter by block identity.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/outer", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf
                          "let nested =\n    http {\n        GET \"%s/outer\"\n        header \"X-Inner\" (string (sprintf \"%%A\" (http { GET \"https://example.com/inner\" })).Length)\n    }\n"
                          server.BaseUrl
                  )

              match run source 0 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "the outer, targeted block must survive the containing sibling span"
                  Expect.equal hitCounter.Value 1 "only the target's own request should fire"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a class-member block does not break a different block's Run that names the type (Hazard 2)" {
              // Decision 5's second hazard: the blank span for a block inside a class member must
              // stop at the member's own right side, which is where `BlockLocator` already narrows
              // it, not the whole type definition. This proves the Run survives it as a sibling: the
              // block below both the type and the member still runs, with the type's own name
              // still resolvable in between.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/below", countingHandler hitCounter ])

              let source =
                  script (
                      sprintf
                          "type Api() =\n    member _.Get() =\n        http {\n            GET \"https://example.com/member\"\n        }\n\nlet _usesApi : Api = Api()\n\nhttp {\n    GET \"%s/below\"\n}\n"
                          server.BaseUrl
                  )

              match run source 1 with
              | Ok(status, _, _, _, _) ->
                  Expect.equal status 200 "the block below the class and its member should still run"
                  Expect.equal hitCounter.Value 1 "it should send exactly one request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a block piped to Request.send in the script still sends exactly one request" {
              // Matrix case 12. The Setup boundary (Decision 1) truncates at the block's own
              // end, which drops the script's own trailing `|> Request.send` -- the invocation
              // interaction supplies its own. Two sends here would mean the boundary let the
              // user's own pipe through as well as the companion's.
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  script (sprintf "http {\n    GET \"%s/hit\"\n}\n|> Request.send\n" server.BaseUrl)

              match run source 0 with
              | Ok _ -> Expect.equal hitCounter.Value 1 "the script's own trailing pipe must not send a second request"
              | other -> failtestf "expected ok, got %A" other
          }

          test "the code after the target block does not run" {
              // The Setup boundary (Decision 1) stops at the end of the target block's own
              // expression. A second declaration below the block, in the same module, is
              // outside that boundary entirely -- not blanked, just never evaluated.
              let firstHits = ref 0
              let secondHits = ref 0

              use server =
                  new TestServer(Map [ "/first", countingHandler firstHits; "/second", countingHandler secondHits ])

              let source =
                  script (
                      sprintf
                          "module M =\n    http {\n        GET \"%s/first\"\n    }\n\n    System.Net.Http.HttpClient().GetAsync(\"%s/second\").Result |> ignore\n"
                          server.BaseUrl
                          server.BaseUrl
                  )

              match run source 0 with
              | Ok _ ->
                  Expect.equal firstHits.Value 1 "the target block should have sent its own request"
                  Expect.equal secondHits.Value 0 "the code after the target block must not run"
              | other -> failtestf "expected ok, got %A" other
          }

          test "a diagnostic on the block's first line reports the true column on the R1 route" {
              // R1 inserts `let ``__fsHttpStudio_target`` = ` at the block's own start, on the
              // block's own line (Decision 2), which shifts every later column on that one line
              // forward. A diagnostic there must report the column the user actually wrote,
              // with the shift subtracted back out (Decision 9), not the raw FSI column.
              let source = script "http { GET undefinedBaseUrl }\n"

              let blockLine = "http { GET undefinedBaseUrl }"
              let expectedCol = blockLine.IndexOf "undefinedBaseUrl"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  let d =
                      diagnostics
                      |> List.tryFind (fun d -> d.Message.Contains "undefinedBaseUrl")
                      |> Option.defaultValue diagnostics.[0]

                  Expect.equal d.Range.StartLine 4 "the diagnostic should land on the block's own line"

                  Expect.equal
                      d.Range.StartCol
                      expectedCol
                      "the reported column should be the true source column, with the R1 insertion's shift subtracted"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a diagnostic on a later line of the block keeps its column unchanged on the R1 route" {
              // The R1 insertion touches only the block's own start line. A diagnostic on a
              // later line of the same block needs no shift at all.
              let source =
                  script "http {\n    GET \"https://example.com\"\n    header undefinedHeaderName \"x\"\n}\n"

              let headerLine = "    header undefinedHeaderName \"x\""
              let expectedCol = headerLine.IndexOf "undefinedHeaderName"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  let d =
                      diagnostics
                      |> List.tryFind (fun d -> d.Message.Contains "undefinedHeaderName")
                      |> Option.defaultValue diagnostics.[0]

                  Expect.equal d.Range.StartLine 6 "the diagnostic should land on the header line"
                  Expect.equal d.Range.StartCol expectedCol "a later line's column must pass through unshifted"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a let-bound block's streamed body stays readable across repeated Runs (R2 route)" {
              // The R1 variant of this regression already exists below. R2's own Setup text is
              // a plain `let`, which is exactly the shape Decision 10 calls out: the Setup
              // builds the block's context as part of evaluating that `let`, before the
              // companion addendum's `GlobalConfig.set` runs, so the response-reading guard must
              // be re-applied at invocation time or the same "stream was already consumed"
              // regression returns.
              let body = Array.append pngMagic (Array.replicate 40000 0x5Auy)

              use server =
                  new TestServer(Map [ "/stream.png", streamingBytesHandler "image/png" body ])

              let source =
                  script (sprintf "let a =\n    http {\n        GET \"%s/stream.png\"\n    }\n" server.BaseUrl)

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

          test "a non-compiling target block returns compileError with a source range" {
              let source = script "http {\n    GET undefinedBaseUrl\n}\n"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"
                  let d = diagnostics.[0]
                  Expect.isTrue (d.Range.StartLine > 0) "range should point at a real line"

                  Expect.isFalse
                      (d.Message.Contains "Setup failed to evaluate")
                      "a block diagnostic must keep the compiler's own text, not the Setup introduction (Decision 8)"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a diagnostic inside the R1 inserted text takes the Setup treatment, not the block's" {
              // R1 inserts `let ``__fsHttpStudio_target`` = ` at the block's own start column,
              // and that column is also where `unshiftPos` clamps a position landing inside the
              // inserted text. Such a position therefore passes the block test, and the
              // compiler's bare text would reach the user as though they had written it.
              // ADR-0007 records this exact collision: a user binding of the reserved name in
              // the same scope, which reports a duplicate definition on the inserted name.
              // The fault is in the companion's own generated text, so Decision 8 gives it the
              // Setup treatment.
              let source =
                  script
                      "module M =\n    let ``__fsHttpStudio_target`` = 1\n\n    http {\n        GET \"https://example.com\"\n    }\n"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  Expect.isTrue
                      (diagnostics
                       |> List.forall (fun d -> d.Message.Contains "Setup failed to evaluate"))
                      "a fault in the companion's own inserted text must carry the Setup introduction, not read as the user's"

                  for d in diagnostics do
                      expectLineInFile source d
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a non-compiling setup returns compileError" {
              // This source is syntactically valid, so location still finds the block below.
              // But it is ill-typed, so the failure comes from the setup's type-check. A raw
              // parse error would also break locateBlocks' own untyped parse.
              let source =
                  script "let x : int = \"not an int\"\n\nhttp {\n    GET \"https://example.com\"\n}\n"

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
              //
              // The target block's own text is now part of the same Setup interaction (Decision
              // 1), and the parse recovery from the broken `let f` cascades far enough that the
              // compiler also reports "'http' is not defined" at the block's own position. That
              // one diagnostic is a genuine block diagnostic under Decision 8's split rule (it
              // starts inside the block's span), so this test scopes its assertions to the
              // diagnostics that carry the Setup introduction, and does not require every
              // diagnostic to.
              let source =
                  "let f () =\n    let x = 1\n    |> ignore\n    |> ignore\n    x\n\nhttp {\n    GET \"https://example.com\"\n}\n"

              match run source 0 with
              | CompileError diagnostics ->
                  Expect.isNonEmpty diagnostics "at least one diagnostic expected"

                  let setupDiagnostics =
                      diagnostics
                      |> List.filter (fun d -> d.Message.Contains "Setup failed to evaluate")

                  Expect.isNonEmpty
                      setupDiagnostics
                      "the setup's own failure must be reported, not swallowed by the cascading block diagnostic"

                  Expect.isTrue
                      (setupDiagnostics
                       |> List.exists (fun d -> d.Range.StartLine = 2 || d.Range.StartLine = 3))
                      "at least one Setup diagnostic should keep its real location on the broken `let`/`|>` lines, not fall back to a phantom line"

                  for d in setupDiagnostics do
                      expectLineInFile source d

                      Expect.isFalse
                          (d.Message.Contains "http")
                          "a Setup diagnostic's message must not name 'http' -- that would be the symptom of the discarded-diagnostics defect, not the true fault"
              | other -> failtestf "expected compileError, got %A" other
          }

          test "a block in a for-loop body is refused with its code, and sends nothing" {
              // A loop body is decided at run time (Decision 2's F1 family), so `classify`
              // refuses it before `run` reserves any pin or evaluates anything -- not the
              // Setup, and not a request to the server (docs/spec/0003, Decision 5).
              let hitCounter = ref 0
              use server = new TestServer(Map [ "/hit", countingHandler hitCounter ])

              let source =
                  sprintf
                      "for name in [ \"pidgey\"; \"rattata\" ] do\n    http {\n        GET \"%s/hit\"\n    }\n"
                      server.BaseUrl

              match run source 0 with
              | Refused(code, name) ->
                  Expect.equal code "loopBody" "the loop body's own wire code should come back"
                  Expect.equal name None "loopBody carries no name"
                  Expect.equal hitCounter.Value 0 "a refused position must not send a request"
              | other -> failtestf "expected Refused for a refused position, got %A" other
          }

          test "a refused Run leaves its script's #r pin unmarked in loadedVersions" {
              // routeAndReserve marks each Run's pins in loadedVersions up front, before any
              // evaluation, so a refusal that reached it would over-mark a pin no session ever
              // loaded (docs/spec/0003, Decision 5). A fictitious package name keeps this test
              // isolated from every other case's real FsHttp pin.
              let package = "FsHttp.Studio.Tests.RefusalGateFixture"

              let source =
                  sprintf
                      "#r \"nuget: %s, 1.2.3\"\nfor name in [ \"pidgey\" ] do\n    http {\n        GET \"http://example.invalid\"\n    }\n"
                      package

              match run source 0 with
              | Refused(code, _) -> Expect.equal code "loopBody" "the loop body should still be the refusal"
              | other -> failtestf "expected Refused, got %A" other

              // An unmarked pin is also the proof that no worker process started. routeAndReserve
              // is the only chooser of the worker route, and it marks every pin it routes, so a
              // pin it never marked is a Run it never routed.
              Expect.equal (loadedVersionOf package) None "a refused Run must never reserve a pin in loadedVersions"
          }

          test "a setup that throws at run time returns runtimeError, not compileError" {
              // The Setup here compiles: it produces zero error diagnostics and fails only when
              // it runs. The diagnostics-first check must therefore fall through to the
              // exception branch. This guards the boundary from the other side: a check that
              // treated any non-success Setup as a compile error would misreport this case, and
              // the two outcomes must stay distinct.
              let source =
                  script "let x : int = failwith \"setup blew up\"\n\nhttp {\n    GET \"https://example.com\"\n}\n"

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
                  script (sprintf "http {\n    GET \"%s/stream.png\"\n}\n" server.BaseUrl)

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

              let source = script (sprintf "http {\n    GET \"%s/hang\"\n}\n" server.BaseUrl)

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
              let source = script "http {\n    GET \"http://127.0.0.1:1/nope\"\n}\n"

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
