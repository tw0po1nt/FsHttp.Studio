module Companion.BlockLocator

// Finds `http { }` blocks in .fsx source with FCS's untyped-AST parse (ADR-0003, ADR-0004).
// AST-based location avoids the failure modes of a textual brace-counting scan. It never
// matches `http { }` text inside a comment or a string, and an unbalanced brace inside a
// string literal does not affect a range that comes directly from the parse tree. That same
// brace desynchronizes a brace counter.

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// A source range in FCS's own numbering: 1-based lines, 0-based columns.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

/// A located block's own CE range, plus the range of the top-level statement that contains the
/// block. That statement is a `let` binding or a bare expression statement. Run needs the
/// statement range: if it blanks another block's bare CE span alone, that statement's own
/// trailing `|> Request.send` stays behind with nothing to pipe from.
type LocatedBlock =
    { Block: BlockRange
      Statement: BlockRange }

/// Synthetic filename given to FCS's parser. Only the `.fsx` extension matters, because it
/// selects script-mode parsing (ADR-0004). The source never exists on disk under this name.
[<Literal>]
let private syntheticFileName = "block-locator-input.fsx"

let private checker = FSharpChecker.Create()

/// Matches a CE builder identifier by name: the bare form (`http`), or the last segment of a
/// dotted form (`FsHttp.Dsl.http`). This mirrors FSAC's TestAdapter matcher.
let private (|BuilderNamed|_|) (name: string) (expr: SynExpr) =
    match expr with
    | SynExpr.Ident ident when ident.idText = name -> Some()
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
        match List.tryLast ids with
        | Some lastId when lastId.idText = name -> Some()
        | _ -> None
    | _ -> None

/// The nearest enclosing `SynModule` ancestor's range is the whole top-level statement. A
/// `let` declaration's range starts at the `let` keyword and ends at the end of its right-hand
/// side. A bare expression statement's range is the statement itself. In both cases the range
/// is exactly the span to blank, which removes one block's whole effect and not only its CE.
/// Falls back to the block's own range when no such ancestor exists. That case should not
/// occur for a top-level `.fsx` block, and the range is still usable as a no-op blank span.
let private enclosingStatementRange (path: SyntaxVisitorPath) (fallback: range) : range =
    path
    |> List.tryPick (function
        | SyntaxNode.SynModule decl -> Some decl.Range
        | _ -> None)
    |> Option.defaultValue fallback

/// `SynExpr.App.Range` covers `http { ... }`, which is the builder identifier and both braces.
/// It excludes any enclosing `let r =` binding, so it is exactly the span a later Run extracts.
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

/// Parses `source` as a `.fsx` script and returns every `http { }` block it finds, in source
/// order. Each block is paired with the range of its enclosing top-level statement. The parse
/// needs no project, no NuGet resolution, and no type-check, so an undefined `http` identifier
/// or an unresolved `#r` does not prevent location.
let locateBlocks (source: string) : LocatedBlock list =
    (parse source).ParseTree
    |> findLocatedBlocks
    |> List.map (fun (b, s) ->
        { Block = toBlockRange b
          Statement = toBlockRange s })

/// The range of every `http { }` block in `source`, in source order.
let locate (source: string) : BlockRange list =
    locateBlocks source |> List.map (fun lb -> lb.Block)

/// Reconstructs the exact source text that a range covers. Uses FCS's own numbering: 1-based
/// lines, 0-based columns.
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
