// Shared harness primitives for the UI test suite. Checks import this module instead of inventing
// their own wait loops or deadline constants.
module Harness

open Fable.Core

/// Default wait for a CodeLens to appear in the workbench.
let LensAppearanceDeadlineMs = 45_000

/// Default wait for the response viewer to update after a Run.
let ViewerUpdateDeadlineMs = 30_000

/// Default wait for a toast notification.
let ToastDeadlineMs = 15_000

/// Default wait for the editor to recover after a reload.
let PostReloadRecoveryDeadlineMs = 60_000

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

let eventually (timeoutMs: int) (intervalMs: int) (predicate: unit -> bool) : Async<unit> =
    let rec loop (deadline: float) =
        async {
            if predicate () then
                return ()
            elif nowMs () >= deadline then
                Assert.fail (sprintf "eventually timed out after %i ms" timeoutMs)
            else
                do! Async.Sleep intervalMs
                return! loop deadline
        }

    async {
        let deadline = nowMs () + float timeoutMs
        return! loop deadline
    }
