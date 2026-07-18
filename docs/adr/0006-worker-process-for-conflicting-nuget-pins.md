# Isolate conflicting `#r "nuget:"` version pins in a throwaway worker process

`#r "nuget:"`-resolved package assemblies load into the process-wide default `AssemblyLoadContext` and outlive each per-Run FCS session, so two Runs in one companion that pin *different* versions of the same package collide ("Could not load type … from assembly …"). The companion keeps evaluating in-process (warm) by default and delegates a Run to a short-lived `dotnet Companion.dll --worker` child — a fresh process, hence a fresh ALC that dies with it — only when that Run's pins conflict with a version already loaded in the process.

## Considered Options

Three alternatives, weighed on correctness vs. per-Run latency vs. complexity:

- **A collectible ALC per Run.** True in-process isolation, but FCS gives no supported hook to load `#r "nuget:"` references into a per-session context — they always land in the default ALC. Not actually available.
- **Always evaluate in a child process.** Simplest and correct by construction (the process ALC never accumulates), but pays FCS cold-start (~1–2s) on *every* click — a regression against the long-lived-companion design ([ADR-0002](0002-fcs-companion-framed-envelope.md)) whose whole point is to stay warm.
- **Recycle the whole companion on a pin change.** Preserves warmth, but spreads the fix across the process seam — host process-management, a new protocol outcome, and run-retry plumbing — the most moving parts for the least contained change.

We picked the hybrid — warm parent, worker only on conflict — because the collision only arises when a pin actually changes mid-session (uncommon), so the common case keeps ADR-0002's warm fast path and only the rare conflicting Run pays a process cost. It was chosen as the project moved from PoC toward a shippable extension, wanting correctness *and* performance rather than the simpler always-child option.

## Consequences

The companion now self-spawns children of its own binary through a `--worker` entry point; the worker serves exactly one Run against its clean ALC and exits, evaluating in-process directly so it can never recursively spawn another worker. Parent and worker exchange the same framed run envelope (via `outcomeToWire`/`wireToOutcome`) as the host↔companion channel, so the two channels can't drift. The parent tracks loaded package versions in a process-global map that a worker never touches, since a worker's assemblies load into its own process.

### Version-less pins

A version-less `#r "nuget: FsHttp"` resolves *some* latest into the process-wide ALC just as a pinned one does — it merely doesn't name the version. The map therefore records it too, as a distinct `Versionless` marker rather than nothing, and conflict detection routes a Run to a worker unless it can *prove* the requested load matches what the ALC already holds:

- **Version-less then version-less is safe in-process.** Within one process nuget resolves `#r "nuget: pkg"` to the same latest every time, so a later version-less Run of an already-loaded package introduces no new version and stays on the warm path.
- **Two explicit pins conflict exactly when they name different versions** — the original case.
- **Every *mixed* pairing conflicts and is routed to a worker.** A version-less load followed by an explicit pin, *and* an explicit pin followed by a version-less load, both go to a fresh child. We can't name what the version-less side resolved to, so we can't prove it equals the pinned version — and assuming it does is exactly what let a version-less load silently poison a later (or earlier) pinned Run's in-process ALC.

This is deliberately conservative: a mixed pairing whose version-less side *happens* to resolve to the pinned version is routed to a worker anyway. That costs one cold Run, never a collision — the right side to err on. The symmetry closes the collision class in both directions; the only property given up is that a version-less Run is no longer guaranteed to stay in-process when the same package was already pinned explicitly. The common cases (all version-less, or all the same explicit pin) stay both correct and warm.
