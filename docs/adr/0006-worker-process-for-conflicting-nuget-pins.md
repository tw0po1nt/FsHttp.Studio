# Isolate conflicting `#r "nuget:"` version pins in a throwaway worker process

Package assemblies that `#r "nuget:"` resolves load into the process-wide default `AssemblyLoadContext`, and they outlive each per-Run FCS session. Two Runs in one companion that pin *different* versions of the same package therefore collide with "Could not load type … from assembly …".

The companion evaluates in-process (warm) by default. It delegates a Run to a short-lived `dotnet Companion.dll --worker` child only when that Run's pins conflict with a version already loaded in the process. The child is a fresh process, so it has a fresh ALC that ends with it.

## Considered Options

We weighed three alternatives on correctness, per-Run latency, and complexity:

- **A collectible ALC for each Run.** This gives true in-process isolation. But FCS has no supported hook that loads `#r "nuget:"` references into a per-session context, and they always go to the default ALC. This option is not available.
- **Always evaluate in a child process.** This is the simplest option, and it is correct by construction, because the process ALC never accumulates. But it pays the FCS cold start of approximately 1 to 2 seconds on *every* click. That is a regression against the long-lived-companion design ([ADR-0002](0002-fcs-companion-framed-envelope.md)), whose purpose is to stay warm.
- **Recycle the whole companion when a pin changes.** This keeps the companion warm. But it spreads the fix across the process seam: host process management, a new protocol outcome, and run-retry code. That is the largest number of moving parts for the least contained change.

We chose the hybrid: a warm parent, and a worker only on a conflict. The collision occurs only when a pin changes during a session, which is uncommon. The common case therefore keeps the warm fast path of ADR-0002, and only the rare conflicting Run pays a process cost. We made this choice as the project moved from a proof-of-concept toward a shippable extension, because we want correctness *and* performance.

## Consequences

The companion now spawns children of its own binary through a `--worker` entry point. The worker serves exactly one Run against its clean ALC, and then exits. It evaluates in-process directly, so it can never spawn another worker recursively.

The parent and the worker exchange the same framed run envelope as the host-to-companion channel, through `outcomeToWire` and `wireToOutcome`. The two channels therefore cannot drift apart. The parent tracks loaded package versions in a process-global map. A worker never touches that map, because a worker's assemblies load into its own process.

### Version-less pins

A version-less `#r "nuget: FsHttp"` resolves *some* latest version into the process-wide ALC, exactly as a pinned reference does. It only fails to name the version. The map therefore records it as a distinct `Versionless` marker instead of nothing. Conflict detection routes a Run to a worker unless it can *prove* that the requested load matches what the ALC already holds:

- **A version-less load, then a version-less pin, is safe in-process.** In one process, nuget resolves `#r "nuget: pkg"` to the same latest version every time. A later version-less Run of a loaded package therefore adds no new version, and stays on the warm path.
- **Two explicit pins conflict exactly when they name different versions.** This is the original case.
- **Every *mixed* pair conflicts, and routes to a worker.** A version-less load and then an explicit pin, *and* an explicit pin and then a version-less load, both go to a fresh child. We cannot name the version that the version-less side resolved, so we cannot prove that it equals the pinned version. An assumption that the two are equal is what let a version-less load poison the in-process ALC of a later or earlier pinned Run.

This rule is deliberately conservative. A mixed pair whose version-less side *does* resolve to the pinned version still routes to a worker. That costs one cold Run, and never a collision, which is the correct error to make.

The symmetry closes the collision class in both directions. The rule gives up one property: a version-less Run no longer stays in-process when the same package already carries an explicit pin. The common cases stay correct and warm. Those cases are all version-less pins, or the same explicit pin every time.
