// What a phase cost and what it was allowed to cost. Kept apart from the harness so the budget
// vocabulary and the table rendering have one home, and the harness only records observations.
module Timing

/// One row of the timing table: a named phase, the time it took, and the budget it had.
type PhaseTiming =
    { Name: string
      ElapsedMs: float
      BudgetMs: float }

let private budgetSeconds (timing: PhaseTiming) = int (timing.BudgetMs / 1000.0)

let overBudget (timing: PhaseTiming) = timing.ElapsedMs > timing.BudgetMs

/// Names the phase, its budget, and the observed elapsed time, so a drifting run says which of
/// the three it was without a reader having to consult the table.
let budgetFailure (timing: PhaseTiming) =
    sprintf "%s exceeded the %i s budget (observed %.0f ms)" timing.Name (budgetSeconds timing) timing.ElapsedMs

/// `caption` heads the table, because a run emits more than one — the job summary appends them,
/// and two bare tables in a row read as one.
let renderTable (caption: string) (rows: PhaseTiming list) =
    let body =
        rows
        |> List.map (fun row -> sprintf "| %s | %.0f ms | %.0f ms |" row.Name row.ElapsedMs row.BudgetMs)
        |> String.concat "\n"

    sprintf "#### %s\n\n| Phase | Elapsed | Budget |\n| --- | ---: | ---: |\n%s\n\n" caption body
