# A Run evaluates everything above the target block and the block itself, and nothing after it

A Run evaluates the user's script from its first line to the end of the target block's own expression, and no further. Every enclosing `module`, `let`, and type header stays intact. Only the last line truncates, at the block's end column.

We considered two wider boundaries, and rejected both: the whole script, and the end of the target's enclosing top-level statement. Each leaves a blanked statement below the target. That statement must still compile. With either boundary, a side effect below the target also runs: a `printfn`, a file write, or a raw `HttpClient` call. A click on one block must not cause that side effect. The block's own expression end closes this gap at every nesting depth.

The boundary also removes a special case. A block piped directly to `Request.send` needs no routing branch of its own. The pipe sits after the block's own range, so the boundary drops it. Without this boundary, that shape sends two requests for one click.

## Considered Options

We surveyed five mechanisms to reach the block, and measured each one against a 12-case matrix of block positions:

- **Keep slicing the text before the block.** This reaches only a bare top-level expression.
- **Evaluate the script intact, and invoke a name derived from the AST.** This reaches only a module-level binding, and fails on a `private` one.
- **Evaluate intact, and synthesize a binding with an in-place text insertion.** This reaches a bare expression at any nesting depth.
- **Evaluate intact, and capture the block's value in place.** This reaches the most positions of any single mechanism. It fires twice on the piped case, and it turns "which block ran" into a runtime question.
- **Rewrite the AST, and reprint it with Fantomas.** This gives one uniform mechanism. It needs a second F# parser, and it risks the loss of comments.

We rejected text slicing outright: no block in the prototype's 32-block corpus routed to it. We chose the two routes that cover the rest between them, and we refuse the positions that neither route reaches.

## Consequences

R1 (the synthesized binding) and R2 (the derived name) are the current mechanism, and they are replaceable. The boundary above is the promise. When the routes change, a later ADR supersedes the mechanism, and cites this one for the boundary.

The Run decides routing from the untyped syntax tree alone. It needs no type-check, no project load, and no NuGet resolution. The reserved identifier ``` ``__fsHttpStudio_target`` ``` is a permanent name in the user's namespace, and the R1 route alone uses it. A user binding of the same name, in the same scope, gives a duplicate-definition error at the block's line. The Run does not avoid this collision. R1 also carries a column residue of +32 on the block's own line, which equals the width of the inserted text.

[ADR-0003](0003-block-location-in-companion.md) kept a tree-sitter fallback for block location "in reserve." That reserve is now worth less. Tree-sitter gives only ranges. Routing needs the syntax tree itself, to classify a block, to find the truncation point, and to compute its blank spans. This need increases the cost to reverse ADR-0003.

A Run cannot reach some positions without a type-check. A Run refuses these positions, and does not attempt them. See [#97](https://github.com/tw0po1nt/FsHttp.Studio/issues/97) for the policy that names them.

This decision replaces the Setup entry in `CONTEXT.md` and the matching statement in `README.md`. Both described the old text-slicing model.
