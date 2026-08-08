module Companion.Program

// The companion's entry point and I/O loop. It is the two-process framed-envelope transport,
// ported from prototype/dotnet-to-js-seam. It wires block location and block evaluation
// (ADR-0002's FCS session) through RequestHandler.respond.

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

    let emit (o: obj) =
        writeFrame rawStdout (JsonSerializer.SerializeToUtf8Bytes o)

    rawStdin, emit

// The long-lived companion. It reads a framed request and responds, until the host closes stdin.
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

// The `--worker` child. It serves exactly one `{ source, blockIndex, scriptFileName? }`
// request against this process's own fresh ALC, and then exits with its `#r "nuget:"`
// assemblies. It bypasses `run`'s conflict routing, because a fresh process holds nothing that
// can conflict. It evaluates in-process directly, so a worker can never spawn another worker
// recursively.
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
        emit (BlockRunner.outcomeToWire (BlockRunner.runInProcessDirect source blockIndex scriptFileName))

[<EntryPoint>]
let main argv =
    if Array.contains "--worker" argv then
        runWorker ()
    else
        runCompanion ()

    0
