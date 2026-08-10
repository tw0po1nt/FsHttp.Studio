// The webview entry point. It listens for the extension host's viewer updates, which are
// `running`, `result`, `error`, and `refused`. It then mounts the renderer core's output through
// `Webview.Dom`. For the other updates it shows plain placeholder text, error text, or a refused
// notice.
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
      RequestMs = unbox<float> (raw?requestMs: obj)
      TotalMs = unbox<float> (raw?totalMs: obj) }

// The in-flight Run indicator ticks a `setInterval`. The id lives here, so that any terminal
// message (`result` or `error`), or a second `running` for the next Run, can stop the interval
// before it renders.
let mutable private pendingTimer: float option = None

let private clearPending () =
    match pendingTimer with
    | Some id ->
        window.clearInterval id
        pendingTimer <- None
    | None -> ()

/// Replaces the panel contents with a pulsing dot and a live "Running… Ns" counter. An interval
/// of one second drives the counter, so a slow first Run shows motion and does not look frozen.
/// A `#r "nuget:"` restore, or a `--worker` cold start, causes such a slow Run. The counter
/// clears when the Run settles.
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

/// Replaces the panel contents with a Refused Run notice: a heading and a body paragraph
/// (docs/spec/0003, Decision 6). This is a notice, not an error, so `responseStyles` gives it the
/// editor foreground for the heading and `--vscode-descriptionForeground` for the body, and no
/// color that reads as a failure.
let private showRefused (title: string) (detail: string) =
    clearPending ()
    root.innerHTML <- ""

    let container = document.createElement "div"
    let heading = document.createElement "h2"
    heading.className <- "refused-title"
    heading.textContent <- title
    let body = document.createElement "p"
    body.className <- "refused-detail"
    body.textContent <- detail
    container.appendChild heading |> ignore
    container.appendChild body |> ignore
    root.appendChild container |> ignore

let private handle (data: obj) =
    match unbox<string> (data?tag: obj) with
    | "running" -> showRunning ()
    | "result" ->
        clearPending ()
        Dom.renderInto root (toEnvelope (data?envelope: obj))
    | "error" ->
        clearPending ()
        root.textContent <- unbox<string> (data?message: obj)
    | "refused" -> showRefused (unbox<string> (data?title: obj)) (unbox<string> (data?detail: obj))
    | _ -> ()

window.onmessage <- fun (ev: MessageEvent) -> handle ev.data
