// This module must keep no top-level side effect. `smoke.mjs` imports `run` and calls it.
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
        printfn "  ok: %s" name
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

    let copyJson = """{"a":1,"b":[true,null,"x"]}"""

    check
        "copyText JSON body is the raw UTF-8 text"
        (copyText (env "application/json" (utf8 copyJson)) "response-body" = Some copyJson)

    let withRequestBody (bytes: byte[]) =
        { env "text/plain" (utf8 "ok") with
            Request =
                { Method = "PUT"
                  Url = "https://ex/items?f[a]=1"
                  Headers = [ "Content-Type", "text/plain"; "Content-Length", "13" ]
                  ContentType = "text/plain"
                  Body = Captured bytes } }

    check
        "copyText curl quotes a single quote, and puts a body with characters outside ASCII inline"
        (copyText (withRequestBody (utf8 "it's café 😀")) "curl" = Some(
            "curl -X PUT 'https://ex/items?f[a]=1' \\\n"
            + "  --globoff \\\n"
            + "  -H 'Content-Type: text/plain' \\\n"
            + "  -H 'User-Agent:' \\\n"
            + "  -H 'Accept:' \\\n"
            + "  --data-raw 'it'\\''s café 😀'"
        ))

    let pipeCommand (base64: string) =
        "printf '%s' '"
        + base64
        + "' \\\n"
        + "  | base64 -d \\\n"
        + "  | curl -X PUT 'https://ex/items?f[a]=1' \\\n"
        + "    --globoff \\\n"
        + "    -H 'Content-Type: text/plain' \\\n"
        + "    -H 'User-Agent:' \\\n"
        + "    -H 'Accept:' \\\n"
        + "    --data-binary @-"

    check
        "copyText curl gives the base64 pipe for a CR, a C1 control character, and invalid UTF-8"
        (copyText (withRequestBody (utf8 "a\r\nb")) "curl" = Some(pipeCommand "YQ0KYg==")
         && copyText (withRequestBody [| 0xC2uy; 0x9Fuy |]) "curl" = Some(pipeCommand "wp8=")
         && copyText (withRequestBody [| 0xEDuy; 0xA0uy; 0x80uy |]) "curl" = Some(pipeCommand "7aCA"))

    printfn "renderer JS smoke: all checks passed"
