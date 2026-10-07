# The Neovim client is plain Lua, and shares text and Golden fixtures with the F# host

The Neovim client is hand-written Lua on Neovim 0.11 or later. No F# code runs in Neovim, and the
user needs no runtime other than Neovim and the .NET SDK. The two Clients share no runtime code.
They share a source of truth: an F# script generates the user-facing text from `Refusals.fs` as a
Lua table, and F# tests write Golden fixtures that the Lua core suite must match byte for byte.

## Considered Options

- **Fable to Lua.** Fable has no Lua target. The draft spike compiles none of the pure host files to
  Lua that LuaJIT can run. An upstream target is a separate, long effort.
- **A Lua shim that starts a Fable JS process.** This reuses about 660 lines of the host. But
  Node.js becomes a second runtime, and the F# side needs hand-written bindings for the Neovim API.
- **Lua with an F# sidecar on .NET.** The sidecar adds a third process, a second .NET start, and an
  API between Lua and the sidecar to keep in step.
- **The companion talks to Neovim.** This contradicts [ADR-0002](0002-fcs-companion-framed-envelope.md),
  which keeps the companion free of editor knowledge.
- **A typed Lua dialect.** It gives no F# reuse and adds a build step.

## Consequences

Drift between the two Clients is the main cost. Two guards hold it: the generated `refusals.lua`,
which CI checks against `Refusals.fs`, and the Golden fixtures for the envelope and each pure rule.

The Lua client keeps its pure rules behind a module seam, and a core module never reads the `vim`
global. Generated code can replace that core if a Fable Lua target becomes usable.

See [docs/spec/0015](../spec/0015-neovim-client.md), decision A1.
