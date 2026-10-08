module Companion.Program


open System
open System.Text.Json
open Companion.Envelope

/// Sets up the frame channel the same way for both entry modes. Take the real stdout FIRST,
/// then redirect all other output to stderr, so that only envelopes cross the wire. Returns
/// the raw stdin handle and an `emit` that frames a response object onto stdout.
let private openFrameChannel () =
    let rawStdout = Console.OpenStandardOutput()
    let rawStdin = Console.OpenStandardInput()
    Console.SetOut(Console.Error)

    let emit (o: obj) = writeFrame rawStdout (encode o)

    rawStdin, emit

let private runCompanion () =
    let rawStdin, emit = openFrameChannel ()

    let handle (payload: byte[]) =
        use doc = JsonDocument.Parse(payload)
        emit (RequestHandler.respond doc)

    let rec loop () =
        match tryReadFrame rawStdin with
        | None -> () // stdin closed: the extension host has exited
        | Some payload ->
            handle payload
            loop ()

    loop ()

// A worker evaluates in-process, so it can never spawn a second worker.
let private runWorker () =
    let rawStdin, emit = openFrameChannel ()

    match tryReadFrame rawStdin with
    | None -> ()
    | Some payload ->
        use doc = JsonDocument.Parse(payload)
        let root = doc.RootElement
        let source = getStringProp "source" root
        let blockIndex = getIntProp "blockIndex" root
        let scriptFileName = getOptionalStringProp "scriptFileName" root
        let timeoutMs = getIntProp "timeoutMs" root
        emit (BlockRunner.outcomeToWire (BlockRunner.runInProcessDirect source blockIndex scriptFileName timeoutMs))

[<EntryPoint>]
let main argv =
    if Array.contains "--worker" argv then
        runWorker ()
    else
        runCompanion ()

    0
