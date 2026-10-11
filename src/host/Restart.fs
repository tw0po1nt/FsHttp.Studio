module Restart

/// `Epoch` numbers the start of the Companion that the Client owns now.
type Session = { Epoch: int; Stopping: bool }

type Decision =
    | NotifyRestarting
    | BeginRestart

let initial: Session = { Epoch = 0; Stopping = false }

/// The epoch moves on at once, so every later event of the old Companion is stale.
let onCommand (session: Session) : Session * Decision =
    if session.Stopping then
        session, NotifyRestarting
    else
        { Epoch = session.Epoch + 1
          Stopping = true },
        BeginRestart

let onStopped (session: Session) : Session = { session with Stopping = false }

let onWindowClose (session: Session) : Session =
    { session with
        Epoch = session.Epoch + 1 }

/// `current` reads the session when the event arrives, because a Restart can move the epoch first.
let guard (current: unit -> Session) (epoch: int) (handler: 'T -> unit) : 'T -> unit =
    fun event ->
        if (current ()).Epoch = epoch then
            handler event
