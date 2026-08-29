# UI screenshots: a change to the interface must show itself

A pull request that changes what the user sees must carry a screenshot of the change. This rule
has no exception. It applies to a human contributor and to an agent equally.

A reviewer cannot read a CSS rule and know what it paints. The renderer suite proves the shape of
the DOM. It does not prove that a button is visible, that a margin is correct, or that a color has
enough contrast. Only a picture proves that.

## What counts as a change to the interface

A change to one of these paths changes what the user sees:

- `src/renderer/` — the markup of the response viewer
- `src/host/ResponseViewer.fs` — the styles and the shell of the panel
- `src/webview/` — the mount glue and the scripts of the panel

A change to a CodeLens title, a notification, a status bar item, or an icon also counts.

A change that only moves code, renames a binding, or edits a comment does not count. State that in
the pull request when the paths above appear in the diff for one of those reasons.

## What the screenshot must show

- Capture the running editor. A mock page or a browser render of the markup is not acceptable,
  because the VSCode theme variables fall back to different colors outside the panel.
- Show the state that the change alters. A change to a collapsed section must show that section
  collapsed.
- Add a second image for a change to an existing surface, so the reviewer sees the state before
  the change and the state after it.
- Crop to the surface under review. A full editor window hides the change in noise.

## How to capture one

The UI suite already drives a real VSCode window through ExTester, and ExTester saves a screenshot
on request. Use it, and remove the capture code afterward.

1. Write a module in `tests/ui.Tests/` that drives the editor to the state you must show.
   `RequestSectionTests.fs` is the shortest model: it opens a fixture, clicks the Run lens, and
   waits for the viewer.
2. Call `ExTester.VSBrowser.instance.takeScreenshot "<name>"` at that state. `open Fable.Core`
   makes `Async.AwaitPromise` available.
3. Add the module to `Ui.Tests.fsproj` and to `Main.fs`. Reduce the test list in `Main.fs` to your
   module alone, so the run takes seconds instead of minutes.
4. Run `./tests/ui.Tests/run.sh`. The images land in
   `$STORAGE/screenshots/<timestamp>/<name>.png`, where `STORAGE` defaults to
   `/tmp/fshttp-ui-test-resources`.
5. Revert `Main.fs` and `Ui.Tests.fsproj`, and delete your module. The capture code must never
   reach the main line.

Take a second screenshot only after the editor repaints. Two captures in one test can return the
same frame, and two identical files prove one state, not two.

## Where the image lives

Commit the image to `docs/screenshots/` on the feature branch. `.vscodeignore` excludes `docs/**`,
so the image never ships inside the `.vsix`.

Name the file `<issue>-<subject>.png`, for example `210-copy-buttons.png`. Reference it from the
pull request body by its raw URL, and pin that URL to a commit:

```markdown
![Both header sections collapsed, each showing its copy button](https://raw.githubusercontent.com/tw0po1nt/FsHttp.Studio/<commit>/docs/screenshots/210-copy-buttons.png)
```

Push the image first, and then read the commit with `git rev-parse HEAD`.

**A branch name in this URL is a broken link later.** This repository allows a squash merge only,
so the commits of a feature branch never reach the main line. The branch itself survives the merge,
because `deleteBranchOnMerge` is off, but a later cleanup removes it. Every link that names that
branch then fails, inside a pull request that is already part of the record. A commit does not move.

After the merge, the image is also on the main line at the same path, so a reader can still find it
by name.

The `gh` command line cannot upload an image to a pull request, because that endpoint needs a
browser session. A committed image is therefore the only route an agent can take alone.

## The hook is a backstop, not the mechanism

A `PreToolUse` hook (`.claude/settings.json`) fires before a `gh pr create` or a `gh pr edit`
command. It reads the paths that the branch changed. It injects a reminder only when those paths
include the interface. The hook cannot verify that a screenshot exists, and it cannot judge what
the screenshot shows.

The rule above is still the requirement. The hook exists because an agent that has finished the
code reads a pull request body as the last small step, and skips the picture.
