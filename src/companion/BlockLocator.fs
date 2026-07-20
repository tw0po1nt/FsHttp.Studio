module Companion.BlockLocator

// Finds `http { }` blocks in .fsx source via FCS's untyped-AST parse (ADR-0003, ADR-0004).
// AST-based location is immune to the failure modes a textual/brace-counting scan hits:
// `http { }` text inside comments or strings is never matched, and an unbalanced brace inside
// a string literal (which desyncs a brace counter) does not affect a range read straight off
// the parse tree.

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// A source range using FCS's own numbering: 1-based lines, 0-based columns.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

/// A located block's own CE range, plus the range of the top-level statement that contains
/// it (a `let` binding or a bare expression statement). Run needs the latter: blanking out
/// another block's bare CE span alone would leave that statement's own trailing
/// `|> Request.send`/`|> ...` dangling with nothing to pipe from.
type LocatedBlock =
    { Block: BlockRange
      Statement: BlockRange }

/// Synthetic filename fed to FCS's parser. Only its `.fsx` extension matters — it selects
/// script-mode parsing (ADR-0004); the source never actually lives on disk under this name.
[<Literal>]
let private syntheticFileName = "block-locator-input.fsx"

let private checker = FSharpChecker.Create()

/// Matches a CE builder identifier by name — its bare form (`http`) or the last segment of a
/// dotted one (`FsHttp.Dsl.http`), mirroring FSAC's TestAdapter matcher.
let private (|BuilderNamed|_|) (name: string) (expr: SynExpr) =
    match expr with
    | SynExpr.Ident ident when ident.idText = name -> Some()
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
        match List.tryLast ids with
        | Some lastId when lastId.idText = name -> Some()
        | _ -> None
    | _ -> None

/// The nearest enclosing `SynModule` ancestor's range is the whole top-level statement — a
/// `let` decl's range starts at the `let` keyword and runs to the end of its RHS, and a bare
/// expression statement's range is the statement itself — in both cases exactly the span that
/// must be blanked to remove one block's entire effect, not just its CE. Falls back to the
/// block's own range if no such ancestor exists (shouldn't happen for a top-level `.fsx`
/// block, but the range is still usable as a no-op blank span if it ever does).
let private enclosingStatementRange (path: SyntaxVisitorPath) (fallback: range) : range =
    path
    |> List.tryPick (function
        | SyntaxNode.SynModule decl -> Some decl.Range
        | _ -> None)
    |> Option.defaultValue fallback

/// `SynExpr.App.Range` covers `http { ... }` — builder ident and both braces — and excludes
/// any enclosing `let r =` binding, so it is exactly the span a later Run needs to extract.
let private findLocatedBlocks (ast: ParsedInput) : (range * range) list =
    (([], ast)
     ||> ParsedInput.fold (fun acc path node ->
         match node with
         | SyntaxNode.SynExpr(SynExpr.App(
             funcExpr = BuilderNamed "http"; argExpr = SynExpr.ComputationExpr _; range = appRange)) ->
             (appRange, enclosingStatementRange path appRange) :: acc
         | _ -> acc))
    |> List.rev

let private toBlockRange (r: range) : BlockRange =
    { StartLine = r.StartLine
      StartCol = r.StartColumn
      EndLine = r.EndLine
      EndCol = r.EndColumn }

let private parse (source: string) =
    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| syntheticFileName |]
            IsExe = false }

    checker.ParseFile(syntheticFileName, SourceText.ofString source, parsingOptions)
    |> Async.RunSynchronously

/// Parses `source` as a `.fsx` script and returns every `http { }` block found, in source
/// order, each paired with the range of its enclosing top-level statement. Parsing needs no
/// project, NuGet resolution, or type-check, so an undefined `http` identifier or an
/// unresolved `#r` does not prevent location.
let locateBlocks (source: string) : LocatedBlock list =
    (parse source).ParseTree
    |> findLocatedBlocks
    |> List.map (fun (b, s) ->
        { Block = toBlockRange b
          Statement = toBlockRange s })

/// The range of every `http { }` block found in `source`, in source order.
let locate (source: string) : BlockRange list =
    locateBlocks source |> List.map (fun lb -> lb.Block)

/// Reconstructs the exact source text a range covers, using FCS's own numbering (1-based
/// lines, 0-based columns).
let sliceRange (source: string) (r: BlockRange) : string =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    if r.StartLine = r.EndLine then
        lines.[r.StartLine - 1].Substring(r.StartCol, r.EndCol - r.StartCol)
    else
        let sb = System.Text.StringBuilder()
        sb.Append(lines.[r.StartLine - 1].Substring(r.StartCol)) |> ignore

        for i in r.StartLine .. r.EndLine - 2 do
            sb.Append('\n').Append(lines.[i]) |> ignore

        sb.Append('\n').Append(lines.[r.EndLine - 1].Substring(0, r.EndCol)) |> ignore
        sb.ToString()
