# Restart companion in both Clients

Spec for v0.3 shared feature 3 of 3. VSCode gets the palette command "FsHttp.Studio: Restart
companion", and Neovim gets `:FsHttp restart`. The map
[FsHttp.Studio v0.3: Neovim support and shared features](https://github.com/tw0po1nt/FsHttp.Studio/issues/239)
holds the decisions, and the ticket
[How does a restart of the companion work in each client?](https://github.com/tw0po1nt/FsHttp.Studio/issues/258)
holds the detail.

## Problem Statement

**A stopped companion needs a window reload.** When the companion stops, each lens says
"Cannot run: the companion stopped", and the toast says "Reload the window to start it again". A
reload closes the Response viewer and each panel of each other extension.

**A Run that loops forever has no way out.** The companion answers one request at a time. A user
block that loops forever holds the companion, and each later locate and Run waits behind it.

**The process of a worker stays alive.** A Run with a conflicting `#r "nuget:"` pin runs in a
`--worker` child (ADR-0006). The Client kills the companion process only. A worker that loops
forever stays alive after a window reload, and after VSCode closes.

**A new .NET SDK needs a reload.** The SDK check runs once, at activation. A user who installs an
SDK, or corrects `fshttpStudio.dotnetPath`, must reload the window.

## Solution

**Each Client gets a restart.** VSCode gets the palette command "FsHttp.Studio: Restart companion".
Neovim gets `:FsHttp restart`. A restart stops the companion with each process that it started,
and then runs the full start sequence again.

- The restart works in each companion state.
- A Run in progress ends as a Runtime error with its own sentence.
- Each lens and Block mark says "the companion is starting" during the restart.
- The stopped toast, and a click on the stopped Status line text, start a restart.
- A Client never restarts the companion by itself.

The companion and the envelope do not change. No setting is added.

## User Stories

1. As a user whose Run loops forever, I want to restart the companion, so that I end the Run with no window reload.
2. As a user, I want a restart to stop the worker of the companion too, so that no process stays alive after the restart.
3. As a user who closes VSCode or Neovim, I want the worker of the companion to stop too, so that no process stays alive.
4. As a user, I want a Run that the restart ended to say so, so that I know to run it again.
5. As a user, I want each lens and Block mark to say "the companion is starting" during the restart, so that I do not think the companion stopped.
6. As a user, I want the Status line text to show `starting…` at once, so that I see the restart begin.
7. As a VSCode user, I want a "Restart companion" button on the stopped toast, so that I recover with one click.
8. As a user, I want the stopped sentence to name the restart command of my editor, so that I know what to run.
9. As a user, I want a click on the stopped Status line text to restart the companion, so that I recover from where I see the problem.
10. As a user, I want a click on a Ready Status line text to do nothing, so that a click by accident never ends a Run.
11. As a VSCode user who installs a .NET SDK, I want a restart to check the SDK again, so that I need no window reload.
12. As a VSCode user who changes `fshttpStudio.dotnetPath`, I want an INFO toast with a "Restart companion" button, so that I can apply the change.
13. As a Neovim user, I want `:FsHttp restart` to retry a failed download, so that I recover from a network failure.
14. As a user who runs the restart two times fast, I want the second command to wait, so that two stops do not race.
15. As a user whose new companion hangs at start, I want a restart to work again, so that I can recover.

## Implementation Decisions

### 1. The command

- VSCode: the palette command "FsHttp.Studio: Restart companion", with the id
  `fshttpStudio.restartCompanion`.
- Neovim: `:FsHttp restart`, under the `:FsHttp` command of spec 0015.
- The command works in each companion state. On a live companion, it stops the companion first.
- A Client never restarts the companion by itself. A change to a path option gives a notice only.

### 2. The stop

The restart, window close in VSCode, and `VimLeavePre` in Neovim use the same stop.

1. The Client closes the stdin of the companion. An idle companion exits by itself, because its
   read loop ends at the end of stdin.
2. After 1000 ms, the Client kills the full process tree of the companion.
   - On Linux and macOS, the Client spawns the companion detached, in its own process group. The
     kill signals the negative pid, so it reaches each process in the group.
   - On Windows, the kill runs `taskkill /T /F /PID <pid>`.
3. After the command, the Client ignores each event of the old companion. A late `exit` of the old
   companion never writes the Stopped state over the new start.

The tree kill is necessary. A companion can start a `--worker` child, and a worker can start
`dotnet` restore children. A kill of the companion alone leaves a worker that loops forever.

### 3. The start

- The restart runs the full start sequence.
- In VSCode, it reads `fshttpStudio.dotnetPath` again and runs `dotnet --list-sdks` again. Thus a
  restart clears the SDK-not-found state with no reload.
- In Neovim, it also retries a failed download, a missing release, and a `companion_path` with no
  companion. A command during a download kills `curl` and starts the sequence again. The download
  writes under a temporary name, so the data folder stays clean.
- The new companion gets the version check of spec 0015 again.

### 4. A Run that the restart ends

- The Run in progress ends as a Runtime error with a new sentence from `Refusals.fs`:
  "The Run ended because the companion restarted. Run the request again."
- The Client abandons each waiting Run in the pending queue. Only the newest Run renders, because
  of the generation counter of the Run command. Thus a waiting Run needs no text. The Neovim client
  keeps the same rule.
- With no Run in progress, the Response viewer and the Response buffer keep the last result.

### 5. A restart during a restart

- While the Client stops the old companion (1000 ms at most), a second command gives the INFO
  notice "The companion is restarting." The command does nothing more.
- After the new companion spawns, a command restarts again. Thus a user can recover from a start
  that hangs.

### 6. Lenses and Block marks during the start

- While the companion is Starting, each remembered block keeps its lens or Block mark. The title is
  a new entry in `Refusals.fs`: `⊘ Cannot run: the companion is starting`. A click does nothing.
- The title follows the Starting state, so one rule covers each restart. A first start remembers no
  block, so it paints nothing, as today.
- Today the stopped lenses show in each state that is not Ready. After this change, the stopped
  title shows in the Stopped state and in each failed start state.
- When the new companion is ready, VSCode asks for each visible lens again. The Neovim client
  locates each open script again.

### 7. Status line text

- The Client sets the Starting state when the command runs. The VSCode status bar and lualine show
  `starting…`.
- `Protocol.State` gets no new case. Thus `Protocol.statusText`, its Golden fixture, and the lualine
  rows do not change.
- In Neovim, the start sequence can pass through the download rows, as at a first start.

### 8. The stopped sentence

- `Refusals.companionStopped` becomes one template: "The FsHttp.Studio companion stopped. Run
  {command} to start it again."
- VSCode fills in `FsHttp.Studio: Restart companion`. `generate-lua.fsx` fills in `:FsHttp restart`
  when it writes `refusals.lua`.
- The toast from a stopped lens gets a "Restart companion" button. The error in the Response viewer
  has no button, as today.

### 9. A click on the Status line text

- A click restarts the companion in the Stopped state and in each failed start state.
  - In VSCode, the failed start state is SDK not found.
  - In Neovim, the failed start states are SDK not found, download failed, no release, and
    companion not found.
- In these states, the VSCode item gets the restart command and the tooltip "Restart companion".
  The lualine component gets the same rule through `on_click`.
- In each other state, a click does nothing.

### 10. A change to the dotnet path in VSCode

- A change to `fshttpStudio.dotnetPath` while a companion runs gives an INFO toast with a "Restart
  companion" button.
- This matches the Neovim INFO notice for `dotnet_path` and `companion_path` in spec 0015.

### 11. The glossary

`CONTEXT.md` holds the term **Restart**: the command of the user that stops the companion with each
process that the companion started, and then starts a new companion.

## Testing Decisions

### What makes a good test

A Check asserts what the user sees and which processes exist: the text in the Response viewer or
the Response buffer, the lens titles, the toast, and the pids. A rule that depends on the window of
1000 ms goes to a unit test, because a UI wait on that window is not reliable.

### UI suite

1. **Restart under a hang.** Run the hang block, then run "FsHttp.Studio: Restart companion" from
   the palette. The Run shows the restart sentence, the old companion pid is gone, each lens can
   start a Run again, and a recovery Run succeeds.
2. **Restart from the stopped state.** Kill the companion, click a stopped lens, then click
   "Restart companion" in the toast.
3. **A worker under a hang.** A hang block with two conflicting `#r "nuget:"` pins runs in a
   worker. After the restart, the worker pid is gone.
4. **A click on the stopped Status line text.** Click the status bar item after the companion
   stops. A new companion starts.
5. **A change to the dotnet path.** Change `fshttpStudio.dotnetPath`. The INFO toast shows.

The existing companion death Check now recovers through the toast button instead of a window
reload.

### Neovim suite

1. **Restart under a hang.** Run the hang block, then run `:FsHttp restart`. The same assertions as
   the UI suite.
2. **Restart from the stopped state.** Kill the companion, run `:FsHttp run` for the stopped WARN,
   then run `:FsHttp restart`.
3. **A worker under a hang.** The same as the UI suite.
4. **A click on the stopped Status line text.** Call the `on_click` function of the lualine
   component after the companion stops.
5. **A change to the dotnet path.** Call `setup()` with a new `dotnet_path`. The INFO notice shows.

### host.Tests and the Lua core suite

- The starting title for a remembered block, and no mark for a first start.
- The INFO notice for a command during the stop.
- An event of the old companion after the command changes no state.
- The restart sentence, and the stopped template with each command name.

### Prior art

- The companion death Check of the UI suite (spec 0011), with its hang route and its pid checks.

## Out of Scope

- **An automatic restart.** Spec 0004 put it out of scope, and this spec adds a manual restart only.
- A setting for the stop window of 1000 ms.
- A stop envelope that asks the companion to stop its own children. It changes the envelope, and a
  Run that loops in-process cannot read it.
- A new `Protocol.State` case for the restart.

## Further Notes

- The VSCode half can start at once. The Neovim half waits for the start sequence and the Block
  marks of spec 0015.
- Spec 0017 shows the stopped toast with the button of this spec.
- No ADR conflict. ADR-0003 keeps "no companion, no runnable lenses", because the starting title
  starts no Run. ADR-0006 gets a stronger guarantee, because the tree kill stops the worker and its
  children.
