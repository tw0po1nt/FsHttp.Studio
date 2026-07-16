# Execute blocks in an FCS-hosted companion, marshal responses as a framed envelope

A single `http { }` block is F# code, not data, so running it requires an F# evaluator. We host FSharp.Compiler.Service's interactive session in a long-lived .NET companion process and return each response to the extension host as a tagged, framed JSON envelope across the process boundary.

## Considered Options

We built two working prototypes: driving `dotnet fsi` and scraping a sentinel-delimited protocol off its stdout, and hosting FCS as a library. Both round-tripped an image at ~5 ms/request, so this was not a performance call. We chose FCS because it is correct *by construction* — the response comes back as a typed value, so there is no shared stdout channel to collide on and no text to scrape — and it yields structured compile diagnostics with source ranges. The sentinel design is correct only *by convention*.

## Consequences

The companion must **not** statically reference FsHttp — doing so silently overrides the user's `#r "nuget:"` version pin — so responses are extracted by reflection over the BCL `HttpContent` type, which is shared across every FsHttp version. The process boundary doubles as the editor-agnostic seam: another editor reuses the companion and envelope unchanged, reimplementing only the host. Proven end-to-end by prototype (`prototype/dotnet-to-js-seam`).
