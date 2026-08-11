module Companion.BlockRunner

// Runs one located `http { }` block against a fresh FCS interactive session, which is
// ADR-0002's mechanism. Each Run gets a fresh session. The Run reaches the block where the user
// wrote it (docs/spec/0002-reach-a-block-anywhere.md): the Setup interaction evaluates the
// script from line 1 through the end of the target block's own expression, with every *other*
// located block's `Blank` span excluded, and the target block itself included and named. A
// second interaction then invokes the target by that name, applies the response-reading guard,
// pipes to `Request.send`, and extracts the raw `Response` by reflection over the BCL
// `HttpContent` type.
//
// This module must never reference FsHttp itself. The user's own `#r "nuget: FsHttp, x.y.z"`,
// evaluated as part of their own setup text, is the only thing that resolves the package, so
// their version pin always wins.

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

/// The request that was actually sent, read off `Response.requestMessage` plus the body
/// capture (docs/spec/0012-request-as-sent.md, Decisions 1 and 9).
type RequestData =
    { Method: string
      Url: string
      Headers: (string * string) list
      Body: CapturedBody }

/// The response half of a successful Run. `RequestMs` is the invocation bracket only: the
/// single `EvalExpressionNonThrowing` that sends the request
/// (docs/spec/0004-run-path-robustness.md, Decision 7). It is not the host-side total.
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

/// The two response-reading settings, as the generated F# record fields alone. This one text is
/// the only place either field name is spelled. The addendum applies them to
/// `GlobalConfig.defaults`, and `invocationConfigUpdate` applies them to the block's own value
/// (Decision 10 of docs/spec/0002-reach-a-block-anywhere.md). Neither copy can drift from the
/// other, because neither writes the fields out a second time. `companionAddendum` carries the
/// reason that each setting is load-bearing.
let private responseReadingFields =
    "bufferResponseContent = true; httpCompletionOption = System.Net.Http.HttpCompletionOption.ResponseContentRead"

/// The response-reading guard on its own, as generated F# source, for the addendum's copy.
let private responseReadingGuard =
    sprintf "Config.update (fun c -> { c with %s })" responseReadingFields

/// The FSI binding that the invocation's `Config.update` writes the *applied* timeout into, in
/// milliseconds, with `0.` for "no bound at all". The companion reads it back when a
/// cancellation surfaces, so the message names the bound that actually fired rather than the
/// bound that rode the wire. The two differ whenever the block set its own
/// `config_timeoutInSeconds`: `Option.orElse` keeps the block's value, and that is the number
/// the user needs to read (docs/spec/0004-run-path-robustness.md, Decision 5). The binding is
/// also the invocation-time `Config` read that Seam 1 scenario 5 asserts against.
let private appliedTimeoutBinding = "__fsHttpStudioAppliedTimeoutMs"

/// The FSI name of the body-capture transformer. The addendum binds it once by reflecting into
/// `Companion.RequestCapture.captureRequest` (already loaded in this process). The invocation's
/// `Config.update` then prepends it to `httpMessageTransformers`
/// (docs/spec/0012-request-as-sent.md, Decision 4). FSI cannot close over a companion CLR value
/// directly, so the addendum resolves the method through the loaded assembly instead of a `#r`.
let private captureRequestBinding = "__fsHttpStudioCaptureRequest"

/// The Runtime error text for a request that hit its bound. `timeoutMs` is the bound that was
/// applied, so a user who raised `fshttpStudio.requestTimeoutMs`, or who set a timeout on the
/// block, reads their own number back. Public so Seam 1 can assert the wording without
/// duplicating it.
let requestTimeoutMessage (timeoutMs: int) : string =
    sprintf
        "No response within %d ms. FsHttp.Studio stopped waiting.\nRaise fshttpStudio.requestTimeoutMs to wait longer, or set it to 0 to wait as long as HttpClient allows."
        timeoutMs

/// The invocation's `Config.update` fragment: the response-reading fields, the body-capture
/// transformer, an optional injected timeout, and the write to `appliedTimeoutBinding`.
/// `timeoutMs = 0` means do not inject, so `Config.timeout` stays whatever the block already
/// carried (`None` when the block set none). A positive `timeoutMs` adds an `Option.orElse`
/// default, so a block that already set `config_timeoutInSeconds` keeps it
/// (docs/spec/0004-run-path-robustness.md, Decisions 2 and 4;
/// docs/spec/0012-request-as-sent.md, Decision 4).
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
/// exception they are looking at (docs/spec/0004-run-path-robustness.md, Decision 5).
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

/// Companion-side addendum, evaluated after the user's own setup. It silences FsHttp's FSI
/// debug logging, and it forces a read of the whole response body *before* the value that we
/// reflect over returns. It carries no `#r` of its own, because the user's setup is the only
/// source of an FsHttp package reference (ADR-0002).
///
/// `httpCompletionOption = ResponseContentRead` is load-bearing, not a nicety. FsHttp defaults
/// to `ResponseHeadersRead`. With that default, `Request.send` returns as soon as the headers
/// arrive, and the body stays a *read-once* `HttpConnectionResponseContent` bound to the live
/// keep-alive socket.
///
/// FsHttp's own `bufferResponseContent = true` must drain that stream into a replayable buffer.
/// But FsHttp reuses one process-wide static `HttpClient` across every Run's fresh FSI session.
/// A Run that reuses a pooled keep-alive connection can therefore receive a content whose
/// stream is already consumed. `ReadAsByteArrayAsync` (`extractResponse`) then throws "The
/// stream was already consumed. It cannot be read again." A server that streams the body a
/// moment after its headers is the reliable trigger.
///
/// `ResponseContentRead` makes the BCL read the whole body into memory as part of the send,
/// independent of any later connection state, so every Run reads it cleanly.
/// `bufferResponseContent` stays on as a second guard.
///
/// It also declares `appliedTimeoutBinding`, which the invocation writes and the companion
/// reads back, and `captureRequestBinding`, which resolves the companion's `captureRequest`
/// into an FSI value the invocation can prepend to `httpMessageTransformers`. Both are
/// declared here, and not in the invocation, because the invocation is a single expression
/// and has nowhere to put a declaration.
/// The `captureRequestBinding` declaration. The lookup is total: a companion whose capture the
/// reflection cannot find binds `id` instead, so the Run still sends and only the body display
/// is lost. A throwing lookup here would break every Run, not only the capture.
///
/// Public so Seam 1 can evaluate this text in an FSI session of its own and drive the resulting
/// function, rather than assert that a string contains a name. Reflection into a loaded assembly
/// is the one part of the addendum that a type checker cannot verify.
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

/// Blanks a span to pure spaces, with no `()` placeholder: the two uses below remove a keyword or
/// an annotation, not an expression's value, and the surrounding syntax stays valid with nothing
/// in its place (Decisions 6 and 7).
let private blankToSpaces (lines: string[]) (r: BlockRange) =
    blankRange (fun width -> String(' ', width)) lines r

/// True when `outer` fully contains `inner`: at or before its start, and at or after its end.
/// Guards Decision 5's first hazard -- a sibling whose blank span contains the target must never
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
/// identifier can ever collide with it by accident (ADR-0007 records the deliberate one: a user
/// binding of the same backtick-quoted name in the same scope still collides, and the Run does
/// not avoid it).
[<Literal>]
let private reservedTargetName = "__fsHttpStudio_target"

/// Inserted at the block's own start column, on the block's own line (Decision 2's R1 rule).
/// Its own length is the column residue that `unshiftPos` and `shiftForward` carry as `Offset`
/// (Decision 9). Every user of that residue reads the length from here, so the reserved name is
/// free to change without a second edit.
let private r1InsertText = sprintf "let ``%s`` = " reservedTargetName

/// The invocation's own name, unqualified: what the R1 route inserted, or what the R2 route's
/// binding already offers. A `Refused` route never reaches this — `run`'s gate returns before
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
/// and leaves a trailing arity suffix after the qualified name, not before it:
/// `Outer.getSnorlax ()`, not `Outer.getSnorlax()`.
///
/// The split is on the `" ()"` *suffix*, and not on the first space. A binding's own name can
/// itself hold a space — `BlockLocator` spells ``let ``get pikachu`` = …``'s name back with its
/// backticks — and splitting such a name at its first space would emit `Outer.``get pikachu```
/// as two juxtaposed terms, which reads as a function application and does not compile.
let private qualifyInvocation (qualifier: string list) (invocation: string) : string =
    let name, arity =
        if invocation.EndsWith unitArgSuffix then
            invocation.Substring(0, invocation.Length - unitArgSuffix.Length), unitArgSuffix
        else
            invocation, ""

    ((qualifier @ [ name ]) |> String.concat ".") + arity

/// What the R1 insertion does to one line's columns (Decision 9). The R2 route names nothing and
/// inserts no text, so it carries no shift at all — every `ColumnShift option` below is `None`
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

/// Moves a Setup-interaction-coordinate column back to the original source (Decision 9). A
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
/// and `unshiftPos` clamps it to the insertion point — which is also the block's own start
/// column, so it would otherwise pass `withinBlock` and be misreported as the user's fault.
/// Decision 8 puts the companion's own generated text on the Setup side of the split, so this
/// test runs on the *raw* position, before the clamp erases the distinction.
let private withinInsertion (shift: ColumnShift option) (line: int, col: int) =
    match shift with
    | Some s -> line = s.Line && col >= s.InsertCol && col < s.InsertCol + s.Offset
    | None -> false

/// Builds the Setup text (Decision 1): everything from line 1 through the end of the target
/// block's own expression, truncated at its end column, with every *other* located block's
/// `Blank` span replaced first. A click on the target therefore fires exactly one request, which
/// is the isolation criterion, and the code after the target never runs at all — it is not part
/// of either FSI interaction, not even blanked.
///
/// The R1 route inserts `let <name> = ` at the block's own start (Decision 2) before the
/// truncation point is computed, because that insertion can land on the same line the boundary
/// truncates.
///
/// Also returns every name a blanked sibling's statement removed (Decision 7 of
/// docs/spec/0003-lens-tells-the-truth.md, case 11c): the Setup blanking step is the one place
/// that knows which names it took away, so it is the one place that records them.
let private buildSetupText
    (source: string)
    (blocks: LocatedBlock list)
    (target: LocatedBlock)
    : string * ColumnShift option * Set<string> =
    let lines = source.Replace("\r\n", "\n").Split('\n')

    // Hazard 1 (Decision 5): a sibling's blank span can contain the target itself -- a block
    // nested inside another block's own expression shares its outer binding's declaration span.
    // Blanking that span would delete the target along with the sibling. Containment is the whole
    // test, and it needs no identity test beside it. Every route's blank span contains its own
    // block, so this filter already drops the target itself.
    let blankedSiblings =
        blocks |> List.filter (fun b -> not (containsBlock b.Blank target.Block))

    blankedSiblings |> List.iter (fun b -> blankSpan lines b.Blank)

    let blankedNames =
        blankedSiblings |> List.collect (fun b -> b.BoundNames) |> Set.ofList

    // Decisions 6 and 7: blank the target's own `private` keywords and its own type annotation,
    // to spaces, before the R1 insertion below can move any column on the same line.
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
/// response-reading guard (and the optional request timeout) to its value before sending
/// (Decision 10 of docs/spec/0002-reach-a-block-anywhere.md; Decisions 2 and 3 of
/// docs/spec/0004-run-path-robustness.md). The Setup builds the block's context *inside*
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

/// FCS's own "unbound value" diagnostic code. Stable across localizations, unlike the message
/// text (docs/spec/0003-lens-tells-the-truth.md, Decision 7).
[<Literal>]
let private unboundValueErrorNumber = 39

/// The wire spelling of the case 11c refusal. It is a Run outcome and not a `RefusalCode`, so
/// `BlockLocator.codeToWire` does not carry it (docs/spec/0003-lens-tells-the-truth.md,
/// Decision 7) and it is named here instead of spelled inline at the one place that emits it.
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
/// invocation interaction (`realLineCount = 0` there — see `runInProcessDirect`), and has no
/// source counterpart. One example is a failed `open FsHttp`, because the user's script carries
/// no resolvable `#r`. Anchor such a diagnostic at the top of the script, where the missing
/// reference belongs. A phantom line past the end would fail to highlight in the UI.
///
/// *Every* diagnostic from here keeps its compiler text verbatim behind a `Setup failed to
/// evaluate:` prefix, not only an anchored one. See
/// `docs/spec/0001-report-setup-compile-error.md`, Decision 3.
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

/// Case 11c (Decision 7): whether `errors` refuses the Run rather than compile-erroring it. Every
/// error diagnostic must trace to a blanked name for the refusal to claim the Run -- one
/// unrelated error (the user's own typo, or an FS0039 naming something no sibling bound) means
/// the missing binding is not the whole story, and the whole thing is a compile error instead.
///
/// Reads `setupLines`, not `combinedSetup`'s own text, because a diagnostic's position here is
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
    // A backtick-quoted binding — ``get pikachu`` — is written with its backticks at both the
    // definition and the reference, so the diagnostic's range reads them back too, while
    // `BoundNames` holds the name without them. Strip them before the lookup, and report the
    // bare name: it is the name the user reads in the refusal sentence, not F# call syntax.
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
/// unchanged, at its own (unshifted) position (Decision 8) — no introductory sentence, because
/// the fault is in the user's block, not in text the companion generated.
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
/// (unshifted) start position lands inside the target's own block span (Decision 8).
///
/// A diagnostic that starts inside the R1 inserted text is the one exception, and it takes the
/// Setup treatment. The fault there is in the companion's own generated `let <name> = `, not in
/// anything the user wrote — a user binding of the reserved name in the same scope, which
/// ADR-0007 records as the deliberate collision, reports its duplicate definition exactly there.
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
/// which ADR-0002 commits to as version-independent.
let private prop (name: string) (t: Type) : Reflection.PropertyInfo =
    match t.GetProperty name with
    | null -> failwithf "reflection: property '%s' not found on %s" name t.FullName
    | p -> p

// The three `bodyState` names of Decision 10, written once. `bodyToWire` and `bodyFromWire`
// are inverses, so a name spelled separately in each could drift by a letter and the round
// trip would silently lose a state.
[<Literal>]
let private NoneState = "none"

[<Literal>]
let private CapturedState = "captured"

[<Literal>]
let private NotCapturedState = "notCaptured"

/// The body to show for a sent request. A hit is the captured body itself. A miss degrades
/// rather than breaking the status line, because the method, URL, and headers do not depend on
/// the capture at all (docs/spec/0012-request-as-sent.md, Decision 7).
///
/// The content decides which blank state a miss degrades to, not the miss. With no content
/// there was no body, and "no body" is true. With content there was a body, and the capture
/// simply never ran — so "no body" would state something false about a real one (Decision 8).
///
/// Public so the miss is testable without an FSI value to reflect over.
let capturedBodyFor (requestMessage: HttpRequestMessage) : CapturedBody =
    match tryGetCapturedBody requestMessage with
    | Some body -> body
    | None ->
        match requestMessage.Content with
        | null -> NoBody
        | _ -> NotCaptured uncapturedBodyReason

/// Maps a captured body onto the wire's three-state `bodyState` / `bodyBase64` / `bodyReason`
/// triple (docs/spec/0012-request-as-sent.md, Decision 10). Only the matching field carries a
/// value; the others are empty strings.
let private bodyToWire (body: CapturedBody) : string * string * string =
    match body with
    | NoBody -> NoneState, "", ""
    | Captured bytes -> CapturedState, Convert.ToBase64String bytes, ""
    | NotCaptured reason -> NotCapturedState, "", reason

/// The inverse of `bodyToWire`. An unrecognized state is a defect on our own wire — both ends
/// are this module — so it throws rather than decaying to `NoBody`, which would tell the user
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

    // Method, URL, and headers come from the BCL `HttpRequestMessage` on the Response. The
    // body cannot — `HttpClient` has already disposed the content — so it is looked up from
    // the capture table by the same message instance (docs/spec/0012-request-as-sent.md,
    // Decisions 1-2 and 7). A null `requestMessage` is not expected on a successful Run; fail
    // loudly rather than invent an empty request.
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

        // Lines 1 to setupLineCount of `combinedSetup` are native source (the target's own
        // block among them — Decision 1). Anything the addendum reports past them has no source
        // position (see `setupDiagnostic`).
        let setupLines = setupText.Split('\n')
        let setupLineCount = setupLines.Length

        let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
        let args = [| "fsi.exe"; "--noninteractive"; "--nologo" |]
        use inReader = new IO.StringReader("")

        // The session is collectible. The companion is long-lived and creates one session per
        // Run, so the runtime must reclaim each session's own dynamically-compiled user-code
        // assembly after disposal, instead of an accumulation for the life of the process.
        //
        // NOTE: `collectible` isolates only the per-session dynamic assembly. It does not
        // isolate the package assemblies that `#r "nuget:"` resolves, which load into the
        // process-wide default AssemblyLoadContext and outlive the session. Two in-process Runs
        // that pin *different* versions of the same package would collide there ("Could not
        // load type … from assembly …"). `run` prevents that, because it never enters this path
        // for a conflicting pin. It routes such a Run to a throwaway `--worker` child process
        // whose ALC ends with it. The target's pins are therefore safe to load in-process here.
        use session =
            FsiEvaluationSession.Create(fsiConfig, args, inReader, Console.Error, Console.Error, collectible = true)

        // When the extension host supplies the script's absolute path, both evals use FSI's
        // `scriptFileName` overload so `__SOURCE_DIRECTORY__` and `__SOURCE_FILE__` resolve to
        // the script's own directory and name. An untitled buffer omits the path and keeps
        // FSI's default (`input.fsx` under the process working directory).
        //
        // The path also re-bases FSI's own relative resolution: a `#load "sibling.fsx"` or a
        // `#r "lib.dll"` in the Setup then resolves beside the script, rather than beside the
        // companion. That is the same correctness the two symbols buy, and a saved script needs
        // it for the same reason.
        let evalInteraction code =
            match scriptFileName with
            | Some path -> session.EvalInteractionNonThrowing(code, path)
            | None -> session.EvalInteractionNonThrowing(code)

        let evalExpression code =
            match scriptFileName with
            | Some path -> session.EvalExpressionNonThrowing(code, path)
            | None -> session.EvalExpressionNonThrowing(code)

        // The bound the invocation's `Config.update` actually applied, in ms, with 0 for "no
        // bound". It is read after the fact, because `Option.orElse` can keep a timeout the
        // block set for itself, and that number — not the injected one — is what the timeout
        // message must name. A session that cannot produce the value falls back to the injected
        // bound, which is the right answer in every case but the block's own.
        let readAppliedTimeoutMs () =
            match evalExpression appliedTimeoutBinding with
            | Choice1Of2(Some v), _ ->
                match v.ReflectionValue with
                | :? float as ms -> int ms
                | _ -> timeoutMs
            | _ -> timeoutMs

        let setupResult, setupDiags = evalInteraction combinedSetup

        // `Choice1Of2` means only that the Setup threw no exception; it does not mean the
        // Setup has no errors. FSI can return `Choice1Of2` for a Setup that fails to parse or
        // type-check, and discard the failure into the diagnostics array instead of the
        // `Choice`. The array is therefore the only reliable signal, and it is read *before*
        // the `Choice`, independent of which branch the `Choice` took. A Setup failure also
        // stops the Run: evaluating the block against a Setup that never took effect only
        // produces a second, misleading error. See
        // `docs/spec/0001-report-setup-compile-error.md`, Decisions 1 and 2.
        //
        // The target block's own text is now *inside* this interaction (Decision 1), so its
        // diagnostics arrive here too. `splitDiagnostic` tells a fault in the user's block from
        // a fault in the Setup around it (Decision 8), by whether the diagnostic's start
        // position — unshifted back past the R1 insertion, if any — lands inside the block's own
        // span.
        //
        // Case 11c (Decision 7 of docs/spec/0003-lens-tells-the-truth.md) runs first, on the raw
        // Setup errors and in Setup-interaction coordinates: it needs the diagnostic's own range
        // read back against `setupLines`, which `splitDiagnostic` has already translated away by
        // the time its own list exists. A match here refuses the whole Run, and the invocation
        // below never runs.
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
                    // Bracket the invocation alone. That is the number the status line shows as
                    // the request time. Session creation and Setup sit outside it
                    // (docs/spec/0004-run-path-robustness.md, Decision 7).
                    let sw = Stopwatch.StartNew()
                    let targetResult, targetDiags = evalExpression (invocationText timeoutMs target)
                    sw.Stop()
                    let requestMs = sw.Elapsed.TotalMilliseconds

                    match targetResult with
                    | Choice2Of2 ex ->
                        // The invocation is the companion's own generated text, with no user-source
                        // position of its own, so every diagnostic here gets the Setup treatment,
                        // anchored at the top of the script (`realLineCount = 0` forces the anchor
                        // on every line). `targetDiagnostic` is gone: there is no longer a separate
                        // block interaction to map a native position back from.
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

// ---------------------------------------------------------------------------------------------
// Multi-version isolation. The assemblies that `#r "nuget:"` resolves load into the
// process-wide default AssemblyLoadContext, and they outlive each per-Run FSI session. A Run
// that pins a version of a package collides when an earlier in-process Run already loaded that
// package at a *different* version. The fix keeps the warm in-process fast path
// (`runInProcessDirect`). Only when a pin conflicts does it delegate that one Run to a
// short-lived `--worker` child process, which is a fresh process with a fresh ALC that ends
// with it. `run` is the router, and everything below serves it.
// ---------------------------------------------------------------------------------------------

/// Serializes a `RunOutcome` to the same tagged wire shape that the host-facing `run`, `ok`,
/// `compileError`, and `runtimeError` envelope uses. The `--worker` child emits its outcome
/// over this shape, and `RequestHandler` emits the host's response over it. The two channels
/// therefore cannot drift apart.
let outcomeToWire (outcome: RunOutcome) : obj =
    match outcome with
    | Ok(request, response) ->
        // Prefixed: the response carries a `bodyBase64` of its own, and one identifier must
        // not stand for two different bodies in the one expression below.
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
/// *different* version would collide with it in-process (ADR-0006). This is public for the
/// routing unit tests.
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

// The package ids resolved into this process's default ALC so far, as id -> what loaded it.
// Nuget ids are case-insensitive. A lock guards the map. The companion's request loop is serial
// today, but the state is process-global and cheap to make safe for a future concurrent caller.
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
/// are one atomic step. The lock is taken once for each logical operation, not once for each
/// access. A check in one lock scope, followed by a mark in another scope, leaves a TOCTOU gap.
/// A future concurrent caller could load a conflicting version in that gap (coding-standards
/// rule 4). The request loop is serial today, and the lock exists to stay correct when it is not.
///
/// The reservation happens *before* the evaluation runs, not after a successful load, and this
/// is deliberate. The map is a conservative over-approximation of what the shared ALC can hold.
/// A Run that reaches the in-process path can resolve its `#r "nuget:"` into that ALC, and the
/// resolved assembly then outlives the session even when the evaluation compile-errors or
/// throws. An over-mark of a Run that never loaded only over-routes a *later* Run to a safe,
/// cold worker. An under-mark of a Run that did load reopens the "Could not load type … from
/// assembly …" ALC collision (ADR-0006). The two errors are not symmetric, so we mark up front.
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
            // The map records a version-less `#r` as `Versionless`, and does *not* skip it, so
            // it still takes part in a later Run's conflict detection.
            for pkg, ver in pins do
                loadedVersions.[pkg] <-
                    match ver with
                    | Some v -> Pinned v
                    | None -> Versionless

            InProcess)

/// The bound on the time that a `--worker` child can take to produce its response frame. After
/// this time the Run terminates by force (coding-standards rule 3: every external process gets
/// a bounded wait and a kill path). The bound is long enough to absorb a cold first-run
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
/// handle but does not unblock a wait. The bound and the kill, not the disposal, are what
/// guarantee that the Run always terminates.
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

            // Best-effort teardown. A worker in the middle of a restore can spawn child
            // `dotnet` processes, so take the whole tree. Never throw out of a kill, because
            // the process can be gone already.
            let kill () =
                try
                    if not proc.HasExited then
                        proc.Kill(entireProcessTree = true)
                with _ ->
                    ()

            // One shape, always. `Envelope.getOptionalStringProp` reads the empty string as
            // "no value", so the absent case needs no second record to construct here.
            // `timeoutMs` on the payload is the request bound. A missing field reads as 0
            // through `getIntProp`, which means do not inject.
            let request: obj =
                {| source = source
                   blockIndex = blockIndex
                   scriptFileName = defaultArg scriptFileName ""
                   timeoutMs = timeoutMs |}

            writeFrame proc.StandardInput.BaseStream (JsonSerializer.SerializeToUtf8Bytes request)
            proc.StandardInput.Close()

            // The frame read blocks with no native timeout, so cap it on a worker thread. A
            // *crashed* child closes stdout without a frame -> None -> a clean runtimeError. A
            // *hung* child produces nothing at all, so the read never returns. `Wait timeoutMs`
            // caps that case, and we Kill() on expiry instead of a block here forever. The kill
            // closes the pipe, so the read task then completes as None and releases its thread.
            // The child's stderr (FCS and user output) inherits ours, so no drain is necessary.
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

                // The frame is in hand, so the child must now exit on its own. Bound that wait
                // too, because a worker that emitted its frame and then stalled must not block
                // `WaitForExit`. Kill() the child if it stays past the bound.
                if not (proc.WaitForExit workerWaitMs) then
                    kill ()

                outcome
    with ex ->
        RuntimeError(sprintf "worker: %s" ex.Message)

/// Runs the located block at `blockIndex` (0-based, source order) and returns its outcome.
///
/// The gate runs first (Decision 5 of docs/spec/0003-lens-tells-the-truth.md): a target that
/// `classify` refuses returns `Refused` here, before `routeAndReserve` marks any pin and before
/// any worker process starts. `routeAndReserve` marks each of the Run's pins in `loadedVersions`
/// up front, before any evaluation, so a refusal that reached it would mark pins that no session
/// ever loads, and slow a later Run for nothing.
///
/// An out-of-range `blockIndex` has no target route. `runLocated` refuses it as a stale lens.
///
/// The gate has to locate the blocks to decide, so the in-process path takes that same list on
/// to `runLocated`. A Run parses the source once in this process, not once per stage. The
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
