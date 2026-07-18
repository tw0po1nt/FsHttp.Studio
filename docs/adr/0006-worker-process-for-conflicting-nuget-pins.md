# Isolate conflicting `#r "nuget:"` version pins in a throwaway worker process

`#r "nuget:"`-resolved package assemblies load into the process-wide default `AssemblyLoadContext` and outlive each per-Run FCS session, so two Runs in one companion that pin *different* versions of the same package collide ("Could not load type … from assembly …"). The companion keeps evaluating in-process (warm) by default and delegates a Run to a short-lived `dotnet Companion.dll --worker` child — a fresh process, hence a fresh ALC that dies with it — only when that Run's pins conflict with a version already loaded in the process.

## Considered Options

Three alternatives, weighed on correctness vs. per-Run latency vs. complexity:

- **A collectible ALC per Run.** True in-process isolation, but FCS gives no supported hook to load `#r "nuget:"` references into a per-session context — they always land in the default ALC. Not actually available.
- **Always evaluate in a child process.** Simplest and correct by construction (the process ALC never accumulates), but pays FCS cold-start (~1–2s) on *every* click — a regression against the long-lived-companion design ([ADR-0002](0002-fcs-companion-framed-envelope.md)) whose whole point is to stay warm.
- **Recycle the whole companion on a pin change.** Preserves warmth, but spreads the fix across the process seam — host process-management, a new protocol outcome, and run-retry plumbing — the most moving parts for the least contained change.

We picked the hybrid — warm parent, worker only on conflict — because the collision only arises when a pin actually changes mid-session (uncommon), so the common case keeps ADR-0002's warm fast path and only the rare conflicting Run pays a process cost. It was chosen as the project moved from PoC toward a shippable extension, wanting correctness *and* performance rather than the simpler always-child option.

## Consequences

The companion now self-spawns children of its own binary through a `--worker` entry point; the worker serves exactly one Run against its clean ALC and exits, evaluating in-process directly so it can never recursively spawn another worker. Parent and worker exchange the same framed run envelope (via `outcomeToWire`/`wireToOutcome`) as the host↔companion channel, so the two channels can't drift. The parent tracks loaded package versions in a process-global map that a worker never touches, since a worker's assemblies load into its own process. Conflict detection is best-effort: only explicitly-versioned pins participate — a version-less `#r "nuget: FsHttp"` is treated as "whatever resolves" and never forces a worker.
