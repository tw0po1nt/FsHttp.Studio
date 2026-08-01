# Domain Docs

How the engineering skills use this repo's domain documentation when they explore the codebase.

## Read these before you explore

- **`CONTEXT.md`** at the repo root.
- **`docs/adr/`**. Read the ADRs that touch the area you will work in.

If one of these files does not exist, **continue silently**. Do not report the absence, and do not suggest that someone creates the file first. The `/domain-modeling` skill creates these files lazily, when terms or decisions are resolved. `/grill-with-docs` and `/improve-codebase-architecture` reach that skill.

## File structure

This repo has one context:

```
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-in-editor-webview-renderer.md
│   └── 0002-fcs-companion-framed-envelope.md
└── src/
```

## Use the glossary's vocabulary

When your output names a domain concept, use the term as `CONTEXT.md` defines it. This applies to an issue title, a refactor proposal, a hypothesis, and a test name. Do not drift to a synonym that the glossary avoids.

If the concept you need is not yet in the glossary, that is a signal. Either you invent language that the project does not use, and you must reconsider, or there is a real gap, and you must note it for `/domain-modeling`.

## Flag ADR conflicts

If your output contradicts an existing ADR, report the contradiction explicitly. Do not override the ADR silently:

> _Contradicts ADR-0003 (block location in the companion), but worth reopening because…_
