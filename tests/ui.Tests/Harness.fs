// Shared harness primitives for the UI test suite. Checks import this module instead of inventing
// their own wait loops or deadline constants.
module Harness

open Fable.Core

/// Default wait for a CodeLens to appear in the workbench.
let LensAppearanceDeadlineMs = 45_000

/// Default wait for the response viewer to repaint after a Run.
let ViewerUpdateDeadlineMs = 30_000

/// Default wait for a toast notification.
let ToastDeadlineMs = 15_000

/// Default wait for the editor to recover after a reload.
let PostReloadRecoveryDeadlineMs = 60_000

/// Retry spacing between polls. Deliberately not a parameter: the deadline is what a check tunes,
/// and a per-call interval would put a magic number in every check body.
let PollIntervalMs = 250

[<Emit("Date.now()")>]
let private nowMs () : float = jsNative

/// Polls `predicate` until it holds or `timeoutMs` elapses. The predicate is async because every
/// observation of the running editor returns a promise; wrap a synchronous condition in
/// `async { return ... }`. `subject` names what is being waited on, so a timeout reads as the
/// surface that never arrived rather than a bare elapsed time.
let eventually (timeoutMs: int) (subject: string) (predicate: unit -> Async<bool>) : Async<unit> =
    let rec loop (deadline: float) =
        async {
            let! holds = predicate ()

            if holds then
                return ()
            elif nowMs () >= deadline then
                Assert.fail (sprintf "Timed out after %i ms waiting for %s" timeoutMs subject)
            else
                do! Async.Sleep PollIntervalMs
                return! loop deadline
        }

    async {
        let deadline = nowMs () + float timeoutMs
        return! loop deadline
    }
