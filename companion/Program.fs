module Companion.Program

// The companion's entry point and I/O loop: the two-process framed-envelope transport (ported
// from prototype/dotnet-to-js-seam), wiring block location and block evaluation (ADR-0002's FCS
// session) through RequestHandler.respond.

open System
open System.Text.Json
open Companion.Envelope

/// Sets up the frame channel the same way for both entry modes: grab the real stdout FIRST,
/// then redirect everything else to stderr so only envelopes cross the wire. Returns the raw
/// stdin/stdout handles and an `emit` that frames a response object onto stdout.
let private openFrameChannel () =
    let rawStdout = Console.OpenStandardOutput()
    let rawStdin = Console.OpenStandardInput()
    Console.SetOut(Console.Error)

    let emit (o: obj) =
        writeFrame rawStdout (JsonSerializer.SerializeToUtf8Bytes o)

    rawStdin, emit

// The long-lived companion: reads a framed request, responds, repeats until the host closes stdin.
let private runCompanion () =
    let rawStdin, emit = openFrameChannel ()

    let handle (payload: byte[]) =
        use doc = JsonDocument.Parse(payload)
        emit (RequestHandler.respond doc)

    let rec loop () =
        match tryReadFrame rawStdin with
        | None -> () // stdin closed: the extension host has gone away
        | Some payload ->
            handle payload
            loop ()

    loop ()

// The `--worker` child: serves exactly one `{ source, blockIndex }` request against this
// process's own fresh ALC, then exits — taking its `#r "nuget:"` assemblies with it. Bypasses
// `run`'s conflict routing (a fresh process has nothing loaded to conflict with) and evaluates
// in-process directly, so a worker can never recursively spawn another worker.
let private runWorker () =
    let rawStdin, emit = openFrameChannel ()

    match tryReadFrame rawStdin with
    | None -> ()
    | Some payload ->
        use doc = JsonDocument.Parse(payload)
        let root = doc.RootElement
        let source = getStringProp "source" root
        let blockIndex = getIntProp "blockIndex" root
        emit (BlockRunner.outcomeToWire (BlockRunner.runInProcessDirect source blockIndex))

[<EntryPoint>]
let main argv =
    if Array.contains "--worker" argv then
        runWorker ()
    else
        runCompanion ()

    0
