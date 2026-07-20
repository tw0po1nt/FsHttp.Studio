// A build-time runtime smoke for the renderer core's *JavaScript* output. The Seam-B Expecto suite
// proves the render logic on .NET; this proves the same logic actually bundles and runs once Fable
// compiles it to JS — a gap the .NET suite structurally cannot see. It exists because exactly that
// gap bit us: Fable 5.9's bundled fable-library omits the zero-arg `StringBuilder.ToString()` its
// own codegen emits, so a StringBuilder in the core failed to bundle for the webview while every
// .NET test stayed green. `run` is compiled by Fable, bundled by esbuild, and executed under node
// in CI (see ../smoke.mjs); it throws — non-zero exit — on the first mismatch.
//
// No top-level side effect: `smoke.mjs` imports `run` and calls it, so merely referencing this
// module (or pulling the webview project into a .NET build) never executes the checks.
module Webview.Smoke

open System
open Renderer.Core
open Renderer.NodeQuery

let private utf8 (s: string) = Text.Encoding.UTF8.GetBytes s

let private env ct (body: byte[]) =
    { Method = "GET"
      Url = "https://ex/"
      Status = 200
      Reason = "OK"
      Headers = [ "Content-Type", ct ]
      ContentType = ct
      Body = body
      ElapsedMs = 42.0 }

let private check (name: string) (cond: bool) =
    if cond then
        printfn "  ok — %s" name
    else
        failwithf "renderer JS smoke FAILED: %s" name

/// Exercises the Fable-compiled renderer on one representative envelope per dispatch path and
/// asserts the resulting DOM shape. Throws on the first failure so the node process exits non-zero.
let run () : unit =
    printfn "renderer JS smoke:"

    let image = renderBody (env "image/png" [| 0x89uy; 0x50uy; 1uy; 2uy; 3uy |])

    check
        "image → <img> with a data: URI"
        (match byTag "img" image with
         | [ i ] -> (attr "src" i |> Option.defaultValue "").StartsWith "data:image/png;base64,"
         | _ -> false)

    let json =
        renderBody (env "application/json" (utf8 """{"a":1,"b":[true,null,"x"]}"""))

    check
        "JSON → collapsible tree"
        (not (List.isEmpty (byClass "response-json" json))
         && not (List.isEmpty (byTag "details" json))
         && not (List.isEmpty (byClass "json-number" json)))

    let html = renderBody (env "text/html" (utf8 "<h1>hi</h1>"))

    check
        "HTML → sandboxed iframe"
        (match byTag "iframe" html with
         | [ f ] -> attr "sandbox" f = Some "" && attr "srcdoc" f = Some "<h1>hi</h1>"
         | _ -> false)

    let text = renderBody (env "text/plain" (utf8 "hello"))
    check "text → <pre>" (not (List.isEmpty (byClass "response-text" text)))

    let binary =
        renderBody (env "application/octet-stream" [| 0uy; 1uy; 255uy; 0uy; 65uy |])

    check "binary → hex fallback (no throw)" (not (List.isEmpty (byClass "hex-dump" binary)))

    let notFound =
        render
            { env "application/json" (utf8 """{"e":1}""") with
                Status = 404
                Reason = "Not Found" }

    check
        "non-2xx → body rendered with code shown"
        (byClass "status-code" notFound
         |> List.exists (fun n -> (innerText n).Contains "404")
         && not (List.isEmpty (byClass "response-json" notFound)))

    printfn "renderer JS smoke: all checks passed"
