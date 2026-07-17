module Companion.Program

// Walking skeleton (issue #14) proved the framed envelope transport inside the real
// two-process layout, porting prototype/dotnet-to-js-seam. Block location (issue #15) is
// wired via RequestHandler.respond; block evaluation (ADR-0002's FCS session) lands on #16.

open System
open System.Text.Json
open Companion.Envelope

[<EntryPoint>]
let main _argv =
    // Grab the real stdout FIRST, then redirect everything else to stderr so the
    // framing channel carries only envelopes.
    let rawStdout = Console.OpenStandardOutput()
    let rawStdin = Console.OpenStandardInput()
    Console.SetOut(Console.Error)

    let emit (o: obj) =
        writeFrame rawStdout (JsonSerializer.SerializeToUtf8Bytes o)

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
    0
