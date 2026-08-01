# Security Policy

## Supported versions

FsHttp.Studio is pre-1.0 and changes quickly. Security fixes go only to the
latest released version. Before you file a report, reproduce the problem
against the newest `fshttp-studio-<version>.vsix`.

## Report a vulnerability

**Do not open a public issue for a security problem.**

Report the vulnerability privately with GitHub's
[**Report a vulnerability**](https://github.com/tw0po1nt/FsHttp.Studio/security/advisories/new)
button (Security → Advisories). The button opens a private advisory that only
the maintainers can see. If you cannot use GitHub advisories, send email to
**matt@twopoint.dev** instead.

Include this information:

- The extension version, your editor, and your operating system.
- The impact: what an attacker can do.
- The minimum steps, or the `.fsx` sample, that reproduce the problem.

You will receive an acknowledgment in a few days. After a fix ships, we can
credit you in the advisory. Tell us if you prefer to stay anonymous.

## Scope notes

FsHttp.Studio evaluates the F# script you open, and runs the HTTP requests in
that script. This execution is the intended behavior, not a vulnerability.

Report problems at these boundaries:

- The **companion process protocol**, which crosses the companion's process
  boundary.
- The **response viewer** webview. It renders untrusted response bodies (HTML,
  JSON, and images) from the server that answered your request.
- Any path that lets a *response* escape the viewer's sandbox, or run code in
  the extension host.
