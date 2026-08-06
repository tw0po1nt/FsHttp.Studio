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

          test "a title is the sentence alone, and carries no glyph" {
              wireCodes
              |> List.iter (fun code ->
                  let r = forCode code
                  Expect.isFalse (r.Title.Contains "⊘") (sprintf "%s owns the sentence, and the lens owns the glyph" code))

              Expect.equal (forCode "loopBody").Title "Cannot run: inside a loop" "the sentence is what the viewer heads its notice with"
          } ]

[<Tests>]
let lensTitleTests =
    testList
        "Refusals.lensTitle"
        [ test "every lens title carries the refusal glyph, and not the run triangle" {
              wireCodes
              |> List.iter (fun code ->
                  Expect.stringStarts (lensTitle code) "⊘ " (sprintf "%s must not promise a Run it cannot honor" code))
          }

          test "a lens title is the glyph and then the code's sentence" {
              Expect.equal (lensTitle "loopBody") "⊘ Cannot run: inside a loop" "the lens shows the glyph and the sentence"
          } ]

[<Tests>]
let forRefusedTests =
    testList
        "Refusals.forRefused"
        [ test "a catalog code takes its heading and detail from the catalog" {
              wireCodes
              |> List.iter (fun code ->
                  Expect.equal (forRefused code None) (forCode code) (sprintf "%s is a catalog refusal" code))
          }

          test "unboundBlockValue gets its own heading and names the blanked binding" {
              let r = forRefused "unboundBlockValue" (Some "dexId")

              Expect.equal r.Title "Cannot run: depends on another request" "unboundBlockValue needs a heading of its own"
              Expect.stringContains r.Detail "dexId" "the sentence must name the value"
              Expect.notEqual r.Title (forCode "unaddressable").Title "a bound-value refusal is not a position refusal"
          }

          test "unboundBlockValue without a name degrades whole, and not half" {
              let r = forRefused "unboundBlockValue" None

              Expect.equal r (forCode "unaddressable") "a heading and a detail must never come from two different refusals"
          } ]
