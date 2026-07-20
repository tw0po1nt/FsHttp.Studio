# Execute blocks in an FCS-hosted companion, marshal responses as a framed envelope

A single `http { }` block is F# code, not data, so running it requires an F# evaluator. We host FSharp.Compiler.Service's interactive session in a long-lived .NET companion process and return each response to the extension host as a tagged, framed JSON envelope across the process boundary.

## Considered Options

We built two working prototypes: driving `dotnet fsi` and scraping a sentinel-delimited protocol off its stdout, and hosting FCS as a library. Both round-tripped an image at ~5 ms/request, so this was not a performance call. We chose FCS because it is correct *by construction* — the response comes back as a typed value, so there is no shared stdout channel to collide on and no text to scrape — and it yields structured compile diagnostics with source ranges. The sentinel design is correct only *by convention*.

## Consequences

The companion must **not** statically reference FsHttp — doing so silently overrides the user's `#r "nuget:"` version pin — so responses are extracted by reflection over the BCL `HttpContent` type, which is shared across every FsHttp version. The process boundary doubles as the editor-agnostic seam: another editor reuses the companion and envelope unchanged, reimplementing only the host. Proven end-to-end by prototype (`prototype/dotnet-to-js-seam`).

### Toolchain pin

Hosting FCS *as a library* (rather than shelling out to `dotnet fsi`) puts the companion's own FSharp.Core on the same object graph as the compiler it hosts, so the two must agree on assembly identity. The companion therefore pins **FSharp.Core to the exact version FCS carries** (currently FSharp.Core `10.1.204` alongside `FSharp.Compiler.Service 43.12.204`) and sets `DisableImplicitFSharpCoreReference` so the SDK's default FSharp.Core can't slip a second identity into the graph — see `companion/Companion.fsproj`. The companion targets **.NET 10** (`RollForward=LatestMajor`, so a newer-only SDK still launches it); the extension host mirrors that major as an activation-time SDK floor (`Extension.fs`). These are consequences of the host-FCS-as-library choice, which is why the code pins reference this ADR.
