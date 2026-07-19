# FsHttp.Studio

A VSCode extension that runs a single [FsHttp](https://github.com/fsprojects/FsHttp) request straight from your F# script and **renders** the response — images, JSON, HTML — instead of flattening it to text.

> **Status: v0.1, preparing for release.** The extension is wired end-to-end and its proof-of-concept demo landed well; the work now is a release-readiness pass toward publishing on the VS Code Marketplace and Open VSX. See [Project status](#project-status).

## Why

FsHttp pitches itself as a code-first replacement for Postman and `.http` files, and for *describing* requests it's excellent. But the only way to run an `http { }` block today is FSI — and FSI can only **print**. It flattens every response into a string: an image comes back as a byte dump, a JSON payload as one dense unbrowsable line, an HTML page as escaped source. The part that makes a request tool worth using — *seeing* the response — is exactly what the F# workflow can't do. Worse, FSI's printer silently destroys the response body (it's read-once by default), so reaching for the bytes yourself is a trap.

FsHttp.Studio fills that gap: the request stays as idiomatic, source-controllable F#, and the response gets rendered richly, in the editor.

## What v0.1 does

Open a `.fsx` script with FsHttp requests in it. A **`▶ Run request` CodeLens** appears above each `http { }` block. Click it, and:

- Only *that* block runs — evaluated against a fresh evaluation of its surrounding **setup** (`open`s, `#r`, helpers), never firing the other requests in the file.
- A **response viewer** panel opens beside your editor and renders the body, dispatched on its `Content-Type`:
  - **images** display inline (the clearest thing a text printer can never do),
  - **JSON** as a collapsible, syntax-highlighted tree,
  - **HTML** as a rendered page,
  - everything else as readable, wrapped text.
- A thin status line shows method, URL, a colour-coded status code, round-trip time and size, with the headers one collapse away.

Your `#r "nuget: FsHttp, x.y.z"` version pin is honoured exactly.

## How it works

Two processes, one protocol:

- The **extension host** — written in F#, compiled with Fable (Ionide-style) — owns the CodeLens, the response viewer, and the shell-agnostic **renderer core** (`Content-Type → DOM`).
- The **companion** — a long-lived .NET process hosting an FSharp.Compiler.Service interactive session — owns all F# parsing and evaluation. It locates blocks, evaluates the clicked one, and extracts the response.
- They exchange tagged **envelopes** across the companion's process boundary. That boundary is the editor-agnostic seam: a different editor would reimplement only the host.

The design deliberately extracts the response by *reflection* rather than referencing FsHttp directly, so the host never overrides the user's version pin. The whole chain — block → structured response → rendered image — is wired end-to-end in the extension.

The companion runs on the **.NET 10 SDK or newer**, which you install yourself — a full SDK, not just the runtime, because evaluating `#r "nuget: FsHttp, x.y.z"` restores the package through `dotnet msbuild`. Grab it from [aka.ms/dotnet/download](https://aka.ms/dotnet/download) (as F# developers almost always already have). FsHttp.Studio auto-detects `dotnet` on your `PATH`; if your SDK lives elsewhere, point the `fshttpStudio.dotnetPath` setting at your `dotnet` executable.

The architectural decisions and their trade-offs are recorded in [`docs/adr/`](./docs/adr/); the domain vocabulary lives in [`CONTEXT.md`](./CONTEXT.md).

## Out of scope for v0.1

v0.1 set out to gauge whether F# developers want this — its proof-of-concept demo landed well. Still deliberately deferred beyond v0.1:

- the **request tree** (grouping, naming, a Testing-API-style tree of your requests),
- **settings**,
- **`.fs`-in-a-project** execution (v0.1 is `.fsx`-only; blocks in compiled `.fs` files show no Run affordance — by design),
- **request-chaining** (a block that needs a prior request's *response*, e.g. an auth token),
- the **inline-card** presentation (rendering under the block rather than in a panel),
- dedicated **error/timeout/huge-body** rendering.

## Project status

This repo was built with a [wayfinder](https://github.com/tw0po1nt/FsHttp.Studio/issues/1) map — the design was charted as decision tickets, then synthesized into a spec:

- **Map** (the shared plan): [#1](https://github.com/tw0po1nt/FsHttp.Studio/issues/1)
- **Spec** (what v0.1 is): [#13](https://github.com/tw0po1nt/FsHttp.Studio/issues/13)
- **Glossary:** [`CONTEXT.md`](./CONTEXT.md) · **Decisions:** [`docs/adr/`](./docs/adr/)

Every design decision traces to a resolved ticket on the map. The extension is now wired end-to-end; the current focus is a [release-readiness pass](https://github.com/tw0po1nt/FsHttp.Studio/issues/48) toward publishing on the VS Code Marketplace and Open VSX.

## Built with

F# · [Fable](https://fable.io/) · the VSCode extension API · [FsHttp](https://github.com/fsprojects/FsHttp) · [FSharp.Compiler.Service](https://fsharp.github.io/fsharp-compiler-docs/)

## License

Licensed under the [MIT License](./LICENSE).

The published extension bundles a small set of MIT-licensed components (FSharp.Core, FSharp.Compiler.Service, and Fable's `fable-library`); their notices are reproduced in [`extension-host/THIRD-PARTY-NOTICES.md`](./extension-host/THIRD-PARTY-NOTICES.md).
