module Companion.BlockLocator


open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// A source range in FCS's own numbering: 1-based lines, 0-based columns.
type BlockRange =
    { StartLine: int
      StartCol: int
      EndLine: int
      EndCol: int }

/// One code per shape `classify` recognizes. The wire and the host borrow these twelve names
/// unchanged, and the F1 to F5 family labels stay internal.
/// F4 has no code here, because it is a Run outcome that the untyped tree cannot see.
type RefusalCode =
    /// F1. A `for`, `for .. in`, or `while` loop body.
    | LoopBody
    /// F1. An `if`/`elif`/`else` branch.
    | IfBranch
    /// F1. A `match` or `function` clause.
    | MatchClause
    /// F1. A `try`/`with` or `try`/`finally` handler.
    | ExceptionHandler
    /// F2. A function that takes arguments we would have to invent.
    | NeedsArguments
    /// F2. A class member: it needs an instance we would have to invent.
    | ClassMember
    /// F3. A binding that is not module-scoped: neither under a module `let` nor a member, or an
    /// inner `let`/`use`. Both shapes mean the same thing to a reader, so they share one code.
    | InnerBinding
    /// F3. A binding whose value is a lambda, not the block.
    | LambdaValue
    /// F3. A binding whose head pattern gives no single name to invoke.
    | NoNameToCall
    /// F5. A tuple binding: it binds several values, so its value is not the block.
    | TupleBinding
    /// F5. A block nested inside another located block's expression. Found by range containment
    /// over `locateBlocks`'s full output, and used only where the catch-all would otherwise fire.
    | InsideAnotherRequest
    /// F5. The catch-all for a shape `classify` has not enumerated, and what an unrecognized wire
    /// string degrades to at the host.
    | Unaddressable

/// Derived from the code alone, so two shapes that share a code cannot drift into two sentences
/// for one verdict. No sentence names an FCS type, and every sentence uses the glossary's block.
/// The host keys its lens titles and toasts on the code, and `Refusals.forCode` holds what a user
/// reads.
let reasonFor (code: RefusalCode) =
    match code with
    | LoopBody -> "a loop body describes many requests"
    | IfBranch -> "a branch is decided at runtime"
    | MatchClause -> "a match clause is decided at runtime"
    | ExceptionHandler -> "a handler is decided at runtime"
    | NeedsArguments -> "the function takes arguments we would have to invent"
    | ClassMember -> "a class member needs an instance we would have to invent"
    | InnerBinding -> "an inner binding is not reachable from a later FSI interaction"
    | LambdaValue -> "the binding's value is a lambda, not the block"
    | NoNameToCall -> "the binding does not give a name to invoke"
    | TupleBinding -> "a tuple binding binds several values, so its value is not the block"
    | InsideAnotherRequest -> "the block sits inside another block's own expression"
    | Unaddressable -> "the block sits in a position that a Run cannot reach"

/// The wire spelling of a refusal code, in camelCase. The host maps this string to a lens title
/// and a toast, and an unrecognized string degrades to `unaddressable`.
let codeToWire (code: RefusalCode) : string =
    match code with
    | LoopBody -> "loopBody"
    | IfBranch -> "ifBranch"
    | MatchClause -> "matchClause"
    | ExceptionHandler -> "exceptionHandler"
    | NeedsArguments -> "needsArguments"
    | ClassMember -> "classMember"
    | InnerBinding -> "innerBinding"
    | LambdaValue -> "lambdaValue"
    | NoNameToCall -> "noNameToCall"
    | TupleBinding -> "tupleBinding"
    | InsideAnotherRequest -> "insideAnotherRequest"
    | Unaddressable -> "unaddressable"

/// How a Run reaches a block, decided from the untyped syntax tree alone. The two routes differ
/// in where the invocation's name comes from: the Run supplies one, or the binding already has
/// one.
type Route =
    /// A bare expression statement, at any module depth. Nothing names the block, so the Run
    /// inserts `let <name> = ` immediately before it and invokes `<name>`. The spec calls this
    /// route R1.
    | NamedByTheRun
    /// The value of a module-level binding, at unit arity. The binding already names the block,
    /// so the Run invokes that name: `getSnorlax ()`. The spec calls this route R2.
    | NamedByTheBinding of invocation: string
    /// A position neither route reaches. The code is the whole verdict: `reasonFor` turns it into
    /// a sentence, and the host turns it into a lens title.
    | Refused of code: RefusalCode

/// The `locate` response's `refusal` property for a block's route: `None` for either route a Run
/// reaches, `Some` of the code's wire spelling for a refusal. A supported block's wire entry
/// omits the property entirely, and this is what decides which entries do.
let refusalOf (route: Route) : string option =
    match route with
    | Refused code -> Some(codeToWire code)
    | NamedByTheRun
    | NamedByTheBinding _ -> None

/// Keep the name `LocatedBlock`, because it is the glossary's word.
type LocatedBlock =
    {
        Block: BlockRange
        Route: Route
        /// The span to blank when this block is a *sibling* of the target. See `blankSpan`.
        Blank: BlockRange
        /// The enclosing module chain, outermost first: the invocation's qualifier. It covers the
        /// nested `module M =` declarations and the file's own `module M` header, if it has one.
        Qualifier: string list
        /// The `private` keyword spans to blank on this block's own path: its own binding's, and
        /// each enclosing module's. Empty when nothing on the path is `private`.
        PrivateSpans: BlockRange list
        /// The R2 target's own type annotation, colon included, when the binding has one.
        /// `None` on every other route. The Setup truncation can drop a trailing pipe, leaving
        /// an annotation that describes the untruncated value, so the blanking step removes it
        /// on the target's own binding only. The span starts after the head pattern (and any
        /// arguments), so blanking it keeps the bound name.
        TypeAnnotation: BlockRange option
        /// Every module-scoped name that blanking this block's `Blank` span takes away, spelled
        /// as the source writes it, minus any backticks. See `blankedNames`.
        BoundNames: string list
    }

/// Synthetic filename given to FCS's parser. Only the `.fsx` extension matters, because it
/// selects script-mode parsing. The source never exists on disk under this name.
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
/// others. The Setup boundary truncates at the block's own end, which drops such an
/// ancestor's trailing suffix intact — `http { } |> Request.send`'s pipe App starts at the
/// block, so truncation drops `|> Request.send` with no routing branch needed for case 12.
let private leadingAt (blockStart: pos) (node: SyntaxNode) =
    match node with
    | SyntaxNode.SynExpr e ->
        let r = e.Range
        samePos r.Start blockStart
    | _ -> false

/// R2's transparency is narrower than `leadingAt`: only a type annotation or parens may sit
/// between a binding and the block for the binding's *value* to be the block.
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

/// One identifier, spelled the way an invocation can name it. `Ident.idText` carries the *text*
/// of a name and never its backticks, so a name that is not a plain identifier — ``get pikachu``,
/// or a keyword such as ``type`` — comes back in a spelling that does not parse. Restore the
/// backticks that such a name needs. A plain identifier passes through unchanged, so the common
/// case reads exactly as the user wrote it.
let private invocationSpelling (id: Ident) =
    PrettyNaming.NormalizeIdentifierBackticks id.idText

/// The enclosing module chain, outermost first. `List.rev` turns the innermost-first order that
/// `path` walks in (from the block outward) into the outermost-first order the spec asks for.
let private qualifierOf (path: SyntaxNode list) =
    enclosingModules path
    |> List.map (fun (ids, _) -> ids |> List.map invocationSpelling |> String.concat ".")
    |> List.rev

let private accessRange (a: SynAccess option) =
    match a with
    | Some(SynAccess.Private r) -> [ r ]
    | _ -> []

/// Every `private` keyword on the target's own path that would put the invocation out of
/// reach: its own binding's, and each enclosing module's. Each invocation is a separate FSI
/// interaction, so a `private` binding — or a binding inside a `private` module — is not
/// accessible from it. `internal` needs no treatment, because an `internal` binding is
/// accessible from a later interaction, so it is deliberately not matched here.
let private privateSpansOn (ownBinding: SynAccess option) (path: SyntaxNode list) =
    accessRange ownBinding
    @ (enclosingModules path |> List.collect (snd >> accessRange))

/// `private` can sit on the binding *or* on its head pattern — `let private x = …` puts it on
/// `SynPat.Named`, not on `SynBinding.accessibility` — so a read of the binding alone misses it
/// silently.
let private patternAccess (headPat: SynPat) =
    match headPat with
    | SynPat.Named(accessibility = a)
    | SynPat.LongIdent(accessibility = a) -> a
    | _ -> None

/// The R2 target's own type annotation span, colon included: from the end of the head pattern
/// (and its arguments, if any) to the end of the type. Measured directly, because FCS's own
/// `SynBindingReturnInfo.Range` covers only the type name (`Response`, not `: Response`), so a
/// blank of that range alone would leave a bare `:` with nothing after it, which does not parse.
/// Starting from the head pattern's own end keeps the bound name untouched.
let private typeAnnotationSpan (headPat: SynPat) (returnInfo: SynBindingReturnInfo option) : range option =
    // `range` is a struct. An inline `headPat.Range.End` hits FS0052. Bind it first.
    let headPatRange = headPat.Range

    returnInfo
    |> Option.map (fun (SynBindingReturnInfo(range = r)) -> Range.mkRange r.FileName headPatRange.End r.End)

/// What a binding's head pattern offers an invocation. This is a *head-pattern* verdict, not a
/// refusal: `classify` turns `TakesArguments` into the `NeedsArguments` code and `NoName` into
/// `NoNameToCall`.
type private NameResult =
    | Invocable of string
    | TakesArguments
    | NoName

/// The name and arity a binding's head pattern offers for the NamedByTheBinding invocation. The
/// name is spelled for an invocation (`invocationSpelling`), so a backtick-quoted binding keeps
/// its backticks here and stays one term when `BlockRunner` qualifies it.
let private derivedName (headPat: SynPat) =
    match headPat with
    | SynPat.Named(ident = SynIdent(ident = id)) -> Invocable(invocationSpelling id)
    | SynPat.LongIdent(longDotId = SynLongIdent(id = ids); argPats = SynArgPats.Pats args) ->
        let name = ids |> List.map invocationSpelling |> String.concat "."

        match args with
        | [] -> Invocable name
        | [ SynPat.Paren(pat = SynPat.Const(constant = SynConst.Unit)) ]
        | [ SynPat.Const(constant = SynConst.Unit) ] -> Invocable(name + " ()")
        | _ -> TakesArguments
    | _ -> NoName

/// Everything the untyped path alone decides about one block. The three parts travel together
/// from `classify` to `LocatedBlock`. Two of them are options of the same shape, so a positional
/// tuple would let a transposition compile. Each part carries its own name instead.
/// `NoComparison` because FCS's `SynAccess` is not comparable, and nothing here compares.
[<NoComparison>]
type private Classification =
    {
        Route: Route
        /// The binding's own `SynAccess` when the block routes through a binding, for
        /// `privateSpansOn` to read.
        Access: SynAccess option
        /// The R2 target's own type annotation span. `None` on every other route.
        TypeAnnotation: range option
    }

/// The whole routing decision, from the untyped path alone. `path` is
/// innermost-first, as `ParsedInput.fold` builds it.
let private classify (blockStart: pos) (path: SyntaxNode list) : Classification =
    // A tuple must stay unconsumed here, or the two halves of `let a, b = ...` route differently.
    let rec skipLeading p =
        match p with
        | SyntaxNode.SynExpr(SynExpr.Tuple _) :: _ -> p
        | n :: rest when leadingAt blockStart n -> skipLeading rest
        | _ -> p

    let afterBoundary = skipLeading path

    match skipValueWrappers afterBoundary with
    | SyntaxNode.SynBinding(SynBinding(headPat = headPat; accessibility = access; returnInfo = returnInfo)) :: parents ->
        let access = if access.IsSome then access else patternAccess headPat

        let route =
            match parents with
            | SyntaxNode.SynModule(SynModuleDecl.Let _) :: _ ->
                match derivedName headPat with
                | Invocable invocation -> NamedByTheBinding invocation
                | TakesArguments -> Refused NeedsArguments
                | NoName -> Refused NoNameToCall
            | SyntaxNode.SynMemberDefn _ :: _
            | SyntaxNode.SynTypeDefn _ :: _ -> Refused ClassMember
            | _ -> Refused InnerBinding

        let typeAnnotation =
            match route with
            | NamedByTheBinding _ -> typeAnnotationSpan headPat returnInfo
            | _ -> None

        { Route = route
          Access = access
          TypeAnnotation = typeAnnotation }

    | _ ->
        let route =
            match afterBoundary with
            | SyntaxNode.SynModule(SynModuleDecl.Expr _) :: _ -> NamedByTheRun

            | SyntaxNode.SynExpr(SynExpr.Tuple _) :: _ -> Refused TupleBinding

            | SyntaxNode.SynExpr(SynExpr.ForEach _) :: _
            | SyntaxNode.SynExpr(SynExpr.For _) :: _
            | SyntaxNode.SynExpr(SynExpr.While _) :: _ -> Refused LoopBody
            | SyntaxNode.SynExpr(SynExpr.IfThenElse _) :: _ -> Refused IfBranch

            // This pair must precede the bare `SynMatchClause` below.
            | SyntaxNode.SynMatchClause _ :: SyntaxNode.SynExpr(SynExpr.TryWith _) :: _ -> Refused ExceptionHandler

            | SyntaxNode.SynMatchClause _ :: _
            | SyntaxNode.SynExpr(SynExpr.Match _) :: _
            | SyntaxNode.SynExpr(SynExpr.MatchLambda _) :: _ -> Refused MatchClause
            | SyntaxNode.SynExpr(SynExpr.TryWith _) :: _
            | SyntaxNode.SynExpr(SynExpr.TryFinally _) :: _ -> Refused ExceptionHandler
            | SyntaxNode.SynExpr(SynExpr.Lambda _) :: _ -> Refused LambdaValue
            | SyntaxNode.SynExpr(SynExpr.LetOrUse _) :: _ -> Refused InnerBinding
            | _ -> Refused Unaddressable

        { Route = route
          Access = None
          TypeAnnotation = None }

/// The smallest enclosing statement that can hold an expression. A module-level
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

/// Every name a head pattern binds into its enclosing scope, spelled as the source writes it.
/// `Ident.idText` carries the text without backticks, which is what an unbound-value diagnostic's
/// own range reads back to, so this is deliberately *not* `invocationSpelling`: these names are
/// matched against compiler output and shown to a user, never used to build an invocation.
///
/// A `SynPat.LongIdent`'s argument patterns are its parameters, which bind inside the binding and
/// not beside it, so the walk takes the head name alone and does not descend into them. That
/// treats a union-case destructure — `let Some x = …` — as binding `Some`, which is wrong but
/// inert: no diagnostic names a constructor as an unbound *value*.
let rec private headPatternNames (pat: SynPat) : string list =
    match pat with
    | SynPat.Named(ident = SynIdent(ident = id)) -> [ id.idText ]
    | SynPat.LongIdent(longDotId = SynLongIdent(id = ids)) ->
        match List.tryLast ids with
        | Some id -> [ id.idText ]
        | None -> []
    | SynPat.Typed(pat = inner)
    | SynPat.Paren(pat = inner)
    | SynPat.Attrib(pat = inner) -> headPatternNames inner
    | SynPat.As(lhsPat = l; rhsPat = r) -> headPatternNames l @ headPatternNames r
    | SynPat.Tuple(elementPats = pats)
    | SynPat.ArrayOrList(elementPats = pats)
    | SynPat.Ands(pats = pats) -> pats |> List.collect headPatternNames
    | SynPat.Record(fieldPats = fields) ->
        fields |> List.collect (fun (NamePatPairField(pat = p)) -> headPatternNames p)
    | SynPat.Or(lhsPat = l; rhsPat = r) -> headPatternNames l @ headPatternNames r
    | _ -> []

/// The names that blanking this block's span takes out of module scope, for `LocatedBlock`'s
/// `BoundNames`. It walks the same path `blankSpan` walks, and answers on the same branch: where
/// `blankSpan` erases a whole module-level `let`, the declaration's head pattern names go with
/// it, and everywhere else the blank keeps the head pattern and so removes no name.
///
/// The walk reaches the binding through any intervening expression node, so a tuple binding —
/// `let a, b = http { … }, http { … }`, which `classify` refuses before it ever sees the binding
/// — still reports both `a` and `b`. Blanking is what decides this, not routing.
let rec private blankedNames (path: SyntaxNode list) : string list =
    match path with
    | SyntaxNode.SynBinding(SynBinding(headPat = headPat)) :: parents ->
        match parents with
        | SyntaxNode.SynModule(SynModuleDecl.Let _) :: _ -> headPatternNames headPat
        | _ -> []
    | SyntaxNode.SynModule _ :: _ -> []
    | _ :: rest -> blankedNames rest
    | [] -> []

let private toBlockRange (r: range) : BlockRange =
    { StartLine = r.StartLine
      StartCol = r.StartColumn
      EndLine = r.EndLine
      EndCol = r.EndColumn }

/// True when `inner` sits within `outer`: at or after `outer`'s start, and at or before its end.
/// The bounds are inclusive, so a range contains *itself*. The call site excludes the block's own
/// range, because that is where the two ranges
/// being compared are known to belong to two different blocks.
let private contains (outer: BlockRange) (inner: BlockRange) =
    let startsAtOrBefore =
        outer.StartLine < inner.StartLine
        || (outer.StartLine = inner.StartLine && outer.StartCol <= inner.StartCol)

    let endsAtOrAfter =
        outer.EndLine > inner.EndLine
        || (outer.EndLine = inner.EndLine && outer.EndCol >= inner.EndCol)

    startsAtOrBefore && endsAtOrAfter

/// A target block whose range sits strictly inside another located block's range gets
/// `insideAnotherRequest` in place of the catch-all `unaddressable`.
/// Runs as a second pass, because it needs every block that `locateBlocks` found. A block that a
/// more specific branch already refused keeps that verdict.
let private markInsideAnotherRequest (blocks: LocatedBlock list) : LocatedBlock list =
    // A block always sits within its own range, so the identity test is what makes this strict.
    let sitsInsideAnother (block: LocatedBlock) =
        blocks
        |> List.exists (fun other -> not (obj.ReferenceEquals(other, block)) && contains other.Block block.Block)

    blocks
    |> List.map (fun block ->
        match block.Route with
        | Refused Unaddressable when sitsInsideAnother block ->
            { block with
                Route = Refused InsideAnotherRequest }
        | _ -> block)

/// `SynExpr.App.Range` covers `http { ... }`, which is the builder identifier and both braces.
/// It excludes any enclosing `let r =` binding, so it is exactly the span a later Run extracts.
let private findLocatedBlocks (ast: ParsedInput) : LocatedBlock list =
    (([], ast)
     ||> ParsedInput.fold (fun acc path node ->
         match node with
         | SyntaxNode.SynExpr(SynExpr.App(
             funcExpr = BuilderNamed "http"; argExpr = SynExpr.ComputationExpr _; range = appRange)) ->
             let classified = classify appRange.Start path

             { Block = toBlockRange appRange
               Route = classified.Route
               Blank = toBlockRange (blankSpan appRange path)
               Qualifier = qualifierOf path
               PrivateSpans = privateSpansOn classified.Access path |> List.map toBlockRange
               TypeAnnotation = classified.TypeAnnotation |> Option.map toBlockRange
               BoundNames = blankedNames path }
             :: acc
         | _ -> acc))
    |> List.rev
    |> markInsideAnotherRequest

let private parse (source: string) =
    let parsingOptions =
        { FSharpParsingOptions.Default with
            SourceFiles = [| syntheticFileName |]
            IsExe = false }

    checker.ParseFile(syntheticFileName, SourceText.ofString source, parsingOptions)
    |> Async.RunSynchronously

/// `ParseFailed` comes from `ParseHadErrors` rather than from a severity filter over
/// `Diagnostics`. The host's `blocks` envelope carries it as `parseFailed`.
type LocateResult =
    { Blocks: LocatedBlock list
      ParseFailed: bool }

/// Parses `source` as a `.fsx` script and returns every `http { }` block it finds, in source
/// order, with its route and its supporting spans, beside whether the parse failed. The parse
/// needs no project, no NuGet resolution, and no type-check, so an undefined `http` identifier
/// or an unresolved `#r` does not prevent location.
let locateBlocks (source: string) : LocateResult =
    let parsed = parse source

    { Blocks = parsed.ParseTree |> findLocatedBlocks
      ParseFailed = parsed.ParseHadErrors }

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
