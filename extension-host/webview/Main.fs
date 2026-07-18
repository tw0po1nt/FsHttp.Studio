// The webview entry: listens for the extension host's postMessage protocol
// (`running` / `result` / `error`) and mounts the renderer core's output via `Webview.Dom`, or a
// plain placeholder/error text otherwise.
module Webview.Main

open System
open Fable.Core.JsInterop
open Browser
open Browser.Types
open Renderer.Core

let private root: HTMLElement = document.getElementById "root"

let private toHeaders (raw: obj) : (string * string) list =
    unbox<(string * string)[]> raw |> Array.toList

let private toEnvelope (raw: obj) : ResponseEnvelope =
    { Method = unbox<string> (raw?method: obj)
      Url = unbox<string> (raw?url: obj)
      Status = unbox<int> (raw?status: obj)
      Reason = unbox<string> (raw?reason: obj)
      Headers = toHeaders (raw?headers: obj)
      ContentType = unbox<string> (raw?contentType: obj)
      Body = Convert.FromBase64String(unbox<string> (raw?bodyBase64: obj))
      ElapsedMs = unbox<float> (raw?elapsedMs: obj) }

let private handle (data: obj) =
    match unbox<string> (data?tag: obj) with
    | "running" -> root.textContent <- "Running…"
    | "result" -> Dom.renderInto root (toEnvelope (data?envelope: obj))
    | "error" -> root.textContent <- unbox<string> (data?message: obj)
    | _ -> ()

window.onmessage <- fun (ev: MessageEvent) -> handle ev.data
