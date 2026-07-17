module Companion.Program

// Walking skeleton (issue #14): proves the framed envelope transport inside the real
// two-process layout, porting prototype/dotnet-to-js-seam. Block location and block
// evaluation (ADR-0002's FCS session) land on later tickets.

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

        let tag =
            match doc.RootElement.TryGetProperty "tag" with
            | true, v -> v.GetString()
            | false, _ -> null

        match tag with
        | "hello" -> emit {| tag = "ready" |}
        | other ->
            emit
                {| tag = "error"
                   message = sprintf "unknown request tag '%s'" (string other) |}

    let rec loop () =
        match tryReadFrame rawStdin with
        | None -> () // stdin closed: the extension host has gone away
        | Some payload ->
            handle payload
            loop ()

    loop ()
    0
