module Companion.BlockLocator

// Finds `http { }` blocks in .fsx source with FCS's untyped-AST parse (ADR-0003, ADR-0004).
// AST-based location avoids the failure modes of a textual brace-counting scan. It never
// matches `http { }` text inside a comment or a string, and an unbalanced brace inside a
// string literal does not affect a range that comes directly from the parse tree. That same
// brace desynchronizes a brace counter.
//
// This module also classifies each block's Route: how a Run would reach it, from the untyped
// syntax tree alone (docs/spec/0002-reach-a-block-anywhere.md, Decision 2). Classification
// needs no type-check, no project load, and no NuGet resolution, so it is decidable from a
// bare parse.

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// A source range in FCS's own numbering: 1-based lines, 0-based columns.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

/// The refusal families the spec's Decision 2 table names. F4 is not here: it is a Run
/// outcome, not a classify verdict (position 11c: the untyped tree cannot know that a Run
/// blanked away the name a block depends on).
type RefusalFamily =
    /// The position is decided at run time: a loop body, an `if` branch, a `match` clause, a
    /// `try`/`with` handler.
    | F1
    /// We would have to invent a value: a function with arguments, a class member.
    | F2
    /// The position is not module-scoped: an inner `let`, a lambda-valued binding.
    | F3
    /// The binding's value is not the block: a tuple binding, a block inside another block's
    /// expression.
    | F5

/// How a Run reaches a block, decided from the untyped syntax tree alone.
type Route =
    /// A bare expression statement, at any module depth. The Run inserts `let <name> = `
    /// immediately before the block and invokes `<name>`.
    | R1
    /// The value of a module-level binding, at unit arity. `Invocation` is the binding's
    /// derived name, e.g. `getSnorlax ()`.
    | R2 of invocation: string
    /// A position neither route reaches. `reason` is a plain sentence, safe to show a user;
    /// it must not interpolate an FCS type name (Decision 11).
    | Refused of family: RefusalFamily * reason: string

/// A located block's own CE range, the route a Run takes to reach it, the span to blank when
/// it is *not* the target, its enclosing-module qualifier, and its `private` keyword spans.
/// Keep the name `LocatedBlock`: it is the glossary's word (docs/spec/0002, Decision 3).
type LocatedBlock =
    {
        Block: BlockRange
        Route: Route
        /// The span to blank when this block is a *sibling* of the target. A module-level
        /// binding blanks its whole declaration (keeps the value-leakage guard). A member or
        /// inner binding blanks its right side only, because a blanked `member _.Get() = …`
        /// leaves a type with no members, and that does not parse.
        Blank: BlockRange
        /// The enclosing module chain, outermost first: the invocation's qualifier. It covers the
        /// nested `module M =` declarations and the file's own `module M` header, if it has one.
        Qualifier: string list
        /// The `private` keyword spans to blank on this block's own path: its own binding's, and
        /// each enclosing module's. Empty when nothing on the path is `private`.
        PrivateSpans: BlockRange list
    }

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

let private samePos (a: pos) (b: pos) = a.Line = b.Line && a.Column = b.Column

/// True when `node` is a leading ancestor of the block: an expression whose own range starts
/// exactly where the block starts, so the block is its leading part and not one argument among
/// others. The Setup boundary (Decision 1) truncates at the block's own end, which drops such an
/// ancestor's trailing suffix intact — `http { } |> Request.send`'s pipe App starts at the
/// block, so truncation drops `|> Request.send` with no routing branch needed for case 12.
let private leadingAt (blockStart: pos) (node: SyntaxNode) =
    match node with
    | SyntaxNode.SynExpr e ->
        let r = e.Range
        samePos r.Start blockStart
    | _ -> false

/// R2's transparency is narrower than `leadingAt`: only a type annotation or parens may sit
/// between a binding and the block for the binding's *value* to be the block (Decision 2).
/// Returns the remaining path once every such wrapper is consumed. A parenthesis starts at its
/// own `(`, before the block, so `leadingAt` never reaches one — this is the only thing that
/// does. It stays narrow on purpose: R1 inserts `let <name> = ` at the block's own start, and a
/// parenthesis between the statement and the block would put that insertion inside the parens.
let rec private skipValueWrappers (path: SyntaxNode list) =
    match path with
    | SyntaxNode.SynExpr(SynExpr.Typed _) :: rest
    | SyntaxNode.SynExpr(SynExpr.Paren _) :: rest -> skipValueWrappers rest
    | rest -> rest

/// Every enclosing module on the block's path, innermost first, with the module's own
/// accessibility. A nested `module M =` is one, and so is the file's own `module M` header,
/// which puts the same name in front of the invocation and can carry the same `private`. A
/// script with no header parses as an anonymous module, and a namespace holds no bindings, so
/// neither is one.
let private enclosingModules (path: SyntaxNode list) =
    path
    |> List.choose (function
        | SyntaxNode.SynModule(SynModuleDecl.NestedModule(
            moduleInfo = SynComponentInfo(longId = ids; accessibility = access))) -> Some(ids, access)
        | SyntaxNode.SynModuleOrNamespace(SynModuleOrNamespace(
            longId = ids; kind = SynModuleOrNamespaceKind.NamedModule; accessibility = access)) -> Some(ids, access)
        | _ -> None)

/// The enclosing module chain, outermost first. `List.rev` turns the innermost-first order that
/// `path` walks in (from the block outward) into the outermost-first order the spec asks for.
let private qualifierOf (path: SyntaxNode list) =
    enclosingModules path
    |> List.map (fun (ids, _) -> ids |> List.map (fun i -> i.idText) |> String.concat ".")
    |> List.rev

let private accessRange (a: SynAccess option) =
    match a with
    | Some(SynAccess.Private r) -> [ r ]
    | _ -> []

/// Every `private` keyword on the target's own path that would put the invocation out of
/// reach: its own binding's, and each enclosing module's. Each invocation is a separate FSI
/// interaction, so a `private` binding — or a binding inside a `private` module — is not
/// accessible from it (Decision 6). `internal` needs no treatment: an `internal` binding is
/// accessible from a later interaction, so it is deliberately not matched here.
let private privateSpansOn (ownBinding: SynAccess option) (path: SyntaxNode list) =
    accessRange ownBinding
    @ (enclosingModules path |> List.collect (snd >> accessRange))

/// `private` can sit on the binding *or* on its head pattern — `let private x = …` puts it on
/// `SynPat.Named`, not on `SynBinding.accessibility` — so a read of the binding alone misses it
/// silently (Decision 6's first trap).
let private patternAccess (headPat: SynPat) =
    match headPat with
    | SynPat.Named(accessibility = a)
    | SynPat.LongIdent(accessibility = a) -> a
    | _ -> None

type private NameResult =
    | Invocable of string
    | NeedsArguments
    | NoName

/// The name and arity a binding's head pattern offers for the R2 invocation.
let private derivedName (headPat: SynPat) =
    match headPat with
    | SynPat.Named(ident = SynIdent(ident = id)) -> Invocable id.idText
    | SynPat.LongIdent(longDotId = SynLongIdent(id = ids); argPats = SynArgPats.Pats args) ->
        let name = ids |> List.map (fun i -> i.idText) |> String.concat "."

        match args with
        | [] -> Invocable name
        | [ SynPat.Paren(pat = SynPat.Const(constant = SynConst.Unit)) ]
        | [ SynPat.Const(constant = SynConst.Unit) ] -> Invocable(name + " ()")
        | _ -> NeedsArguments
    | _ -> NoName

/// The whole routing decision, from the untyped path alone (Decision 2). `path` is
/// innermost-first, as `ParsedInput.fold` builds it. Returns the route, plus the binding's own
/// `SynAccess` when the block routes through a binding, for `privateSpansOn` to read.
let private classify (blockStart: pos) (path: SyntaxNode list) : Route * SynAccess option =
    // Consume the ancestors the Setup boundary (Decision 1) will drop. A tuple is deliberately
    // *not* consumed: for its first element the range coincides with the block, and for its
    // second it does not, so letting that asymmetry through would route the two halves of
    // `let a, b = http { }, http { }` differently. One shape gets one verdict.
    let rec skipLeading p =
        match p with
        | SyntaxNode.SynExpr(SynExpr.Tuple _) :: _ -> p
        | n :: rest when leadingAt blockStart n -> skipLeading rest
        | _ -> p

    let afterBoundary = skipLeading path

    // Look for an R2 binding through the wrappers R2 alone tolerates. Every other position
    // classifies from `afterBoundary`, so a parenthesis stays opaque everywhere but here.
    match skipValueWrappers afterBoundary with
    | SyntaxNode.SynBinding(SynBinding(headPat = headPat; accessibility = access)) :: parents ->
        // Reaching a binding through `skipValueWrappers` is what makes the block the binding's
        // value. What is left to decide is whether the binding is module-level: an inner `let`
        // or a member is out of reach from a later FSI interaction.
        let access = if access.IsSome then access else patternAccess headPat

        match parents with
        | SyntaxNode.SynModule(SynModuleDecl.Let _) :: _ ->
            match derivedName headPat with
            | Invocable invocation -> R2 invocation, access
            | NeedsArguments -> Refused(F2, "the function takes arguments we would have to invent"), access
            // A wildcard or a destructuring pattern binds no single name, so there is nothing to
            // invoke and no value the invocation could name. The spec's table has no row for this
            // shape; F5 is its nearest family, beside the tuple binding it resembles.
            | NoName -> Refused(F5, "the binding does not give a name to invoke"), access
        | SyntaxNode.SynMemberDefn _ :: _
        | SyntaxNode.SynTypeDefn _ :: _ ->
            Refused(F2, "a class member needs an instance we would have to invent"), access
        | _ -> Refused(F3, "an inner binding is not reachable from a later FSI interaction"), access

    | _ ->
        match afterBoundary with
        | SyntaxNode.SynModule(SynModuleDecl.Expr _) :: _ -> R1, None

        | SyntaxNode.SynExpr(SynExpr.Tuple _) :: _ ->
            Refused(F5, "a tuple binding binds several values, so its value is not the block"), None

        | SyntaxNode.SynExpr(SynExpr.ForEach _) :: _
        | SyntaxNode.SynExpr(SynExpr.For _) :: _
        | SyntaxNode.SynExpr(SynExpr.While _) :: _ -> Refused(F1, "a loop body describes many requests"), None
        | SyntaxNode.SynExpr(SynExpr.IfThenElse _) :: _ -> Refused(F1, "a branch is decided at runtime"), None
        | SyntaxNode.SynMatchClause _ :: _
        | SyntaxNode.SynExpr(SynExpr.Match _) :: _
        | SyntaxNode.SynExpr(SynExpr.MatchLambda _) :: _ -> Refused(F1, "a match clause is decided at runtime"), None
        | SyntaxNode.SynExpr(SynExpr.TryWith _) :: _
        | SyntaxNode.SynExpr(SynExpr.TryFinally _) :: _ -> Refused(F1, "a handler is decided at runtime"), None
        | SyntaxNode.SynExpr(SynExpr.Lambda _) :: _ ->
            Refused(F3, "the binding's value is a lambda, not the block"), None
        | SyntaxNode.SynExpr(SynExpr.LetOrUse _) :: _ ->
            Refused(F3, "an inner let is not reachable from a later FSI interaction"), None
        | _ -> Refused(F5, "the block sits inside another expression, so its value is not the block"), None

/// The smallest enclosing statement that can hold an expression (Decision 5). A module-level
/// binding blanks its whole declaration, which keeps the value-leakage protection: the binding
/// disappears, and a consumer fails with a clean "not defined" instead of leaking a value. A
/// member or inner binding blanks its right side only, because erasing `member _.Get() = …`
/// entirely leaves a type with no members and does not parse. Falls back to the block's own
/// range when no enclosing statement exists, which should not occur for a block that FCS parsed
/// out of a `.fsx` file.
let rec private blankSpan (blockRange: range) (path: SyntaxNode list) =
    match path with
    | SyntaxNode.SynBinding(SynBinding(expr = rhs)) :: parents ->
        match parents with
        | SyntaxNode.SynModule(SynModuleDecl.Let(range = r)) :: _ -> r
        | _ -> rhs.Range
    | SyntaxNode.SynModule(decl) :: _ -> decl.Range
    | _ :: rest -> blankSpan blockRange rest
    | [] -> blockRange

let private toBlockRange (r: range) : BlockRange =
    { StartLine = r.StartLine
      StartCol = r.StartColumn
      EndLine = r.EndLine
      EndCol = r.EndColumn }

/// `SynExpr.App.Range` covers `http { ... }`, which is the builder identifier and both braces.
/// It excludes any enclosing `let r =` binding, so it is exactly the span a later Run extracts.
let private findLocatedBlocks (ast: ParsedInput) : LocatedBlock list =
    (([], ast)
     ||> ParsedInput.fold (fun acc path node ->
         match node with
         | SyntaxNode.SynExpr(SynExpr.App(
             funcExpr = BuilderNamed "http"; argExpr = SynExpr.ComputationExpr _; range = appRange)) ->
             let route, access = classify appRange.Start path

             { Block = toBlockRange appRange
               Route = route
               Blank = toBlockRange (blankSpan appRange path)
               Qualifier = qualifierOf path
               PrivateSpans = privateSpansOn access path |> List.map toBlockRange }
             :: acc
         | _ -> acc))
    |> List.rev

let private parse (source: string) =
    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| syntheticFileName |]
            IsExe = false }

    checker.ParseFile(syntheticFileName, SourceText.ofString source, parsingOptions)
    |> Async.RunSynchronously

/// Parses `source` as a `.fsx` script and returns every `http { }` block it finds, in source
/// order, with its route and its supporting spans. The parse needs no project, no NuGet
/// resolution, and no type-check, so an undefined `http` identifier or an unresolved `#r` does
/// not prevent location.
let locateBlocks (source: string) : LocatedBlock list =
    (parse source).ParseTree |> findLocatedBlocks

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
