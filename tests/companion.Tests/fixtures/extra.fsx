// The shapes the twelve-case corpus does not have, plus both blanking hazards from Decision 5 of
// docs/spec/0002-reach-a-block-anywhere.md, and an explicit probe for the Decision 1 truncation
// boundary. Every block carries the verdict the spec's position table assigns it, so this file
// can be read against the decision directly.
//
// Order matters: the last two blocks exist to be run *after* every hazard above them, so their
// Setup has to blank all of it and still compile.

#r "nuget: FsHttp, 15.0.3"

open FsHttp
open System


// [13] private binding -- R2, with the `private` blanked to spaces rather than refused
let private secret = http { GET "http://127.0.0.1:8391/api/v2/secret" }


// internal binding -- deliberately NOT blanked, because `accessRange` matches `private` only.
// Decision 6 measured `internal` as accessible from a later interaction, so it needs no
// treatment. This block is the guard on that measurement.
let internal semiSecret = http { GET "http://127.0.0.1:8391/api/v2/semi-secret" }


// [14] bare block in a private module -- R1, with the module's `private` blanked one level up
module private Vault =
    http { GET "http://127.0.0.1:8391/api/v2/vault" }


// [15] nested modules -- R2, qualified by the enclosing module chain, outermost first
module Outer =
    module Inner =
        let deep = http { GET "http://127.0.0.1:8391/api/v2/deep" }


// [16] attributed binding -- the declaration range includes the attribute line
[<Obsolete("prototype")>]
let attributed = http { GET "http://127.0.0.1:8391/api/v2/attributed" }


// [22] class member -- F2, needs an instance. Also Decision 5's second hazard: blanking this as a
// *sibling* must not erase the whole type definition.
type Api() =
    member _.Get() =
        http { GET "http://127.0.0.1:8391/api/v2/member" }


// [19] parameterized function -- F2, no arguments we can invent
let multi (a: string) (b: int) =
    http { GET $"http://127.0.0.1:8391/api/v2/{a}/{b}" }


// [20] lambda-valued binding -- F3, the binding's value is the lambda, not the block
let lambdaValued = fun () -> http { GET "http://127.0.0.1:8391/api/v2/lambda" }


// [21] inner let -- F3, not module-scoped
let innerLet () =
    let x = http { GET "http://127.0.0.1:8391/api/v2/inner" }

    x


// [17] match clause -- F1, decided at run time
let matched =
    match DateTime.Now.Hour with
    | 0 -> http { GET "http://127.0.0.1:8391/api/v2/matched" }
    | _ -> http { GET "http://127.0.0.1:8391/api/v2/matched-other" }


// [18] try/with handler -- F1, decided at run time
let tried =
    try
        http { GET "http://127.0.0.1:8391/api/v2/tried" }
    with _ ->
        http { GET "http://127.0.0.1:8391/api/v2/caught" }


// [23] tuple binding -- F5. Both blocks share one declaration range, which is Decision 5's first
// hazard: running either must not blank the span that contains it.
let tupleA, tupleB =
    http { GET "http://127.0.0.1:8391/api/v2/tuple-a" }, http { GET "http://127.0.0.1:8391/api/v2/tuple-b" }


// [24] a block nested inside another block's expression -- F5. The inner one's blank span is the
// OUTER binding's declaration, which *contains* the target, so blanking it would erase the very
// block being run. This is Decision 5's first hazard in the only shape where it is live: one
// block runnable and one span containing the other.
let nested =
    http {
        GET "http://127.0.0.1:8391/api/v2/nested"
        header "X-Inner" (string (sprintf "%A" (http { GET "http://127.0.0.1:8391/api/v2/inner-block" })).Length)
    }


// The sweeper -- a plain bare block whose Setup has to blank every hazard above it.
http { GET "http://127.0.0.1:8391/api/v2/sweeper" }


// A bare block inside a module, with a non-Block side effect after it but still inside the
// module. This is the exact shape that ruled out the "truncate at the enclosing top-level
// statement" boundary: a module is one statement, so that boundary would run the side effect.
// Truncating at the block's own expression end must drop it, per Decision 1. A request to
// /SIDE-EFFECT means that boundary is wrong.
module Tail =
    http { GET "http://127.0.0.1:8391/api/v2/tail" }

    System.Net.Http.HttpClient().GetStringAsync("http://127.0.0.1:8391/api/v2/SIDE-EFFECT").Result
    |> ignore
