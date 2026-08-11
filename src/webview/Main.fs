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

// The three `bodyState` names of Decision 10, written once for this end of the viewer-update
// wire. The host spells the same three in `Protocol`; the two projects cannot share a module,
// because the extension does not reference the renderer core. Adding a fourth state means an
// edit here, one in the host, and one in the companion.
[<Literal>]
let private NoneState = "none"

[<Literal>]
let private CapturedState = "captured"

[<Literal>]
let private NotCapturedState = "notCaptured"

/// Maps the viewer update's three-state body triple onto `CapturedBody`. An unknown state throws
/// rather than decaying: `NotCaptured` carries a reason that is shown to the user, and Decision 8
/// reserves that slot for the written reasons the capture produces. A wire term rendered there
/// would read as an explanation of the request. `handle` turns the throw into the viewer's error
/// text, which is where a defect on our own wire belongs
/// (docs/spec/0012-request-as-sent.md, Decision 8; Seam 3, test 15 makes the host's parse agree).
let private toCapturedBody (raw: obj) : CapturedBody =
    match unbox<string> (raw?bodyState: obj) with
    | NoneState -> NoBody
    | CapturedState -> Captured(Convert.FromBase64String(unbox<string> (raw?bodyBase64: obj)))
    | NotCapturedState -> NotCaptured(unbox<string> (raw?bodyReason: obj))
    | other -> failwithf "viewer update: unknown bodyState '%s'" other

let private toRequest (raw: obj) : RequestView =
    { Method = unbox<string> (raw?method: obj)
      Url = unbox<string> (raw?url: obj)
      Headers = toHeaders (raw?headers: obj)
      ContentType = unbox<string> (raw?contentType: obj)
      Body = toCapturedBody raw }

let private toEnvelope (raw: obj) : ResponseEnvelope =
    { Request = toRequest (raw?request: obj)
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

/// Renders a result update, or the reason it could not be decoded. `toEnvelope` reads a wire both
/// ends of which are ours, so a value it rejects is a defect and not a response — showing it as
/// error text keeps the panel from going silently blank, and keeps a wire term out of the request
/// view's own reason slot.
let private showResult (envelope: obj) =
    try
        Dom.renderInto root (toEnvelope envelope)
    with ex ->
        root.textContent <- ex.Message

let private handle (data: obj) =
    match unbox<string> (data?tag: obj) with
    | "running" -> showRunning ()
    | "result" ->
        clearPending ()
        showResult (data?envelope: obj)
    | "error" ->
        clearPending ()
        root.textContent <- unbox<string> (data?message: obj)
    | "refused" -> showRefused (unbox<string> (data?title: obj)) (unbox<string> (data?detail: obj))
    | _ -> ()

window.onmessage <- fun (ev: MessageEvent) -> handle ev.data
