// The nine shapes the 12-case matrix does not have, plus the two blanking hazards (C3, C4) and
// an explicit probe for the C1 truncation boundary. Every block is annotated with the verdict
// the matrix assigned it, so the harness grid can be read against the decision directly.
//
// Order matters: the last two blocks exist to be run *after* every hazard above them, so their
// Setup has to blank all of it and still compile.

#r "nuget: FsHttp, 15.0.3"

open FsHttp
open System


// [S] private binding -- R2 + C6 (`private` blanked to spaces, not refused)
let private secret =
    http {
        GET "http://127.0.0.1:8391/api/v2/secret"
    }


// [C7] internal binding -- the matrix flagged this unverified. Deliberately NOT blanked by
// RunPlan (accessRange matches `private` only), so this row reports whether `internal` needs the
// same treatment or none at all.
let internal semiSecret =
    http {
        GET "http://127.0.0.1:8391/api/v2/semi-secret"
    }


// [S] bare block in a private module -- R1 + C6 one level up
module private Vault =
    http {
        GET "http://127.0.0.1:8391/api/v2/vault"
    }


// [S] nested modules -- R2 qualified by the reversed NestedModule chain
module Outer =
    module Inner =
        let deep =
            http {
                GET "http://127.0.0.1:8391/api/v2/deep"
            }


// [S] attributed binding -- the decl range includes the attribute line
[<Obsolete("prototype")>]
let attributed =
    http {
        GET "http://127.0.0.1:8391/api/v2/attributed"
    }


// [F2] class member -- needs an instance. Also the C4 hazard: blanking this as a *sibling* must
// not erase the whole type definition.
type Api() =
    member _.Get() =
        http {
            GET "http://127.0.0.1:8391/api/v2/member"
        }


// [F2] parameterized function -- no arguments we can invent
let multi (a: string) (b: int) =
    http {
        GET $"http://127.0.0.1:8391/api/v2/{a}/{b}"
    }


// [F3] lambda-valued binding -- the binding's value is the lambda, not the block
let lambdaValued =
    fun () ->
        http {
            GET "http://127.0.0.1:8391/api/v2/lambda"
        }


// [F3] inner let -- not module-scoped
let innerLet () =
    let x =
        http {
            GET "http://127.0.0.1:8391/api/v2/inner"
        }

    x


// [F1] match clause -- runtime-decided
let matched =
    match DateTime.Now.Hour with
    | 0 ->
        http {
            GET "http://127.0.0.1:8391/api/v2/matched"
        }
    | _ ->
        http {
            GET "http://127.0.0.1:8391/api/v2/matched-other"
        }


// [F1] try/with handler -- runtime-decided
let tried =
    try
        http {
            GET "http://127.0.0.1:8391/api/v2/tried"
        }
    with _ ->
        http {
            GET "http://127.0.0.1:8391/api/v2/caught"
        }


// [C3] tuple binding -- both blocks share one statement range. Running either must not blank
// the statement that contains it.
let tupleA, tupleB =
    http {
        GET "http://127.0.0.1:8391/api/v2/tuple-a"
    },
    http {
        GET "http://127.0.0.1:8391/api/v2/tuple-b"
    }


// [C3] a block nested inside another block. The inner one's blank span is the OUTER binding's
// declaration -- which *contains* the target. Blanking it would erase the very block being run.
// This is the hazard C3 names, in the only shape where it is live: both blocks runnable-ish and
// one span containing the other. Run with PROTO_NO_C3=1 to watch it break.
let nested =
    http {
        GET "http://127.0.0.1:8391/api/v2/nested"
        header "X-Inner" (string (sprintf "%A" (http { GET "http://127.0.0.1:8391/api/v2/inner-block" })).Length)
    }


// [S] the sweeper -- a plain bare block whose Setup has to blank every hazard above it.
http {
    GET "http://127.0.0.1:8391/api/v2/sweeper"
}


// [S + C1] a bare block inside a module, with a non-Block side effect after it but still inside
// the module. This is the exact shape that killed the "truncate at the enclosing top-level
// statement" boundary: a module is one statement, so that boundary would run the side effect.
// Truncating at the block's own expression end must drop it. If the harness ever sees a request
// to /SIDE-EFFECT, C1 is wrong.
module Tail =
    http {
        GET "http://127.0.0.1:8391/api/v2/tail"
    }

    System.Net.Http.HttpClient().GetStringAsync("http://127.0.0.1:8391/api/v2/SIDE-EFFECT").Result
    |> ignore
