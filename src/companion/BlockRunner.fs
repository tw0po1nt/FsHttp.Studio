module Companion.BlockRunner

// This module must never reference FsHttp. The user's own `#r "nuget:"` resolves the package,
// so their version pin always wins.

open System
open System.Diagnostics
open System.Net.Http
open System.Text.Json
open System.Text.RegularExpressions
open System.Collections.Generic
open System.Threading.Tasks
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Interactive.Shell
open Companion.BlockLocator
open Companion.Envelope
open Companion.RequestCapture

type Diagnostic = { Message: string; Range: BlockRange }

/// The request that was actually sent, read off `Response.requestMessage` plus the body capture.
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

/// `RequestMs` brackets the single `EvalExpressionNonThrowing` that sends the request, and it
/// excludes the host-side total.
type ResponseData =
    { Status: int
      Reason: string
      Headers: (string * string) list
      ContentType: string
      BodyBase64: string
      RequestMs: float }

type RunOutcome =
    | Ok of request: RequestData * response: ResponseData
    | CompileError of Diagnostic list
    | RuntimeError of string
    /// The companion refused the Run before it evaluated a block. `code` is the wire spelling.
    /// `name` carries the blanked binding name for `unboundBlockValue` only. Other codes carry
    /// `None`.
    | Refused of code: string * name: string option

/// The only place either field name is spelled. The addendum and `invocationConfigUpdate` both
/// read this text, so the two copies cannot drift.
let private responseReadingFields =
    "bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead"

/// The response-reading guard on its own, as generated F# source, for the addendum's copy.
let private responseReadingGuard =
    sprintf "Config.update (fun c -> { c with %s })" responseReadingFields

/// The FSI binding that the invocation's `Config.update` writes the *applied* timeout into, in
/// milliseconds, with `0.` for "no bound at all". The companion reads it back when a
/// cancellation surfaces, so the message names the bound that fired rather than the bound that
/// rode the wire. The two differ whenever the block set its own `config_timeoutInSeconds`.
let private appliedTimeoutBinding = "__fsHttpStudioAppliedTimeoutMs"

/// The FSI name of the body-capture transformer. The addendum binds it once by reflecting into
/// `Companion.RequestCapture.captureRequest` (already loaded in this process). The invocation's
/// `Config.update` then prepends it to `httpMessageTransformers`. FSI cannot close over a
/// companion CLR value, so the addendum resolves the method through the loaded assembly.
let private captureRequestBinding = "__fsHttpStudioCaptureRequest"

/// The Runtime error text for a request that hit its bound. `timeoutMs` is the bound that was
/// applied, so a user who raised `fshttpStudio.requestTimeoutMs`, or who set a timeout on the
/// block, reads their own number back.
let requestTimeoutMessage (timeoutMs: int) : string =
    sprintf
        "No response within %d ms. FsHttp.Studio stopped waiting.\nRaise fshttpStudio.requestTimeoutMs to wait longer, or set it to 0 to wait as long as HttpClient allows."
        timeoutMs

/// The invocation's `Config.update` fragment: the response-reading fields, the body-capture
/// transformer, an optional injected timeout, and the write to `appliedTimeoutBinding`.
/// `timeoutMs = 0` means do not inject, so `Config.timeout` stays whatever the block already
/// carried (`None` when the block set none). A positive `timeoutMs` adds an `Option.orElse`
/// default, so a block that already set `config_timeoutInSeconds` keeps it.
let invocationConfigUpdate (timeoutMs: int) : string =
    let timeoutField =
        if timeoutMs <= 0 then
            ""
        else
            sprintf "; timeout = c.timeout |> Option.orElse (Some (System.TimeSpan.FromMilliseconds %d.))" timeoutMs

    let captureField =
        sprintf "; httpMessageTransformers = %s :: c.httpMessageTransformers" captureRequestBinding

    sprintf
        "Config.update (fun c -> let applied = { c with %s%s%s } in (%s <- (match applied.timeout with Some t -> t.TotalMilliseconds | None -> 0.)); applied)"
        responseReadingFields
        captureField
        timeoutField
        appliedTimeoutBinding

/// The exception that a Run should report on. A refused connection arrives wrapped
/// (`AggregateException` → `HttpRequestException` → `SocketException`), and
/// `AggregateException`'s own message is the generic "One or more errors occurred.". A timeout
/// arrives bare. One unwrap serves both readers below, so they cannot disagree about which
/// exception they are looking at.
let private unwrapAggregate (ex: exn) : exn =
    match ex with
    | :? AggregateException as ae ->
        let flat = ae.Flatten()

        if flat.InnerExceptions.Count > 0 then
            flat.InnerExceptions.[0]
        else
            ex
    | _ -> ex

/// True when `root`, already unwrapped, is a bound firing. The companion never passes a
/// cancellation token of its own, so any `OperationCanceledException` in the chain is the
/// timeout. `TaskCanceledException` derives from it, so this covers both.
let private isRequestTimeout (root: exn) : bool =
    let rec inChain (e: exn) =
        match e with
        | :? OperationCanceledException -> true
        | _ ->
            match e.InnerException with
            | null -> false
            | inner -> inChain inner

    inChain root

/// Maps an invocation exception to a Runtime error. A cancellation, when a bound was actually
/// applied, becomes `requestTimeoutMessage` at that applied number. `readAppliedTimeoutMs` is
/// deferred, because reading it costs an FSI evaluation and only the timeout branch needs it.
/// Every other failure keeps the unwrapped exception's own message, so a refused connection
/// still names the connection.
let private runtimeErrorFrom (readAppliedTimeoutMs: unit -> int) (ex: exn) : RunOutcome =
    let root = unwrapAggregate ex

    if isRequestTimeout root then
        match readAppliedTimeoutMs () with
        | applied when applied > 0 -> RuntimeError(requestTimeoutMessage applied)
        | _ -> RuntimeError root.Message
    else
        RuntimeError root.Message

/// Works around an FsHttp defect: FsHttp reuses one process-wide static `HttpClient`, so a Run
/// that reuses a pooled keep-alive connection can receive a content whose stream is already
/// consumed, and `ReadAsByteArrayAsync` then throws "The stream was already consumed."
/// `httpCompletionOption = ResponseContentRead` makes the BCL read the whole body during the
/// send, independent of any later connection state. `bufferResponseContent` stays on as a second
/// guard.
/// The addendum carries no `#r`, because the user's setup is the only source of an FsHttp
/// package reference. It declares `appliedTimeoutBinding` and `captureRequestBinding`, because
/// the invocation is a single expression with nowhere to put a declaration.
/// The reflection lookup is total: a companion whose capture it cannot find binds `id`, so the
/// Run still sends and only the body display is lost. A throwing lookup would break every Run.
let captureRequestDeclaration =
    [ sprintf
          "let %s : System.Net.Http.HttpRequestMessage -> System.Net.Http.HttpRequestMessage ="
          captureRequestBinding
      "    try"
      "        System.AppDomain.CurrentDomain.GetAssemblies()"
      "        |> Array.tryPick (fun a ->"
      "            match a.GetType \"Companion.RequestCapture\" with"
      "            | null -> None"
      "            | t ->"
      "                match t.GetMethod \"captureRequest\" with"
      "                | null -> None"
      "                | m -> Some m)"
      "        |> function"
      "            | Some mi -> fun m -> mi.Invoke(null, [| box m |]) :?> System.Net.Http.HttpRequestMessage"
      "            | None -> id"
      "    with _ ->"
      "        id" ]
    |> String.concat "\n"

let private companionAddendum =
    [ "open FsHttp"
      "FsHttp.Fsi.disableDebugLogs()"
      sprintf "let mutable %s = 0." appliedTimeoutBinding
      captureRequestDeclaration
      sprintf "GlobalConfig.set (GlobalConfig.defaults |> %s)" responseReadingGuard ]
    |> String.concat "\n"

let private asPairs (h: IEnumerable<KeyValuePair<string, IEnumerable<string>>>) =
    h |> Seq.map (fun kv -> kv.Key, String.Join(", ", kv.Value))

/// Joins a message's own headers with its content's, first name winning. The request and the
/// response both need this exact merge, so one walk serves both and neither can drift from the
/// other in join character or dedupe order.
let private mergeHeaders
    (own: IEnumerable<KeyValuePair<string, IEnumerable<string>>>)
    (content: HttpContent | null)
    : (string * string) list =
    let contentHeaders =
        match content with
        | null -> Seq.empty
        | c -> asPairs c.Headers

    Seq.append (asPairs own) contentHeaders |> Seq.distinctBy fst |> Seq.toList

/// Blanks a span in place across `lines`. It keeps every newline, so every other line's row and
/// column numbers stay aligned with the original source. `fill` builds the replacement for the
/// span's own first line, from that line's width. The two callers below differ in `fill` and in
/// nothing else, so one walk serves both, and neither can drift from the other by a column.
let private blankRange (fill: int -> string) (lines: string[]) (r: BlockRange) =
    let startIdx = r.StartLine - 1
    let endIdx = r.EndLine - 1

    if startIdx = endIdx then
        let line = lines.[startIdx]

        lines.[startIdx] <-
            line.Substring(0, r.StartCol)
            + fill (r.EndCol - r.StartCol)
            + line.Substring(r.EndCol)
    else
        let firstLine = lines.[startIdx]
        lines.[startIdx] <- firstLine.Substring(0, r.StartCol) + fill (firstLine.Length - r.StartCol)

        for i in startIdx + 1 .. endIdx - 1 do
            lines.[i] <- String(' ', lines.[i].Length)

        let lastLine = lines.[endIdx]
        lines.[endIdx] <- String(' ', r.EndCol) + lastLine.Substring(r.EndCol)

/// Blanks a block's `Blank` span. The `()` placeholder keeps a `let`-bound declaration
/// well-formed, and does not evaluate the request that it displaces. The span reaches past the CE
/// itself, so a trailing `|> Request.send` on the excluded block's own line has nothing left to
/// pipe from.
let private blankSpan (lines: string[]) (r: BlockRange) =
    blankRange (fun width -> "()" + String(' ', max 0 (width - 2))) lines r

/// Blanks a span to pure spaces, with no `()` placeholder: the two uses below remove a keyword
/// or an annotation rather than an expression's value, and the surrounding syntax stays valid
/// with nothing in its place.
let private blankToSpaces (lines: string[]) (r: BlockRange) =
    blankRange (fun width -> String(' ', width)) lines r

/// True when `outer` fully contains `inner`: at or before its start, and at or after its end.
/// A sibling whose blank span contains the target must never
/// be blanked, or the blank would delete the very block the user clicked. `let a, b = http { },
/// http { }` gives both blocks one statement span; a block nested inside another block's own
/// expression gives the same shape, and it is the one hazard 1 shape that is actually runnable
/// (extra.fsx case 24).
let private containsBlock (outer: BlockRange) (inner: BlockRange) =
    let startsAtOrBefore =
        outer.StartLine < inner.StartLine
        || (outer.StartLine = inner.StartLine && outer.StartCol <= inner.StartCol)

    let endsAtOrAfter =
        outer.EndLine > inner.EndLine
        || (outer.EndLine = inner.EndLine && outer.EndCol >= inner.EndCol)

    startsAtOrBefore && endsAtOrAfter

/// The R1 route names nothing, so the Run invents a name. Backtick-quoted so that no legal user
/// identifier can collide with it by accident. A user binding of the same backtick-quoted name
/// in the same scope still collides, and the Run does not avoid that.
[<Literal>]
let private reservedTargetName = "__fsHttpStudio_target"

/// Inserted at the block's own start column, on the block's own line.
/// Its own length is the column residue that `unshiftPos` and `shiftForward` carry as `Offset`.
/// Every user of that residue reads the length from here, so the reserved name is
/// free to change without a second edit.
let private r1InsertText = sprintf "let ``%s`` = " reservedTargetName

/// The invocation's own name, unqualified: what the R1 route inserted, or what the R2 route's
/// binding already offers. A `Refused` route never reaches this, because `run`'s gate returns before
/// `runInProcessDirect` is ever called for a refused target.
let private baseInvocation (route: Route) : string =
    match route with
    | NamedByTheRun -> sprintf "``%s``" reservedTargetName
    | NamedByTheBinding invocation -> invocation
    | BlockLocator.Refused _ -> invalidArg "route" "a refused route builds no invocation"

/// The `()` a unit-arity binding's invocation carries: `getSnorlax ()`'s own suffix.
[<Literal>]
let private unitArgSuffix = " ()"

/// Prefixes the invocation's own name with the enclosing-module qualifier (outermost first),
/// and leaves a trailing arity suffix after the qualified name, so it emits
/// `Outer.getSnorlax ()`.
///
/// The split is on the `" ()"` *suffix* rather than on the first space. A binding's own name can
/// itself hold a space, because `BlockLocator` spells ``let ``get pikachu`` = …``'s name back
/// with its backticks, and splitting such a name at its first space would emit
/// `Outer.``get pikachu```
/// as two juxtaposed terms, which reads as a function application and does not compile.
let private qualifyInvocation (qualifier: string list) (invocation: string) : string =
    let name, arity =
        if invocation.EndsWith unitArgSuffix then
            invocation.Substring(0, invocation.Length - unitArgSuffix.Length), unitArgSuffix
        else
            invocation, ""

    ((qualifier @ [ name ]) |> String.concat ".") + arity

/// What the R1 insertion does to one line's columns. The R2 route names nothing and
/// inserts no text, so it carries no shift at all. Every `ColumnShift option` below is `None`
/// there, and every translation is the identity.
type private ColumnShift =
    {
        /// The block's own start line: the one line the insertion touches.
        Line: int
        /// The column the insertion starts at, which is the block's own start column.
        InsertCol: int
        /// The inserted text's own width: what a column at or past the insertion carries.
        Offset: int
    }

let private shiftFor (target: LocatedBlock) : ColumnShift option =
    match target.Route with
    | NamedByTheRun ->
        Some
            { Line = target.Block.StartLine
              InsertCol = target.Block.StartCol
              Offset = r1InsertText.Length }
    | NamedByTheBinding _
    | BlockLocator.Refused _ -> None

/// Moves an *original*-source column forward across the R1 insertion, so a boundary computed in
/// source coordinates (the block's own end column, for the truncation point) lands on the same
/// character in the edited Setup text. Identity off the shifted line, and left of the insertion.
let private shiftForward (shift: ColumnShift option) (line: int, col: int) =
    match shift with
    | Some s when line = s.Line && col >= s.InsertCol -> line, col + s.Offset
    | _ -> line, col

/// Moves a Setup-interaction-coordinate column back to the original source. A
/// column before the insertion point is untouched. A column inside the inserted text itself has
/// no original counterpart, and clamps to the insertion point. A column at or past the inserted
/// text's end is the block's own text, shifted forward by `offset`, so it subtracts back out.
let private unshiftPos (shift: ColumnShift option) (line: int, col: int) =
    match shift with
    | Some s when line = s.Line ->
        if col < s.InsertCol then line, col
        elif col < s.InsertCol + s.Offset then line, s.InsertCol
        else line, col - s.Offset
    | _ -> line, col

/// True when a *Setup-coordinate* position falls inside the R1 inserted text itself, which is
/// the companion's own generated `let <name> = `. Such a position has no user-source counterpart,
/// and `unshiftPos` clamps it to the insertion point, which is also the block's own start
/// column, so it would otherwise pass `withinBlock` and be misreported as the user's fault.
/// The companion's own generated text belongs on the Setup side of the split, so this test runs
/// on the *raw* position, before the clamp erases the distinction.
let private withinInsertion (shift: ColumnShift option) (line: int, col: int) =
    match shift with
    | Some s -> line = s.Line && col >= s.InsertCol && col < s.InsertCol + s.Offset
    | None -> false

/// Everything from line 1 through the end of the target block's expression, with every other
/// located block's `Blank` span replaced first, so a click fires exactly one request.
/// The R1 insertion of `let <name> = ` happens before the truncation point is computed, because
/// that insertion can land on the line the boundary truncates.
/// Also returns every name a blanked sibling removed, because the blanking step is the only place
/// that knows which names it took away.
let private buildSetupText
    (source: string)
    (blocks: LocatedBlock list)
    (target: LocatedBlock)
    : string * ColumnShift option * Set<string> =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    // A sibling's blank span can contain the target: a nested block shares its outer
    // binding's declaration span.
    let blankedSiblings =
        blocks |> List.filter (fun b -> not (containsBlock b.Blank target.Block))

    blankedSiblings |> List.iter (fun b -> blankSpan lines b.Blank)

    let blankedNames =
        blankedSiblings |> List.collect (fun b -> b.BoundNames) |> Set.ofList

    // Must run before the R1 insertion below moves any column on the same line.
    target.PrivateSpans |> List.iter (blankToSpaces lines)
    target.TypeAnnotation |> Option.iter (blankToSpaces lines)

    let shift = shiftFor target

    match shift with
    | Some s ->
        let idx = s.Line - 1
        let line = lines.[idx]
        lines.[idx] <- line.Substring(0, s.InsertCol) + r1InsertText + line.Substring(s.InsertCol)
    | None -> ()

    let cutLine, cutCol = shiftForward shift (target.Block.EndLine, target.Block.EndCol)
    let cutIdx = cutLine - 1

    let prefixLines = lines.[0 .. cutIdx - 1]
    let lastLineText = lines.[cutIdx].Substring(0, cutCol)

    Array.append prefixLines [| lastLineText |] |> String.concat "\n", shift, blankedNames

/// The second interaction: invokes the target by its qualified name, and applies the
/// response-reading guard (and the optional request timeout) to its value before sending.
/// The Setup builds the block's context *inside*
/// itself, and thus before the companion addendum's `GlobalConfig.set` runs, so the context
/// would otherwise still carry FsHttp's `ResponseHeadersRead` default and leave the body a
/// read-once stream. `Config.update` re-applies the guard on the built value, which is
/// idempotent with the addendum's own guard. The timeout rides this same update as an
/// `Option.orElse` default: it never overrides a bound the block already set.
let private invocationText (timeoutMs: int) (target: LocatedBlock) : string =
    let qualified = baseInvocation target.Route |> qualifyInvocation target.Qualifier

    sprintf "%s |> %s |> Request.send" qualified (invocationConfigUpdate timeoutMs)

let private errorDiagnostics (diags: FSharpDiagnostic[]) =
    diags |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error)

/// FCS's own "unbound value" diagnostic code, which stays stable across localizations.
[<Literal>]
let private unboundValueErrorNumber = 39

/// A Run outcome rather than a `RefusalCode`, so `BlockLocator.codeToWire` does not carry it.
[<Literal>]
let private unboundBlockValueCode = "unboundBlockValue"

/// The source text a diagnostic's own (single-line) range covers in `lines`, read back against
/// the Setup text itself -- never parsed out of the (localized) *message*, which this
/// deliberately does not touch. `None` when the range does not describe one line inside `lines`,
/// which a multi-line unbound-value diagnostic never should, but a defensive read still declines
/// to guess.
let private textUnderDiagnostic (lines: string[]) (d: FSharpDiagnostic) : string option =
    if d.StartLine = d.EndLine && d.StartLine >= 1 && d.StartLine <= lines.Length then
        let line = lines.[d.StartLine - 1]

        if d.StartColumn >= 0 && d.StartColumn <= d.EndColumn && d.EndColumn <= line.Length then
            Some(line.Substring(d.StartColumn, d.EndColumn - d.StartColumn))
        else
            None
    else
        None

/// True when `point` (already unshifted to original-source coordinates) falls inside `block`'s
/// own span: at or after its start, and strictly before its end.
let private withinBlock (block: BlockRange) (line: int, col: int) =
    let atOrAfterStart =
        line > block.StartLine || (line = block.StartLine && col >= block.StartCol)

    let beforeEnd = line < block.EndLine || (line = block.EndLine && col < block.EndCol)
    atOrAfterStart && beforeEnd

/// Maps a diagnostic from the combined Setup evaluation back onto the original source, and
/// names the Setup in its message. Every position first passes through `unshiftPos`, which is
/// the identity off the R1 insertion line and off the R2 route (`shift = None`).
///
/// The first `realLineCount` lines *are* the original text, minus the other blocks that the
/// setup blanked. A diagnostic inside those lines is already native and needs no translation.
///
/// A diagnostic past those lines comes from the appended `companionAddendum`, or from the
/// invocation interaction, where `realLineCount = 0`, and has no
/// source counterpart. One example is a failed `open FsHttp`, because the user's script carries
/// no resolvable `#r`. Anchor such a diagnostic at the top of the script, where the missing
/// reference belongs. A phantom line past the end would fail to highlight in the UI.
///
/// Every diagnostic from here keeps its compiler text verbatim behind a `Setup failed to
/// evaluate:` prefix, including an anchored one.
let private setupDiagnostic (realLineCount: int) (shift: ColumnShift option) (d: FSharpDiagnostic) : Diagnostic =
    let message = sprintf "Setup failed to evaluate: %s" d.Message
    let sl, sc = unshiftPos shift (d.StartLine, d.StartColumn)

    if sl > realLineCount then
        { Message = message
          Range =
            { StartLine = 1
              StartCol = 0
              EndLine = 1
              EndCol = 0 } }
    else
        let el, ec = unshiftPos shift (d.EndLine, d.EndColumn)

        { Message = message
          Range =
            { StartLine = sl
              StartCol = sc
              EndLine = el
              EndCol = ec } }

/// Whether `errors` refuses the Run rather than compile-erroring it. Every
/// error diagnostic must trace to a blanked name for the refusal to claim the Run -- one
/// unrelated error (the user's own typo, or an FS0039 naming something no sibling bound) means
/// the missing binding is not the whole story, and the whole thing is a compile error instead.
///
/// Reads `setupLines` rather than `combinedSetup`'s own text, because a diagnostic's position here is
/// still in Setup-interaction coordinates and `setupLines` is exactly that interaction's text
/// (the companion addendum carries no user name to unbind, so it never contributes a match).
///
/// Several blanked names can be unbound at once. The refusal names the *first* one in diagnostic
/// order, which is the first one the compiler reached, because the detail sentence speaks about
/// one value. The rest are the same limit reported twice, so naming them adds nothing.
let private blankedNameRefusal
    (setupLines: string[])
    (blankedNames: Set<string>)
    (errors: FSharpDiagnostic[])
    : string option =
    // A diagnostic range carries the backticks of ``a quoted name``. `BoundNames` does not.
    let unquote (text: string) =
        if text.Length >= 4 && text.StartsWith "``" && text.EndsWith "``" then
            text.Substring(2, text.Length - 4)
        else
            text

    if errors.Length = 0 then
        None
    else
        let matches =
            errors
            |> Array.map (fun d ->
                if d.ErrorNumber = unboundValueErrorNumber then
                    textUnderDiagnostic setupLines d
                    |> Option.map unquote
                    |> Option.filter blankedNames.Contains
                else
                    None)

        if matches |> Array.forall Option.isSome then
            matches.[0]
        else
            None

/// A diagnostic that starts inside the target block's own span keeps the compiler's text
/// unchanged, at its own (unshifted) position, with no introductory sentence, because the fault
/// is in the user's block rather than in text the companion generated.
let private blockDiagnostic (shift: ColumnShift option) (d: FSharpDiagnostic) : Diagnostic =
    let sl, sc = unshiftPos shift (d.StartLine, d.StartColumn)
    let el, ec = unshiftPos shift (d.EndLine, d.EndColumn)

    { Message = d.Message
      Range =
        { StartLine = sl
          StartCol = sc
          EndLine = el
          EndCol = ec } }

/// Splits a Setup-interaction diagnostic between the two treatments above, by whether its
/// (unshifted) start position lands inside the target's own block span.
///
/// A diagnostic that starts inside the R1 inserted text is the one exception, and it takes the
/// Setup treatment. The fault there is in the companion's own generated `let <name> = `, and
/// never in anything the user wrote. A user binding of the reserved name in the same scope reports its
/// duplicate definition exactly there.
/// The test runs before `unshiftPos`, because the clamp moves such a position onto the block's
/// own start column and it would otherwise read as the user's fault.
let private splitDiagnostic
    (shift: ColumnShift option)
    (blockRange: BlockRange)
    (realLineCount: int)
    (d: FSharpDiagnostic)
    : Diagnostic =
    let raw = d.StartLine, d.StartColumn

    if not (withinInsertion shift raw) && withinBlock blockRange (unshiftPos shift raw) then
        blockDiagnostic shift d
    else
        setupDiagnostic realLineCount shift d

/// `PropertyInfo.GetProperty` is nullable-annotated, because the name can be absent. The fields
/// that this function reads are FsHttp's `Response` record shape, which is stable across the
/// FsHttp versions that we target. A missing property is therefore a real extraction bug, and
/// not a case to recover from. The response body itself comes from the BCL `HttpContent` type,
/// which is version-independent.
let private prop (name: string) (t: Type) : Reflection.PropertyInfo =
    match t.GetProperty name with
    | null -> failwithf "reflection: property '%s' not found on %s" name t.FullName
    | p -> p

[<Literal>]
let private NoneState = "none"

[<Literal>]
let private CapturedState = "captured"

[<Literal>]
let private NotCapturedState = "notCaptured"

/// The body to show for a sent request. A hit is the captured body itself. A miss degrades
/// rather than breaking the status line, because the method, URL, and headers do not depend on
/// the capture at all.
/// The content decides which blank state a miss degrades to. With no content there was no body.
/// With content there was a body that the capture never read, so "no body" would be false.
let capturedBodyFor (requestMessage: HttpRequestMessage) : CapturedBody =
    match tryGetCapturedBody requestMessage with
    | Some body -> body
    | None ->
        match requestMessage.Content with
        | null -> NoBody
        | _ -> NotCaptured uncapturedBodyReason

/// Maps a captured body onto the wire's three-state `bodyState` / `bodyBase64` / `bodyReason`
/// triple. Only the matching field carries a value, and the others are empty strings.
let private bodyToWire (body: CapturedBody) : string * string * string =
    match body with
    | NoBody -> NoneState, "", ""
    | Captured bytes -> CapturedState, Convert.ToBase64String bytes, ""
    | NotCaptured reason -> NotCapturedState, "", reason

/// The inverse of `bodyToWire`. An unrecognized state is a defect on our own wire, because both
/// ends are this module, so it throws rather than decaying to `NoBody`, which would tell the user
/// no body was sent when one was.
let private bodyFromWire (bodyState: string) (bodyBase64: string) (bodyReason: string) : CapturedBody =
    match bodyState with
    | NoneState -> NoBody
    | CapturedState -> Captured(Convert.FromBase64String bodyBase64)
    | NotCapturedState -> NotCaptured bodyReason
    | other -> failwithf "wire: unknown bodyState '%s'" other

let private extractResponse (requestMs: float) (v: FsiValue) : RunOutcome =
    let t = v.ReflectionType
    let rv = v.ReflectionValue
    let getValue name = (prop name t).GetValue(rv)

    let statusInt = int (getValue "statusCode" :?> Net.HttpStatusCode)
    let reason = string (getValue "reasonPhrase")

    let content =
        match getValue "content" with
        | :? HttpContent as c -> c
        | _ -> failwith "reflection: 'content' property was not an HttpContent"

    let respHeaders =
        match getValue "headers" with
        | :? Net.Http.Headers.HttpResponseHeaders as h -> h
        | _ -> failwith "reflection: 'headers' property was not HttpResponseHeaders"

    let requestMessage =
        match getValue "requestMessage" with
        | null -> failwith "reflection: 'requestMessage' was null"
        | :? HttpRequestMessage as m -> m
        | _ -> failwith "reflection: 'requestMessage' property was not an HttpRequestMessage"

    let requestUrl =
        match requestMessage.RequestUri with
        | null -> failwith "reflection: requestMessage.RequestUri was null"
        // AbsoluteUri keeps percent-escapes (`?q=one%20two`). Uri.ToString() would decode them.
        | uri -> uri.AbsoluteUri

    let requestHeaders = mergeHeaders requestMessage.Headers requestMessage.Content

    let requestBody = capturedBodyFor requestMessage

    let bytes = content.ReadAsByteArrayAsync().Result

    let ctype =
        match content.Headers.ContentType with
        | null -> ""
        | c -> string c

    let headers = mergeHeaders respHeaders content

    Ok(
        { Method = requestMessage.Method.ToString()
          Url = requestUrl
          Headers = requestHeaders
          Body = requestBody },
        { Status = statusInt
          Reason = reason
          Headers = headers
          ContentType = ctype
          BodyBase64 = Convert.ToBase64String bytes
          RequestMs = requestMs }
    )

/// Evaluates the block at `blockIndex` in `source` *in the current process*, and returns its
/// outcome. The index is 0-based and in source order, which matches the order in a `locate` and
/// `blocks` envelope. Each call creates and disposes a fresh `FsiEvaluationSession`, which
/// gives one fresh session per Run.
///
/// `timeoutMs` is the request bound from the host setting. `0` means do not inject one.
///
/// This is the warm fast path. `run` calls it directly when the target's `#r "nuget:"` pins do
/// not conflict with a version already loaded into this process. The `--worker` entry point
/// also calls it in a throwaway child process, to serve a conflicting pin against a clean ALC.
let private runLocated
    (source: string)
    (located: LocatedBlock list)
    (blockIndex: int)
    (scriptFileName: string option)
    (timeoutMs: int)
    : RunOutcome =
    match List.tryItem blockIndex located with
    | None -> Refused("staleBlockIndex", None)
    | Some target ->
        let setupText, shift, blankedNames = buildSetupText source located target
        let combinedSetup = setupText + "\n" + companionAddendum

        let setupLines = setupText.Split('\n')
        let setupLineCount = setupLines.Length

        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
        let args = [| "fsi.exe"; "--noninteractive"; "--nologo" |]
        use inReader = new IO.StringReader("")

        //
        // `collectible` isolates the per-session dynamic assembly alone. The assemblies that
        // `#r "nuget:"` resolves load into the process-wide default ALC and outlive the session.
        use session =
            FsiEvaluationSession.Create(fsiConfig, args, inReader, Console.Error, Console.Error, collectible = true)

        let evalInteraction code =
            match scriptFileName with
            | Some path -> session.EvalInteractionNonThrowing(code, path)
            | None -> session.EvalInteractionNonThrowing(code)

        let evalExpression code =
            match scriptFileName with
            | Some path -> session.EvalExpressionNonThrowing(code, path)
            | None -> session.EvalExpressionNonThrowing(code)

        // `Option.orElse` can keep a timeout the block set for itself, so read the applied
        // bound after the fact rather than the injected one.
        let readAppliedTimeoutMs () =
            match evalExpression appliedTimeoutBinding with
            | Choice1Of2(Some v), _ ->
                match v.ReflectionValue with
                | :? float as ms -> int ms
                | _ -> timeoutMs
            | _ -> timeoutMs

        let setupResult, setupDiags = evalInteraction combinedSetup

        // FSI returns `Choice1Of2` for a Setup that fails to parse or type-check, and discards
        // the failure into the diagnostics array. The array is the only reliable signal.
        //
        //
        // Must run before `splitDiagnostic`, which translates the range this check needs away.
        let setupErrors = errorDiagnostics setupDiags

        match blankedNameRefusal setupLines blankedNames setupErrors with
        | Some name -> Refused(unboundBlockValueCode, Some name)
        | None ->
            match
                setupErrors
                |> Array.map (splitDiagnostic shift target.Block setupLineCount)
                |> Array.toList
            with
            | [] ->
                match setupResult with
                | Choice2Of2 ex -> RuntimeError ex.Message
                | Choice1Of2 _ ->
                    let sw = Stopwatch.StartNew()
                    let targetResult, targetDiags = evalExpression (invocationText timeoutMs target)
                    sw.Stop()
                    let requestMs = sw.Elapsed.TotalMilliseconds

                    match targetResult with
                    | Choice2Of2 ex ->
                        match
                            errorDiagnostics targetDiags
                            |> Array.map (setupDiagnostic 0 None)
                            |> Array.toList
                        with
                        | [] -> runtimeErrorFrom readAppliedTimeoutMs ex
                        | errors -> CompileError errors
                    | Choice1Of2 None -> RuntimeError "expression returned no value"
                    | Choice1Of2(Some v) ->
                        try
                            extractResponse requestMs v
                        with ex ->
                            runtimeErrorFrom readAppliedTimeoutMs ex
            | errors -> CompileError errors

/// `runLocated` against a fresh locate of `source`. This is the `--worker` child's entry point,
/// and the direct in-process path. The child receives source text, an optional absolute
/// `scriptFileName`, and the request `timeoutMs` from the parent's worker payload. In the
/// parent, `run` has already located the blocks to decide the gate, so it calls `runLocated`
/// directly rather than parse a second time.
let runInProcessDirect
    (source: string)
    (blockIndex: int)
    (scriptFileName: string option)
    (timeoutMs: int)
    : RunOutcome =
    runLocated source (locateBlocks source).Blocks blockIndex scriptFileName timeoutMs


/// Serializes a `RunOutcome` to the same tagged wire shape that the host-facing `run`, `ok`,
/// `compileError`, and `runtimeError` envelope uses. The `--worker` child emits its outcome
/// over this shape, and `RequestHandler` emits the host's response over it. The two channels
/// therefore cannot drift apart.
let outcomeToWire (outcome: RunOutcome) : obj =
    match outcome with
    | Ok(request, response) ->
        let requestBodyState, requestBodyBase64, requestBodyReason = bodyToWire request.Body

        {| tag = "ok"
           status = response.Status
           reason = response.Reason
           headers = dict response.Headers
           contentType = response.ContentType
           bodyBase64 = response.BodyBase64
           requestMs = response.RequestMs
           request =
            {| method = request.Method
               url = request.Url
               headers = dict request.Headers
               bodyState = requestBodyState
               bodyBase64 = requestBodyBase64
               bodyReason = requestBodyReason |} |}
    | CompileError diagnostics ->
        {| tag = "compileError"
           diagnostics =
            diagnostics
            |> List.map (fun d ->
                {| message = d.Message
                   range =
                    {| startLine = d.Range.StartLine
                       startCol = d.Range.StartCol
                       endLine = d.Range.EndLine
                       endCol = d.Range.EndCol |} |}) |}
    | RuntimeError message ->
        {| tag = "runtimeError"
           message = message |}
    | Refused(code, None) -> {| tag = "refused"; code = code |}
    | Refused(code, Some name) ->
        {| tag = "refused"
           code = code
           name = name |}

/// Parses a `--worker` child's response frame back into a `RunOutcome`. It is the inverse of
/// `outcomeToWire`, so the delegation is transparent to `run`'s caller. Public so the wire
/// round-trip for the request object is testable at the same seam.
let wireToOutcome (root: JsonElement) : RunOutcome =
    match jsonString (root.GetProperty "tag") with
    | "ok" ->
        let headers =
            [ for p in root.GetProperty("headers").EnumerateObject() -> p.Name, jsonString p.Value ]

        let requestElem = root.GetProperty "request"

        let requestHeaders =
            [ for p in requestElem.GetProperty("headers").EnumerateObject() -> p.Name, jsonString p.Value ]

        let body =
            bodyFromWire
                (jsonString (requestElem.GetProperty "bodyState"))
                (jsonString (requestElem.GetProperty "bodyBase64"))
                (jsonString (requestElem.GetProperty "bodyReason"))

        Ok(
            { Method = jsonString (requestElem.GetProperty "method")
              Url = jsonString (requestElem.GetProperty "url")
              Headers = requestHeaders
              Body = body },
            { Status = root.GetProperty("status").GetInt32()
              Reason = jsonString (root.GetProperty "reason")
              Headers = headers
              ContentType = jsonString (root.GetProperty "contentType")
              BodyBase64 = jsonString (root.GetProperty "bodyBase64")
              RequestMs = root |> getFloatProp "requestMs" }
        )
    | "compileError" ->
        [ for d in root.GetProperty("diagnostics").EnumerateArray() do
              let r = d.GetProperty "range"

              { Message = jsonString (d.GetProperty "message")
                Range =
                  { StartLine = r.GetProperty("startLine").GetInt32()
                    StartCol = r.GetProperty("startCol").GetInt32()
                    EndLine = r.GetProperty("endLine").GetInt32()
                    EndCol = r.GetProperty("endCol").GetInt32() } } ]
        |> CompileError
    | "refused" ->
        let name =
            match root.TryGetProperty "name" with
            | true, v when v.ValueKind <> JsonValueKind.Null -> Some(jsonString v)
            | _ -> None

        Refused(jsonString (root.GetProperty "code"), name)
    | _ -> RuntimeError(jsonString (root.GetProperty "message"))

/// Matches a `#r "nuget: Package[, Version]"` directive. It captures the package id, and the
/// version when the directive pins one.
let private nugetPinRegex =
    Regex("""#r\s+"nuget:\s*(?<pkg>[^,"\s]+)\s*(?:,\s*(?<ver>[^",\s]+))?""", RegexOptions.Compiled)

/// Extracts the `#r "nuget: Package[, Version]"` pins in a script as `(package, version option)`
/// pairs, in source order. A version-less `#r` yields `None` (see `nugetPinRegex`). This is
/// public for the direct pin-parsing tests, and `run` consumes it for conflict routing.
let extractPins (source: string) : (string * string option) list =
    [ for m in nugetPinRegex.Matches source do
          let ver = m.Groups.["ver"]

          m.Groups.["pkg"].Value,
          (if ver.Success && ver.Value <> "" then
               Some ver.Value
           else
               None) ]

/// What a package resolved to when it loaded into this process's default ALC. `Pinned v` is an
/// explicit `#r "nuget: pkg, v"`. `Versionless` is a `#r "nuget: pkg"` that resolved *some*
/// latest version that we cannot name. The version-less case is load-bearing, and the map
/// records it instead of nothing. It still poisons the ALC, so a later Run that pins a
/// *different* version would collide with it in-process.
type LoadedVersion =
    | Pinned of string
    | Versionless

/// Pure routing decision for one pin against the state that a package is already loaded in.
/// `None` means that this process has not loaded the package yet. The rule is "route to a
/// worker unless we can *prove* that the requested load matches what the ALC already holds":
///
/// - A version-less pin against a version-less load is the same latest version, because nuget
///   resolves `#r "nuget: pkg"` to one version per process. It is therefore safe in-process.
/// - Two explicit pins conflict exactly when they name different versions.
/// - Every *mixed* pair conflicts. A version-less load with a later explicit pin, and an
///   explicit load with a later version-less pin, both route to a worker. The version-less side
///   can resolve a different latest version than the named one, and we cannot prove otherwise.
///
/// This rule is deliberately conservative. A false conflict costs one cold worker Run. A false
/// match reopens the original "Could not load type … from assembly …" ALC collision.
/// Correctness wins.
let pinConflicts (loaded: LoadedVersion option) (pin: string option) : bool =
    match loaded, pin with
    | None, _ -> false
    | Some Versionless, None -> false
    | Some(Pinned l), Some v -> l <> v
    | Some(Pinned _), None
    | Some Versionless, Some _ -> true

// NuGet ids are case-insensitive.
let private loadLock = obj ()

let private loadedVersions =
    Dictionary<string, LoadedVersion>(StringComparer.OrdinalIgnoreCase)

/// What `pkg` is marked as loaded to, or `None` when nothing has reserved it. Public for the
/// routing tests only, mirroring `LoadedVersion`'s own reason for being public: a test can prove
/// that a refused Run left a pin unmarked, with no access to the private map itself.
let loadedVersionOf (pkg: string) : LoadedVersion option =
    lock loadLock (fun () ->
        match loadedVersions.TryGetValue pkg with
        | true, v -> Some v
        | false, _ -> None)

/// Where `run` sends one Run: to the warm in-process session, or to a fresh `--worker` child.
type private RunRoute =
    | InProcess
    | Worker

/// Routes one Run's `pins`. On the in-process path it also reserves them in the load map. The
/// conflict *check* and the reservation *act* run under a single `lock loadLock`, so the two
/// are one atomic step. The lock is taken once for each logical operation. A check in one lock scope, followed by a mark in another scope, leaves a TOCTOU gap.
/// A future concurrent caller could load a conflicting version in that gap.
/// The request loop is serial today, and the lock keeps it correct when that changes.
///
/// The reservation happens *before* the evaluation runs, and never after a successful load.
/// This is deliberate. The map is a conservative over-approximation of what the shared ALC can hold.
/// A Run that reaches the in-process path can resolve its `#r "nuget:"` into that ALC, and the
/// resolved assembly then outlives the session even when the evaluation compile-errors or
/// throws. An over-mark of a Run that never loaded only over-routes a *later* Run to a safe,
/// cold worker. An under-mark of a Run that did load reopens the "Could not load type … from
/// assembly …" ALC collision. The two errors have different costs, so the mark happens up front.
let private routeAndReserve (pins: (string * string option) list) : RunRoute =
    lock loadLock (fun () ->
        let conflicts =
            pins
            |> List.exists (fun (pkg, ver) ->
                let loaded =
                    match loadedVersions.TryGetValue pkg with
                    | true, v -> Some v
                    | false, _ -> None

                pinConflicts loaded ver)

        if conflicts then
            Worker
        else
            for pkg, ver in pins do
                loadedVersions.[pkg] <-
                    match ver with
                    | Some v -> Pinned v
                    | None -> Versionless

            InProcess)

/// The bound on the time that a `--worker` child can take to produce its response frame. After
/// this time the Run terminates by force. The bound is long enough to absorb a cold first-run
/// `#r "nuget:"` restore of a newly pinned version. It is short enough that a stalled worker
/// cannot hang the Run indefinitely. A user block that loops forever, or a request to a server
/// that never answers, both stall a worker. This is public so that tests can drive the hung
/// path on a short bound.
let workerTimeoutMs = 120_000

/// Runs one block in a throwaway child process (`dotnet Companion.dll --worker`), so that its
/// `#r "nuget:"` assemblies load into a fresh default ALC. The runtime reclaims that ALC when
/// the process exits, which avoids the process-global collision. The child reads one framed
/// `{ source, blockIndex, scriptFileName?, timeoutMs }` request, writes one outcome envelope,
/// and then exits.
///
/// The two bounds are both durations in milliseconds, and they mean different things, so they
/// carry different names rather than sit transposable beside each other. `workerWaitMs` bounds
/// how long the parent waits for the child's frame. `timeoutMs` is the request bound the child
/// injects at invocation time; it rides the worker payload under that name, the same field the
/// warm `run` envelope carries, and `0` means do not inject.
///
/// `Kill()` terminates a worker that does not produce its frame within `workerWaitMs`, and also
/// a worker that produces the frame and then stalls before it exits. The Run then maps to a
/// `RuntimeError`, instead of a block of the caller forever. A user block that loops forever, or
/// a request that never answers, both cause the first case. `use proc = proc` disposes the
/// handle but does not unblock a wait. The bound and the kill are what guarantee that the Run
/// always terminates.
let runInWorker
    (workerWaitMs: int)
    (source: string)
    (blockIndex: int)
    (scriptFileName: string option)
    (timeoutMs: int)
    : RunOutcome =
    let companionDll = typeof<RunOutcome>.Assembly.Location

    let psi = ProcessStartInfo(FileName = "dotnet")
    psi.ArgumentList.Add companionDll
    psi.ArgumentList.Add "--worker"
    psi.RedirectStandardInput <- true
    psi.RedirectStandardOutput <- true
    psi.UseShellExecute <- false

    try
        match Process.Start psi with
        | null -> RuntimeError "worker: failed to start evaluation process"
        | proc ->
            use proc = proc

            // A worker mid-restore can spawn child `dotnet` processes. A kill must never throw:
            // the process can be gone already.
            let kill () =
                try
                    if not proc.HasExited then
                        proc.Kill(entireProcessTree = true)
                with _ ->
                    ()

            let request: obj =
                {| source = source
                   blockIndex = blockIndex
                   scriptFileName = defaultArg scriptFileName ""
                   timeoutMs = timeoutMs |}

            writeFrame proc.StandardInput.BaseStream (JsonSerializer.SerializeToUtf8Bytes request)
            proc.StandardInput.Close()

            // The frame read has no native timeout, and a hung child never returns one.
            let readFrame = Task.Run(fun () -> tryReadFrame proc.StandardOutput.BaseStream)

            if not (readFrame.Wait workerWaitMs) then
                kill ()
                RuntimeError(sprintf "worker: no response within %dms. Evaluation process terminated." workerWaitMs)
            else
                let outcome =
                    match readFrame.Result with
                    | Some payload ->
                        use doc = JsonDocument.Parse(payload: byte[])
                        wireToOutcome doc.RootElement
                    | None -> RuntimeError "worker: evaluation process produced no response"

                // A worker that emitted its frame can still stall, so `WaitForExit` needs a bound.
                if not (proc.WaitForExit workerWaitMs) then
                    kill ()

                outcome
    with ex ->
        RuntimeError(sprintf "worker: %s" ex.Message)

/// Runs the located block at `blockIndex` (0-based, source order) and returns its outcome.
///
/// The gate runs first. A target that
/// `classify` refuses returns `Refused` here, before `routeAndReserve` marks any pin and before
/// any worker process starts. `routeAndReserve` marks each of the Run's pins in `loadedVersions`
/// up front, before any evaluation, so a refusal that reached it would mark pins that no session
/// ever loads, and slow a later Run for nothing.
///
/// An out-of-range `blockIndex` has no target route. `runLocated` refuses it as a stale lens.
///
/// The gate has to locate the blocks to decide, so the in-process path takes that same list on
/// to `runLocated`. A Run parses the source once in this process. The
/// `--worker` path cannot share it: the child is a separate process, and it locates its own.
///
/// Past the gate, `run` routes on one further condition: whether the target's `#r "nuget:"` pins
/// conflict with a version already loaded in this process. No conflict -> the warm in-process
/// session. A conflict -> a fresh `--worker` child, whose ALC cannot collide.
///
/// `timeoutMs` is the request bound from the host. Both routes carry it. `0` means do not
/// inject.
let run (source: string) (blockIndex: int) (scriptFileName: string option) (timeoutMs: int) : RunOutcome =
    let located = (locateBlocks source).Blocks

    match List.tryItem blockIndex located with
    | Some { Route = BlockLocator.Refused code } -> RunOutcome.Refused(codeToWire code, None)
    | _ ->
        match routeAndReserve (extractPins source) with
        | Worker -> runInWorker workerTimeoutMs source blockIndex scriptFileName timeoutMs
        | InProcess -> runLocated source located blockIndex scriptFileName timeoutMs
