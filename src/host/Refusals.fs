// The shipped words for a refused block (docs/spec/0003-lens-tells-the-truth.md, Decisions 2 and
// 10). This is the one file that owns every lens title and every toast/viewer-detail sentence, so
// a wording change touches no process boundary: the companion ships a code alone
// (`Companion.BlockLocator.codeToWire`), and this module is where that code becomes a sentence a
// user reads.
module Refusals

/// A refusal's shipped words: the CodeLens title (glyph included) and the longer sentence that
/// the toast and the response viewer both show.
type Refusal = { Title: string; Detail: string }

/// One row per `classify` verdict (docs/spec/0003, Decision 2's table). `unaddressable` is both a
/// code of its own and the fallback that an unrecognized code degrades to.
let private catalog: (string * Refusal) list =
    [ "loopBody",
      { Title = "⊘ Cannot run: inside a loop"
        Detail =
          "FsHttp.Studio cannot run a request inside a loop. A loop body describes many requests, and one Run sends one request. To run this request, bind it to a name outside the loop, then run that binding." }

      "ifBranch",
      { Title = "⊘ Cannot run: inside an if branch"
        Detail =
          "FsHttp.Studio cannot run a request inside an if branch. The script chooses the branch when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the if, then run that binding." }

      "matchClause",
      { Title = "⊘ Cannot run: inside a match clause"
        Detail =
          "FsHttp.Studio cannot run a request inside a match clause. The script chooses the clause when it runs, so FsHttp.Studio cannot tell which request you want. To run this request, bind it to a name outside the match, then run that binding." }

      "exceptionHandler",
      { Title = "⊘ Cannot run: inside a try block"
        Detail =
          "FsHttp.Studio cannot run a request inside a try block. The script chooses the handler when it runs. To run this request, bind it to a name outside the try, then run that binding." }

      "needsArguments",
      { Title = "⊘ Cannot run: this function needs arguments"
        Detail =
          "FsHttp.Studio cannot run a request in a function that takes arguments, because it has no values to supply. To run this request, move it to a binding that takes no arguments." }

      "classMember",
      { Title = "⊘ Cannot run: inside a class member"
        Detail =
          "FsHttp.Studio cannot run a request in a class member, because it has no instance of the class. To run this request, move it to a module-level binding." }

      "innerBinding",
      { Title = "⊘ Cannot run: inside a local binding"
        Detail =
          "FsHttp.Studio cannot run a request in a local binding. A local binding is not in scope after the script runs. To run this request, move it to a module-level binding." }

      "lambdaValue",
      { Title = "⊘ Cannot run: this binding holds a function"
        Detail =
          "This binding holds a function, and not a request. FsHttp.Studio sends the request only when your code calls the function. To run this request, bind it directly to a name." }

      "noNameToCall",
      { Title = "⊘ Cannot run: this binding has no name"
        Detail =
          "The pattern of this binding gives FsHttp.Studio no name to call. To run this request, bind it to a simple name." }

      "tupleBinding",
      { Title = "⊘ Cannot run: this binding binds two or more values"
        Detail =
          "This binding binds two or more values, so its value is not the request alone. To run this request, give it its own let binding." }

      "insideAnotherRequest",
      { Title = "⊘ Cannot run: inside another request"
        Detail =
          "This request is inside another request. FsHttp.Studio can run the outer request only. To run this request, move it to its own binding." }

      "unaddressable",
      { Title = "⊘ Cannot run in this position"
        Detail =
          "FsHttp.Studio cannot address a request in this position. To run this request, move it to its own let binding, at the top level of the script or of a module." } ]

let private table = catalog |> Map.ofList

let private fallback = table.["unaddressable"]

/// The lens title and toast/detail text for a wire refusal code. An unrecognized code degrades to
/// `unaddressable` (docs/spec/0003, Decision 2) and never throws.
let forCode (code: string) : Refusal =
    table |> Map.tryFind code |> Option.defaultValue fallback

/// The `unboundBlockValue` detail text (docs/spec/0003, Decision 10). It is a Run outcome only:
/// `classify` never produces it, so it has no lens title and no row in `catalog`.
let unboundBlockValueDetail (name: string) : string =
    sprintf
        "This request uses `%s`, which another request in this script binds. One Run evaluates one request, so `%s` has no value. FsHttp.Studio cannot run a request that depends on another request."
        name
        name

/// The `unboundBlockValue` heading for the response viewer's `refused` notice (docs/spec/0003,
/// Decision 6). It has no lens and no `catalog` row, so it carries no glyph to strip.
let private unboundBlockValueTitle = "Cannot run: depends on another request"

/// The response viewer's `refused` heading for a wire refusal code (docs/spec/0003, Decision 6):
/// the matching lens title, with its `⊘ ` glyph removed, in sentence form. `unboundBlockValue` has
/// no lens title, so it uses its own heading instead of `forCode`.
let title (code: string) : string =
    if code = "unboundBlockValue" then
        unboundBlockValueTitle
    else
        (forCode code).Title.Replace("⊘ ", "")
