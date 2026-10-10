// Every user-visible sentence for a refused block lives here and nowhere else.
module Refusals

/// A refusal's shipped words: the short sentence that heads it, and the longer sentence that the
/// toast and the response viewer both show. `Title` carries no glyph: the lens prepends one
/// (`lensTitle`), and the response viewer's notice shows the sentence alone.
type Refusal = { Title: string; Detail: string }

type Client =
    | VSCode
    | Neovim

/// `unaddressable` is both a code of its own and the fallback for an unrecognized code.
let private catalog: (string * Refusal) list =
    [ "loopBody",
      { Title = "Cannot run: inside a loop"
        Detail =
          "FsHttp.Studio cannot run a request inside a loop. A loop body describes many requests, and one Run sends one request. To run this request, bind it to a name outside the loop, then run that binding." }

      "ifBranch",
      { Title = "Cannot run: inside an if branch"
        Detail =
          "FsHttp.Studio cannot run a request inside an if branch. The script chooses the branch when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the if, then run that binding." }

      "matchClause",
      { Title = "Cannot run: inside a match clause"
        Detail =
          "FsHttp.Studio cannot run a request inside a match clause. The script chooses the clause when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the match, then run that binding." }

      "exceptionHandler",
      { Title = "Cannot run: inside a try block"
        Detail =
          "FsHttp.Studio cannot run a request inside a try block. The script chooses the handler when it runs. To run this request, bind it to a name outside the try, then run that binding." }

      "needsArguments",
      { Title = "Cannot run: this function needs arguments"
        Detail =
          "FsHttp.Studio cannot run a request in a function that takes arguments, because it has no values to supply. To run this request, move it to a binding that takes no arguments." }

      "classMember",
      { Title = "Cannot run: inside a class member"
        Detail =
          "FsHttp.Studio cannot run a request in a class member, because it has no instance of the class. To run this request, move it to a module-level binding." }

      "innerBinding",
      { Title = "Cannot run: inside a local binding"
        Detail =
          "FsHttp.Studio cannot run a request in a local binding. A local binding is not in scope after the script runs. To run this request, move it to a module-level binding." }

      "lambdaValue",
      { Title = "Cannot run: this binding is a function"
        Detail =
          "This binding is a function rather than a request. FsHttp.Studio sends the request only when your code calls the function. To run this request, bind it directly to a name." }

      "noNameToCall",
      { Title = "Cannot run: this binding has no name"
        Detail =
          "The pattern of this binding gives FsHttp.Studio no name to call. To run this request, bind it to a simple name." }

      "tupleBinding",
      { Title = "Cannot run: this binding binds two or more values"
        Detail =
          "This binding binds two or more values, so its value is not the request alone. To run this request, give it its own let binding." }

      "insideAnotherRequest",
      { Title = "Cannot run: inside another request"
        Detail =
          "This request is inside another request. FsHttp.Studio can run the outer request only. To run this request, move it to its own binding." }

      "unaddressable",
      { Title = "Cannot run in this position"
        Detail =
          "FsHttp.Studio cannot address a request in this position. To run this request, move it to its own let binding, at the top level of the script or of a module." } ]

/// Every Refusal code that has a catalog row, in catalog order.
let catalogCodes: string list = catalog |> List.map fst

let private table = catalog |> Map.ofList

let fallbackCode: string = "unaddressable"

let private fallback = table.[fallbackCode]

/// An unrecognized code degrades to `unaddressable` and never throws.
let forCode (code: string) : Refusal =
    table |> Map.tryFind code |> Option.defaultValue fallback

/// Belongs to the lens rather than to the sentence, so no caller has to strip it back off.
let private glyph = "⊘ "

/// The CodeLens title for a wire refusal code: the refusal's sentence behind the refusal glyph.
let lensTitle (code: string) : string = glyph + (forCode code).Title

/// Carries no wire code, because `classify` runs in the companion that stopped.
/// The lens toast and an abandoning Run both show `Detail`, so those two surfaces cannot drift.
let companionStopped: Refusal =
    { Title = "Cannot run: the companion stopped"
      Detail = "The FsHttp.Studio companion stopped. Reload the window to start it again." }

let companionStoppedLensTitle: string = glyph + companionStopped.Title

/// A Run outcome only. `classify` never produces it, so it has no lens and no `catalog` row.
let unboundBlockValue (name: string) : Refusal =
    { Title = "Cannot run: depends on another request"
      Detail =
        sprintf
            "This request uses `%s`, which another request in this script binds. One Run evaluates one request, so `%s` has no value. FsHttp.Studio cannot run a request that depends on another request."
            name
            name }

/// A stale lens has no block at its recorded index. It is a Run outcome only, so it has no
/// `catalog` row or lens title.
let staleBlockIndex (client: Client) : Refusal =
    { Title = "Cannot run: the script changed"
      Detail =
        match client with
        | VSCode ->
            "This request moved or was removed after you started the Run. FsHttp.Studio cannot find it at the position the lens recorded. To run this request, run it again from its lens."
        | Neovim ->
            "This request moved or was removed after you started the Run. FsHttp.Studio cannot find it at the position it had when the Run started. To run this request, run :FsHttp run again." }

/// The sentence for a script with a parse failure and no Block.
let noBlocksParseFailure: string =
    "No requests found: this script has a syntax error."

/// A lens title ends with no period, so this title drops the period of the sentence.
let noBlocksParseFailureLensTitle: string = glyph + noBlocksParseFailure.TrimEnd '.'

/// The CodeLens title for a Block that a Run can reach.
let runLensTitle: string = "▶ Run request"

/// The sentence for the command that runs the Block at the cursor, when there is no Active document,
/// or the Active document is not a Script.
let runAtCursorNeedsScript: string =
    "Open an F# script (.fsx) to run the request at the cursor."

/// The sentence for a script with no Block and no parse failure.
let noBlocksEmpty: string =
    "This script has no request. Write an http { } block to run one."

/// The one place that knows the outcome-only codes `catalog` omits, so a heading and a body can
/// never come from two different refusals.
let forRefused (code: string) (name: string option) : Refusal =
    match code, name with
    | "unboundBlockValue", Some blockedName -> unboundBlockValue blockedName
    | "staleBlockIndex", None -> staleBlockIndex VSCode
    | _ -> forCode code
