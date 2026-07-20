# Security Policy

## Supported versions

FsHttp.Studio is pre-1.0 and moves fast. Security fixes land on the latest
released version only; please reproduce any report against the newest
`fshttp-studio-<version>.vsix` before filing.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through GitHub's
[**Report a vulnerability**](https://github.com/tw0po1nt/FsHttp.Studio/security/advisories/new)
button (Security → Advisories). This opens a private advisory visible only to
the maintainers. If you can't use GitHub advisories, email
**matt@twopoint.dev** instead.

Please include:

- the extension version and your editor + OS,
- what an attacker could do (impact),
- and the minimal steps or `.fsx` sample to reproduce it.

You'll get an acknowledgement within a few days. Once a fix ships, we're happy
to credit you in the advisory unless you'd prefer to stay anonymous.

## Scope notes

FsHttp.Studio evaluates the F# script you open and runs the HTTP requests it
contains — that execution is the intended behavior, not a vulnerability. The
interesting boundaries for reports are the **companion process protocol**, the
**webview response viewer** (it renders untrusted response bodies — HTML, JSON,
images — from whatever server your request hit), and anything that lets a
*response* escape the viewer's sandbox or run code in the extension host.
