# Manual check

The by-hand walk of FsHttp.Studio in a real VSCode window.

Some surfaces carry no test. `CodeLensProvider`, the warning toast, the response viewer, and
`Companion.fs` are VSCode and Node interop. [ADR-0003](./adr/0003-block-location-in-companion.md)
puts the testable logic behind those surfaces, and the suites cover it there. The surfaces
themselves are covered here, by a person.

A spec that finds a new untestable surface adds its item to this file. A spec must not strand the
instruction in its own `What is not tested here` section, because nobody reads a shipped spec again.

## When to walk this file

Walk this file against a Beta, before you release the version that the Beta is a candidate for.
`release.yml` refuses to release a version that has no Beta.

You can also walk it against a Branch build, when a pull request changes one of these surfaces.
That check is useful evidence, and it is not a requirement.

## Prepare

1. Cut a Beta. Open the **Beta** workflow, then click **Run workflow** with an empty `ref`.
2. Open the pre-release that the workflow published.
3. Download `fshttp-studio-<version>.vsix`.
4. Install it. Run `code --install-extension fshttp-studio-<version>.vsix`.
5. Reload the VSCode window.
6. Open a `.fsx` script that contains at least one `http { }` block.

## The core path

1. Confirm that `▶ Run request` appears above each block.
2. Click the lens on a block that requests a JSON resource.
3. Confirm that the response viewer opens beside the editor.
4. Confirm that the viewer shows `Running…` while the Run is in flight.
5. Confirm that the viewer renders the response body and the status code.
6. Click the lens on a second block in the same script.
7. Confirm that the viewer renders the second response, and not the first.

## Run outcomes

1. Run a block that requests a resource which answers 404.
2. Confirm that the viewer renders the body and the status code, and reports no failure.
3. Add a type error to the script, above the block.
4. Run the block, and confirm that the viewer reports the error at its source location.
5. Remove the type error.
6. Point a block at a port that nothing listens on.
7. Run the block, and confirm that the viewer renders the error as plain text.

## The lens tells the truth

Covers [spec 0003](./spec/0003-lens-tells-the-truth.md). No suite drives the lens surface or the
toast.

1. Put a block inside a `for` loop.
2. Confirm that its lens reads `⊘ Cannot run: inside a loop`.
3. Click that lens.
4. Confirm that a warning toast states the reason and the workaround.
5. Confirm that no response viewer opens.
6. Bind a value in one block, then use that value in a second block.
7. Run the second block, and confirm that the viewer reports a Refused Run.
8. Confirm that the viewer marks no fault in the script.

## The companion stops

Covers [spec 0004](./spec/0004-run-path-robustness.md), Decision 6. `Companion.fs` is Fable and Node
interop, and no suite drives it.

1. Start a Run against a slow server.
2. Kill the companion process while the Run is in flight.
3. Confirm that the viewer leaves `Running…`.
4. Confirm that the viewer reports: `The FsHttp.Studio companion stopped. Reload the window to start
   it again.`
5. Click the lens again.
6. Confirm that the viewer reports the same message immediately.
7. Reload the window, and confirm that a Run succeeds again.

## Record the result

Comment on the pre-release. State the version you walked, and the result of each section.

Report a defect as an issue, and link the issue from the comment. Do not release a version whose
Manual check found a defect on the core path.
