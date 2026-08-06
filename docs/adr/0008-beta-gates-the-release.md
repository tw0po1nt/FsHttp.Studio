# A Beta gates the release, and its version is never committed

FsHttp.Studio has surfaces that no suite can drive. `CodeLensProvider`, the warning toast, the
response viewer, and `Companion.fs` are VSCode and Node interop. Specs 0003 and 0004 both name their
own surface as *verified by hand*, and spec 0004 asked for the check in the pull request.

That obligation could not be met. No installable build existed while a pull request was open, so
[#139](https://github.com/tw0po1nt/FsHttp.Studio/issues/139) substituted a scripted check that drove
the compiled artifacts from node. The author recorded, correctly, that the response viewer was never
opened.

**A Beta now gates the release.** `beta.yml` builds `main`, runs the full CI gate, and publishes a
GitHub pre-release at `v<version>-beta.<n>` with the `.vsix` attached. An operator installs it, walks
[`docs/manual-check.md`](../manual-check.md), and records the result on the pre-release. `release.yml`
refuses a version that has no Beta, and a `force` input releases a change that needs no walk.

## The Beta version is synthesized, not committed

`package.json` holds the **target release version** for the whole cycle. `beta.yml` counts the
existing `v<version>-beta.*` tags, adds one, and stamps `npm version --no-git-tag-version` into the
checkout only. Nothing is committed, and nothing is pushed.

A future reader will find `package.json` at `0.2.0` beside a `v0.2.0-beta.3` tag, and will read that
as a mistake. It is not. Committing the Beta version was the alternative, and it fails on three
counts. `release.yml` reads `package.json`, so a committed Beta version would ship `0.2.0-beta.3`
unless somebody bumped it back. A Branch build has no version at all under that scheme. And cutting a
Beta becomes a commit, a push, and a dispatch, rather than one button.

The cost is real: `0.2.0-beta.3` is not reconstructible from the repository. The pre-release body
therefore records the commit that was built.

## A Branch build is not a Beta

`beta.yml` takes an optional `ref`. A non-empty `ref` produces a **Branch build**: the same `.vsix`,
stamped `<version>-<ref>.<run>`, uploaded as a workflow artifact, with no tag and no release.

A Branch build **skips the CI gate on purpose**. `ci.yml` already runs that gate on the pull request,
and running it again reports nothing new. A Branch build must reach an operator early. A build that
arrives only after the branch is green arrives too late.

## Considered and rejected

- **A build for every pull request.** It is the highest confidence, and it makes one maintainer the
  bottleneck on every merge. The `ref` input gives the same reach on demand.
- **A workflow artifact for the Beta too.** An artifact expires, and it needs an unzip. "I walked the
  check against `0.2.0-beta.3`" means something only while `0.2.0-beta.3` still exists.
- **A public beta channel.** The Marketplace pre-release channel imposes its odd-minor and even-minor
  scheme on every future version number. That is hard to reverse, and this project has no beta
  testers to serve yet. A GitHub pre-release is public, and deleting one costs nothing.

## Consequences

- The Releases page shows Betas. `prune-betas.yml` deletes the Beta *releases* for a version when
  that version is published, so the page stays readable. A superseded Beta also stops being an
  opportunity to install the wrong build.
- **The prune keeps the tags.** `gh release delete` runs without `--cleanup-tag`. A tag with no
  release does not appear on the Releases page, so the tag costs no readability. The tag is the
  only surviving record of the commit that a Beta was built from. The prune deletes the pre-release
  body, which carried the same record.
- Pruning costs no bisect that anybody needs. A Beta survives the whole window in which Beta
  granularity is useful, because the prune happens when the version ships. A report that arrives
  after the release names a release, and the first answer is to try the current release. A report
  that survives that answer is a defect, and a bisect across releases locates it. Beta granularity
  adds nothing at that point. The surviving tag also makes an exact rebuild cheap: `beta.yml` with
  `ref: v0.2.0-beta.3` reproduces that Beta. The prune therefore removes a built artifact, and not
  the means to make one.
- `main` has no by-hand verification between Betas. A Manual check that finds a regression looks at
  a batch of merged changes. The repair is a new commit, and not a change to an open pull request.
- The tripwire in `release.yml` proves that a Beta was **built**, and it cannot prove that anybody
  walked the checklist. No workflow can prove that.
