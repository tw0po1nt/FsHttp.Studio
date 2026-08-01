# v0.1 supports .fsx scripts only, with no Run affordance on .fs

Both block detection and block execution target `.fsx` scripts only. A block in a compiled `.fs` file shows no Run request CodeLens. This is deliberate, and not a bug.

A self-contained script *is* the FSI input, so both evaluation paths get it for free. A block in a `.fs` file instead draws its `#r` references, `open`s, and bindings from the compiled project context. A fresh FSI session does not have that context. Support for `.fs` files means we must rebuild that context from the project's NuGet graph. That is a project-load subsystem, and it is out of proportion to a proof-of-concept.

A CodeLens that is present but broken on `.fs` would look like a defect. An absent CodeLens, with the explanation in the README, reads as a scope decision instead.

This decision is recorded so that nobody "fixes" the missing `.fs` affordance without an understanding of the cost.
