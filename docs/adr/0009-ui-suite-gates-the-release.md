---
Status: accepted
Supersedes: ADR-0008
---

# The UI suite gates the release, and a Beta is distribution only

The UI test suite is the release gate. `release.yml` runs the suite with the same budgets and job
retries as `ui-tests.yml`. A red suite refuses the draft Release. A green Actions run is the record
of what was verified. No human comment lists the covered sections.

A Beta remains an optional pre-release `.vsix` channel. It hands someone an installable build. It is
not required for a release. The `force` input on `release.yml` skips the UI suite for an emergency
release, and it logs that skip in the run.

ADR-0008 made a Beta the gate because no suite drove the interop surfaces. Specs 1 through 7 of the
UI suite now drive those surfaces. A tag's existence never showed that anyone verified the product
by hand. The suite is the stronger gate, so it replaces the Beta tag requirement.
