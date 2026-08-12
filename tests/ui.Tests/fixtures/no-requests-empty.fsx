// Fixture for the no-requests lens: a clean script with no `http { }` block. ParseFailed is
// false and the locator finds nothing, so no lens appears at all
// (docs/spec/0014-explain-missing-lenses.md, Decision 1). The line-1 lens does not change this
// row: a script that parses cleanly has painted no lens since before that lens existed.

let x = 1
