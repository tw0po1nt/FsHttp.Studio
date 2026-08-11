// A build-time runtime smoke for the renderer core's *JavaScript* output. The Seam-B Expecto
// suite proves the render logic on .NET. This module proves that the same logic bundles and runs
// after Fable compiles it to JS, which is a gap that the .NET suite structurally cannot see.
//
// It exists because that gap caused a real failure. Fable 5.9's bundled fable-library omits the
// zero-argument `StringBuilder.ToString()` that its own codegen emits. A StringBuilder in the
// core therefore failed to bundle for the webview, while every .NET test stayed green.
//
// Fable compiles `run`, esbuild bundles it, and CI executes it under node (see ../smoke.mjs). It
// throws on the first mismatch, which gives a non-zero exit.
//
// This module has no top-level side effect. `smoke.mjs` imports `run` and calls it. A reference
// to this module, or a webview project pulled into a .NET build, therefore never runs the checks.
module Webview.Smoke

open System
open Renderer.Core
open Renderer.NodeQuery

let private utf8 (s: string) = Text.Encoding.UTF8.GetBytes s

let private env ct (body: byte[]) =
    { Request =
        { Method = "GET"
          Url = "https://ex/"
          Headers = []
          ContentType = ""
          Body = NoBody }
      Status = 200
      Reason = "OK"
      Headers = [ "Content-Type", ct ]
      ContentType = ct
      Body = body
      RequestMs = 42.0
      TotalMs = 100.0 }

let private check (name: string) (cond: bool) =
    if cond then
        printfn "  ok — %s" name
    else
        failwithf "renderer JS smoke FAILED: %s" name

/// Drives the Fable-compiled renderer on one representative envelope for each dispatch path,
/// and asserts the resulting DOM shape. It throws on the first failure, so the node process
/// exits with a non-zero code.
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
