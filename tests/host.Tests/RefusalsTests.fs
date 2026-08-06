module Extension.Tests.RefusalsTests

// Seam 3 of docs/spec/0003-lens-tells-the-truth.md: drives the host's map from a wire refusal
// code to its shipped words, with no VSCode and no companion process.

open Expecto
open Refusals

let private wireCodes =
    [ "loopBody"
      "ifBranch"
      "matchClause"
      "exceptionHandler"
      "needsArguments"
      "classMember"
      "innerBinding"
      "lambdaValue"
      "noNameToCall"
      "tupleBinding"
      "insideAnotherRequest"
      "unaddressable" ]

[<Tests>]
let forCodeTests =
    testList
        "Refusals.forCode"
        [ test "each of the twelve codes maps to a non-empty, distinct title and a non-empty detail" {
              let refusals = wireCodes |> List.map forCode

              refusals
              |> List.iter (fun r ->
                  Expect.isNotEmpty r.Title "each code needs a lens title"
                  Expect.isNotEmpty r.Detail "each code needs a toast/detail sentence")

              Expect.equal
                  (refusals |> List.map (fun r -> r.Title) |> List.distinct |> List.length)
                  wireCodes.Length
                  "two codes that share a title would hide one verdict behind the other"
          }

          test "an unrecognized code degrades to the unaddressable title and detail, and does not throw" {
              let unknown = forCode "somethingLaterVersionsAdd"
              let unaddressable = forCode "unaddressable"

              Expect.equal unknown.Title unaddressable.Title "an unknown code shows the unaddressable title"
              Expect.equal unknown.Detail unaddressable.Detail "an unknown code shows the unaddressable detail"
          }

          test "every lens title carries the refusal glyph, and not the run triangle" {
              wireCodes
              |> List.iter (fun code ->
                  let r = forCode code
                  Expect.stringStarts r.Title "⊘" (sprintf "%s must not promise a Run it cannot honor" code))
          } ]

[<Tests>]
let unboundBlockValueDetailTests =
    testList
        "Refusals.unboundBlockValueDetail"
        [ test "names the blanked binding" {
              Expect.stringContains (unboundBlockValueDetail "dexId") "dexId" "the sentence must name the value"
          } ]

[<Tests>]
let titleTests =
    testList
        "Refusals.title"
        [ test "strips the glyph from each code's lens title, in sentence form" {
              wireCodes
              |> List.iter (fun code ->
                  let expected = (forCode code).Title.Replace("⊘ ", "")
                  Expect.equal (title code) expected (sprintf "%s must keep its lens title, minus the glyph" code)
                  Expect.isFalse ((title code).StartsWith "⊘") (sprintf "%s must not carry the glyph" code))
          }

          test "unboundBlockValue gets a heading, though it has no lens title" {
              Expect.isNotEmpty (title "unboundBlockValue") "unboundBlockValue needs a viewer heading"
          } ]
