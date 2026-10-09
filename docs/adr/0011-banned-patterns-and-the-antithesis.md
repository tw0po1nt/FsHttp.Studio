---
Status: accepted
---

# One pattern list for banned prose, and a ban on the antithesis and the em dash

`.banned-words` and `scripts/check-banned-words.sh` become `.banned-patterns` and
`scripts/check-banned-patterns.sh`. Every line of the list has an extended regular expression, a
tab, and the message a hit reports. A banned word is written `\bprovenance\b`, so a word is one
shape of pattern and the file needs one reader. `.claude/hooks/banned-patterns-check.sh` reads the
same file and denies a `Write` or an `Edit` that carries a hit.

The unification is what makes the two new prose rules enforceable. Both target a construct rather
than a word, and the old whole-word matcher could never see either one.

## The antithesis

`docs/standards/technical-prose.md` defines an **antithesis** as a sentence that states a fact and
then denies its opposite: `a backstop, not the mechanism`. The denial adds no information. It sets a
rhythm that a reader meets in every paragraph, and the rhythm tires that reader. The banned forms
are `X, not Y`, `not X but Y`, `not just`, `not merely`, `not simply`, `not only`, and the same
contrast split across two sentences. The sweep added `X, and not Y` and `X, and never Y`, because a
conjunction in front of the denial leaves the construct intact. That form appeared 82 times.

`rather than` and `instead of` stay legal. Each half of those comparisons carries information,
because the sentence names two real options and picks one.

No document is exempt, and an ADR title is bound as much as any other text. An ADR records a choice
between real alternatives, so the rejected option keeps a sentence of its own in the body, with the
reason. `0001` and `0005` carry the construct in their own titles today, and the sweep rewrites
them.

## The em dash

The em dash is banned. A comma, a colon, or a period does the same work. This repository had 307
em dashes across 52 files, which is the cost of the rule and also the argument for it.

## The three words that were kept

`harness` is the `CONTEXT.md` glossary term for the UI test harness, and rule 1 of
`docs/standards/coding-standards.md` requires that word. `solution` names `FsHttp.Studio.slnx`.
`richly` is the one-line description of what this extension does. `.banned-patterns` records all
three, so a later reader does not re-open the question.

## Rules 5 and 6 of the coding standards merge

The old rule 5 forbade a bare issue or PR number in source. The old rule 6 governed what a comment
may state. Both express one requirement: a comment speaks from where it stands. The merged rule 5
forbids every off-page citation, which now includes a spec path, an ADR number, and a file path.
The old rule 5 encouraged an in-repo reference, and that paragraph is deleted.

A citation makes a reader open a second document to learn what the line in front of them means, and
that reader pays the cost on every pass. A `TODO` keeps its full URL, because it points at work that
does not exist yet, so no fact on the page can stand for it.

## Alternatives that were rejected

Two separate scripts, one for words and one for patterns, duplicate the stripping rules and the skip
lists. Those two lists had already drifted apart once.

An exemption for `docs/spec/` would have saved 240 edits. It was considered and rejected. A rule
with a carve-out teaches a reader that the carve-out is where the rule stops.

An exemption for a contrast that is the subject of its sentence, such as an ADR title, needs a
judgment call, and no script can make one.

A `Stop` hook that reads the assistant's reply and forces a redraft was rejected. It fires after the
reader has already seen the text, and it taxes every turn of a session.

## The sweep

The antithesis and em dash patterns shipped inactive, because an active pattern failed CI against
520 hits that no one had rewritten. The sweep rewrote every hit and turned the patterns on. It also
found a second citation style that the merged rule 5 does not name in its list: a bare `Decision 7`,
`Seam 1`, or `user story 11`. Rule 5 leads with the principle that a comment cites nothing that
sends the reader off the page, so both styles are gone from `src/` and `tests/`.

## Accepted costs

The sweep also rewrites shipped product copy. `BlockLocator.classify` returns
`the binding's value is a lambda rather than the block`, which a user reads in a CodeLens toast. Spec 0003
pins it in a table, and the UI suite asserts it. That spec table is amended in the same commit as
the string, so the record and the code agree.

The check now reads F# string literals as well as `//` comment lines, because a shipped string is
prose that a user reads. A test fixture string that carries banned text is a new source of a CI
failure.

Six more shipped strings lose an em dash to a colon: the two capture reasons, the two status-bar
rows that report a syntax error, the binary-body note, and the lambda-value refusal. The specs that
pin them are amended in the same commit.
