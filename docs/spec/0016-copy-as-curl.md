# Copy as curl in both Clients

Spec for v0.3 shared feature 1 of 3. Each Client builds a Curl command from the request as sent.
The map [FsHttp.Studio v0.3: Neovim support and shared features](https://github.com/tw0po1nt/FsHttp.Studio/issues/239)
records the decisions, and the ticket
[How does copy as curl turn each request as sent into a curl command?](https://github.com/tw0po1nt/FsHttp.Studio/issues/256)
records the detail.

## Problem Statement

**A request cannot leave the editor as a command.** A user reads the Request section and wants to
send the same request from a terminal, a CI job, or a bug report. The copy button of the Request
section gives the request as text, which no shell can run. The user must write the curl command by
hand from the method, the URL, the headers, and the body.

The hand-written command is often wrong:

1. A stale `Content-Length` makes curl hang, or cut the body, after the user edits the body.
2. curl adds `application/x-www-form-urlencoded` to a body with no `Content-Type`.
3. A binary body, a multipart body, or a body with a CR cannot go inline in a shell argument.
4. A URL with `[` or `{` turns into a curl glob pattern.

## Solution

**Each Client builds a Curl command from the request as sent.** In a POSIX shell, the command sends
the same method, URL, headers, and body bytes as the Run.

- **VSCode:** a "Copy as curl" button beside the copy button of the Request section.
- **Neovim:** `:FsHttp yank curl` and the `yc` key in the Response buffer.

The companion and the envelope do not change. The `ok` envelope already carries the method, the
URL, the headers, and the Captured body. No setting is added.

## User Stories

1. As a script author, I want to copy the request as sent as a curl command, so that I can send it again from a terminal.
2. As a script author, I want the curl command to send the same method, URL, headers, and body bytes as the Run, so that the server gets the same request.
3. As a script author with a binary body, a multipart body, or a large body, I want the command to send the exact bytes, so that the copy is correct.
4. As a script author whose body the companion did not read, I want no Copy as curl action and a reason, so that I never send a wrong body.
5. As a script author, I want one argument on each line, so that I can read and edit the command.
6. As a script author who edits the body of the command, I want no `Content-Length` header in it, so that curl computes the length again.
7. As a script author with a body and no `Content-Type`, I want the command to send no `Content-Type`, so that curl does not add a form type.
8. As a script author with a URL that contains brackets or braces, I want the command to send the URL as it is, so that curl does not read a glob.
9. As a script author on Git Bash or WSL, I want the command to work in my shell, so that I can use it on Windows.
10. As a VSCode user, I want the Copy as curl button beside the Request copy button, so that I find it where I copy the request.
11. As a Neovim user, I want `:FsHttp yank curl` and `yc`, so that I can yank the command from the keyboard.
12. As a Neovim user, I want a WARN notice with the reason when no command exists, so that I know why the yank gave nothing.
13. As the maintainer, I want both Clients to give the same bytes for each request, so that the two Clients cannot drift.

## Implementation Decisions

### 1. Each Client builds the command

- `Renderer.copyText` gets the key `curl`. It returns `None` when the companion did not read the
  body.
- The Lua client ports that key into its pure core.
- Golden fixtures pin the two outputs to the same bytes, as spec 0015 Part C states for the copy
  payload.

### 2. The term

`GLOSSARY.md` defines the term **Curl command**: the shell text that a Client builds from the request
as sent.

### 3. The body, for each state of the Captured body

| State | Body in the command |
|---|---|
| No body | No data flag. |
| Captured bytes, safe to paste, 16,384 bytes or fewer | `--data-raw '<text>'` |
| Each other set of captured bytes | `printf '%s' '<base64>' \| base64 -d \| curl ... --data-binary @-` |
| Not read by the companion | No Curl command. |

- A body is safe to paste when it is valid UTF-8 and contains no control byte except tab and LF. The
  rule is strict, so it gives the same result in both Clients. The display heuristic `looksBinary`
  does not decide it.
- The inline form uses `--data-raw`, because `--data-binary` and `--data` read a file when the body
  starts with `@`.
- The base64 pipe covers a binary body, a multipart body, a body with a CR, a body with invalid
  UTF-8, and a text body above 16,384 bytes. A large inline argument fails on Linux (128 KB for each
  argument) and in Git Bash (32,767 characters for the Windows command line).

### 4. Method and URL

- curl sends GET with no data flag and POST with a data flag. The command adds a method flag only
  when the method of the Run differs from that default.
- HEAD uses `--head`, because `-X HEAD` makes curl wait for a body.
- Each other case uses `-X <METHOD>`. A GET with a body is one example.
- A URL that contains `[`, `]`, `{`, or `}` gets `--globoff`.

### 5. Headers

- Each header of the Request goes out as `-H 'Name: value'`, in the order of the Request section.
- The command drops `Content-Length` only, because curl computes it.
- Each other header stays as it is, `Host` and `Accept-Encoding` included.
- When the request has a body and no `Content-Type`, the command adds `-H 'Content-Type:'`.

### 6. Quoting and layout

- Each argument uses POSIX single quotes. A single quote in a value becomes `'\''`.
- The command puts one argument on each line, and each line except the last ends with ` \`.
- The pipe form puts `printf`, `base64 -d`, and `curl` on separate lines, and the curl arguments
  keep the same layout.

```sh
curl -X PUT 'https://api.example.com/items/7' \
  -H 'Accept: application/json' \
  -H 'Content-Type: application/json' \
  --data-raw '{"name":"snorlax"}'

printf '%s' 'iVBORw0KGgo...' \
  | base64 -d \
  | curl 'https://api.example.com/upload' \
    -H 'Content-Type: image/png' \
    --data-binary @-
```

- The command works in bash, zsh, Git Bash, and WSL. A user of PowerShell or cmd must adjust the
  text.

### 7. VSCode surface

- A "Copy as curl" button sits beside the copy button of the Request section. It uses the
  feedback of the copy buttons of spec 0013.
- When `copyText` returns `None` for `curl`, the button is absent. The plain copy of the Request
  section still works.

### 8. Neovim surface

- `:FsHttp yank curl` puts the command in `v:register`.
- The Response buffer maps `yc` to `<Plug>(FsHttpYankCurl)`. `g?` lists the key.
- When there is no Curl command, the yank and `yc` give a WARN notice with the reason.
- This half depends on the Response buffer and the yank of spec 0015.

## Testing Decisions

### What makes a good test

A test asserts the text that reaches the clipboard or the register, and the bytes that a server
receives. It does not assert how the command is built.

### Golden fixtures

There is one Golden fixture for each rule. renderer.Tests writes each fixture, and the Lua core
suite reads it.

- **Method and URL:** `get-no-body`, `post-json-inline`, `get-with-body`, `head`, `put-no-body`, `url-glob-chars`
- **Headers:** `drops-content-length`, `body-no-content-type`, `value-single-quote`
- **Inline body:** `body-single-quote`, `body-leading-at`, `body-16384-bytes`
- **Base64 pipe:** `body-16385-bytes`, `body-crlf`, `body-multipart`, `body-nul`, `body-invalid-utf8`, `body-control-byte`
- **No command:** `not-captured`

### renderer.Tests

- The `curl` key of `copyText` for each fixture.
- **A replay test.** It runs the command of each Golden fixture with `sh` and `curl` against a
  local echo server. It compares the method, URL, headers, and body bytes that the server receives.
  The test runs on Linux and macOS, outside the Release gate. It adds no `curl` dependency to the
  Release gate on Windows.

### UI suite

1. Run the echo fixture, click Copy as curl, and read the clipboard.
2. A body that the companion did not read shows no Copy as curl button.

### Neovim suite

1. Run the echo fixture, run `:FsHttp yank curl` and `yc`, and read the unnamed register.
2. A body that the companion did not read gives the WARN notice.

### Prior art

- The `copyText` tests and the copy button Checks of spec 0013.

## Out of Scope

- A curl dialect for PowerShell or cmd. A later version can add one when a user asks.
- A setting for the command layout.
- A Curl command for a body that the companion did not read.

## Further Notes

- The VSCode half can start at once. The Neovim half waits for the Response buffer of spec 0015.
- No ADR conflict. ADR-0001 covers the button in the Response viewer, and ADR-0009 with its update
  covers the new Checks.
