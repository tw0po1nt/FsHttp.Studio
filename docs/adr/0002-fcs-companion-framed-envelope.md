# Run blocks in an FCS-hosted companion, marshal responses as a framed envelope

A single `http { }` block is F# code, not data, so it needs an F# evaluator. We host FSharp.Compiler.Service's interactive session in a long-lived .NET companion process. The companion returns each response to the extension host as a tagged, framed JSON envelope across the process boundary.

## Considered Options

We built two working prototypes:

- Drive `dotnet fsi`, and scrape a sentinel-delimited protocol from its stdout.
- Host FCS as a library.

Both prototypes round-tripped an image at approximately 5 ms per request, so performance did not decide this. We chose FCS because it is correct *by construction*. The response arrives as a typed value, so no shared stdout channel can collide, and no text needs a scrape. FCS also yields structured compile diagnostics with source ranges. The sentinel design is correct only *by convention*.

## Consequences

The companion must **not** reference FsHttp statically, because a static reference silently overrides the user's `#r "nuget:"` version pin. The companion therefore extracts responses by reflection over the BCL `HttpContent` type, which every FsHttp version shares.

The process boundary is also the editor-agnostic seam. Another editor reuses the companion and the envelope unchanged, and reimplements only the extension host. A prototype proved this end-to-end (`prototype/dotnet-to-js-seam`).

### Toolchain pin

The companion hosts FCS *as a library* instead of a call out to `dotnet fsi`. This puts the companion's own FSharp.Core on the same object graph as the compiler that it hosts, so the two must agree on assembly identity.

The companion therefore pins **FSharp.Core to the exact version that FCS carries**. It currently pins FSharp.Core `10.1.204` with `FSharp.Compiler.Service 43.12.204`. It also sets `DisableImplicitFSharpCoreReference`, so the default FSharp.Core of the SDK cannot add a second identity to the graph. See `companion/Companion.fsproj`.

The companion targets **.NET 10** and sets `RollForward=LatestMajor`, so a newer-only SDK still launches it. The extension host mirrors that major version as an SDK floor at activation time (`Extension.fs`). These constraints are consequences of the FCS-as-a-library choice, which is why the pins in the code refer to this ADR.
