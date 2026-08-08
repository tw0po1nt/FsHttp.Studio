"use strict";

module.exports = {
  // Fable.Mocha calls the global `describe` and `it`. Under Mocha's `tdd` UI the suite fails with
  // `describe is not defined`, so this setting is mandatory rather than a restatement of a default.
  ui: "bdd",
  // Spec 0005 requires the Mocha per-test timeout to sit above the largest harness deadline
  // (PostReloadRecoveryDeadlineMs, 60 s). Mocha's 2 s default would kill a waiting check with an
  // illegible timeout before `eventually` ever reported which surface never arrived.
  timeout: 120000,
};
