# v0.1 supports .fsx scripts only, with no Run affordance on .fs

Both block detection and execution target `.fsx` scripts only. A block living in a compiled `.fs` file shows no Run request CodeLens at all — deliberately, not as a bug.

A self-contained script *is* the FSI input, so both evaluation paths take it for free. A block in a `.fs` file instead draws its `#r`/`open`s/bindings from the compiled project context a fresh FSI session lacks; supporting it means reconstructing that context from the project's NuGet graph — a project-load subsystem out of proportion to a proof-of-concept. A present-but-broken CodeLens on `.fs` would read as buggy, whereas its absence plus README framing reads as scoped. Recorded so no one "fixes" the missing `.fs` affordance without understanding the cost behind it.
