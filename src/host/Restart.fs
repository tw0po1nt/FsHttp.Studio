module Restart

type Phase =
    | Spawned
    | Stopping

/// `Epoch` numbers the Companion that the Client currently owns. The Companion and its start
/// sequence tag every event with the epoch they started under.
type Session = { Epoch: int; Phase: Phase }

type Decision =
    | NotifyRestarting
    | BeginRestart

let initial: Session = { Epoch = 0; Phase = Spawned }

/// A command during the stop gives a notice and changes nothing. Otherwise the epoch moves on at
/// once, so every later event of the old Companion is stale.
let onCommand (session: Session) : Session * Decision =
    match session.Phase with
    | Stopping -> session, NotifyRestarting
    | Spawned ->
        { Epoch = session.Epoch + 1
          Phase = Stopping },
        BeginRestart

/// The old Companion is gone and the new start sequence runs.
let onStopped (session: Session) : Session = { session with Phase = Spawned }

let accepts (session: Session) (epoch: int) : bool = session.Epoch = epoch
