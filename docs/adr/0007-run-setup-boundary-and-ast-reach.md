# A Run evaluates everything above the target block and the block itself, and nothing after it

A Run evaluates the user's script from its first line to the end of the target block's own expression, and no further. Every enclosing `module`, `let`, and type header stays intact. Only the last line truncates, at the block's end column.

We considered two wider boundaries, and rejected both: the whole file, and the end of the target's enclosing top-level statement. Each leaves a blanked statement below the target. That statement must still compile. Each also lets a side effect after the target run: a `printfn`, a file write, or a raw `HttpClient` call. A click on one block must not cause that side effect. The block's own expression end closes this gap at every nesting depth. It also removes a special case. A block piped directly to `Request.send` needs no routing branch of its own, because the pipe sits after the block's own range, so the boundary drops it. Without this boundary, that shape sends two requests for one click.

## Considered Options

We surveyed five mechanisms for reaching the block, on a 32-block corpus:

- **Keep slicing the text before the block.** This reaches only a bare top-level expression.
- **Evaluate the script intact, and invoke a name derived from the AST.** This reaches only a module-level binding, and fails on a `private` one.
- **Evaluate intact, and synthesize a binding with an in-place text insertion.** This reaches a bare expression at any nesting depth.
- **Evaluate intact, and capture the block's value in place.** This reaches the most positions of any single mechanism. It fires twice on the piped case, and it turns "which block ran" into a runtime question.
- **Rewrite the AST, and reprint it with Fantomas.** This gives one uniform mechanism. It needs a second F# parser, and it risks the loss of comments.

We rejected text slicing outright: no block in the 32-block corpus routed to it. We chose the two routes that cover the rest between them, and refuse what neither reaches.

## Consequences

R1 (the synthesized binding) and R2 (the derived name) are the current mechanism, and they are replaceable. The boundary above is the promise. When the routes change, a later ADR supersedes the mechanism, and cites this one for the boundary.

The Run decides routing from the untyped syntax tree alone. It needs no type-check, no project load, and no NuGet resolution. The reserved identifier `` `__fsHttpStudio_target` `` is a permanent name in the user's namespace. The R1 route alone uses it. A user binding of the same name, in the same scope, gives a duplicate-definition error at the block's line. The Run does not avoid this collision. R1 also carries a column residue of +32 on the block's own line. This residue equals the width of the inserted text.

ADR-0003 kept a tree-sitter fallback for block location "in reserve." That reserve now buys less. Tree-sitter yields only ranges. Routing needs the syntax tree itself, to classify a block and to compute its blank spans. This need raises the cost of a reversal of ADR-0003.

Some positions are not reachable without a type-check. A Run refuses these positions, and does not attempt them. See #97 for the policy that names them.

This decision replaces the Setup entry in `CONTEXT.md` and the matching statement in `README.md`. Both described the old text-slicing model.
