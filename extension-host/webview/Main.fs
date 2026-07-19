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

// The in-flight Run indicator ticks a `setInterval`; the id lives here so any terminal message
// (`result`/`error`) — or a second `running` for the next Run — can stop it before rendering.
let mutable private pendingTimer: float option = None

let private clearPending () =
    match pendingTimer with
    | Some id ->
        window.clearInterval id
        pendingTimer <- None
    | None -> ()

/// Replaces the panel contents with a pulsing dot and a live "Running… Ns" counter, driven by a
/// once-a-second interval, so a slow first Run (`#r "nuget:"` restore / `--worker` cold-start) shows
/// motion instead of looking frozen. Cleared when the run settles.
let private showRunning () =
    clearPending ()
    root.innerHTML <- ""

    let container = document.createElement "div"
    container.className <- "pending"
    let pulse = document.createElement "span"
    pulse.className <- "pending-pulse"
    let label = document.createElement "span"
    label.className <- "pending-label"
    label.textContent <- "Running…"
    container.appendChild pulse |> ignore
    container.appendChild label |> ignore
    root.appendChild container |> ignore

    let start = DateTime.Now

    let tick () =
        let elapsed = DateTime.Now - start
        let secs = int elapsed.TotalSeconds
        label.textContent <- sprintf "Running… %ds" secs

    pendingTimer <- Some(window.setInterval (tick, 1000))

let private handle (data: obj) =
    match unbox<string> (data?tag: obj) with
    | "running" -> showRunning ()
    | "result" ->
        clearPending ()
        Dom.renderInto root (toEnvelope (data?envelope: obj))
    | "error" ->
        clearPending ()
        root.textContent <- unbox<string> (data?message: obj)
    | _ -> ()

window.onmessage <- fun (ev: MessageEvent) -> handle ev.data
