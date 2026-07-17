module Companion.BlockLocator

// Finds `http { }` blocks in .fsx source via FCS's untyped-AST parse (ADR-0003, ADR-0004) —
// the same parse the companion will reuse for diagnostics once it exists. AST-based location
// is immune to the failure modes a textual/brace-counting scan hits: `http { }` text inside
// comments or strings is never matched, and an unbalanced brace inside a string literal (which
// desyncs a brace counter) does not affect a range read straight off the parse tree.

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// A source range using FCS's own numbering: 1-based lines, 0-based columns.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

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

/// `SynExpr.App.Range` covers `http { ... }` — builder ident and both braces — and excludes
/// any enclosing `let r =` binding, so it is exactly the span a later Run needs to extract.
let private findHttpBlocks (ast: ParsedInput) : range list =
    (([], ast)
     ||> ParsedInput.fold (fun acc _path node ->
         match node with
         | SyntaxNode.SynExpr(SynExpr.App(
             funcExpr = BuilderNamed "http"; argExpr = SynExpr.ComputationExpr _; range = appRange)) -> appRange :: acc
         | _ -> acc))
    |> List.rev

let private toBlockRange (r: range) : BlockRange =
    { StartLine = r.StartLine
      StartCol = r.StartColumn
      EndLine = r.EndLine
      EndCol = r.EndColumn }

/// Parses `source` as a `.fsx` script and returns the range of every `http { }` block found,
/// in source order. Parsing needs no project, NuGet resolution, or type-check, so an
/// undefined `http` identifier or an unresolved `#r` does not prevent location.
let locate (source: string) : BlockRange list =
    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| syntheticFileName |]
            IsExe = false }

    let parseResults =
        checker.ParseFile(syntheticFileName, SourceText.ofString source, parsingOptions)
        |> Async.RunSynchronously

    parseResults.ParseTree |> findHttpBlocks |> List.map toBlockRange
