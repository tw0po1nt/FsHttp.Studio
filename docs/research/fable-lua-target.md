# Research: the work to upstream a Lua target to Fable

Issue: [#251](https://github.com/tw0po1nt/FsHttp.Studio/issues/251), "What does it take to upstream a
Lua target to Fable?" Map: [#239](https://github.com/tw0po1nt/FsHttp.Studio/issues/239).

> **Scope.** This note gathers facts for the language decision in
> [#245](https://github.com/tw0po1nt/FsHttp.Studio/issues/245). The decision belongs to that
> ticket. All facts are current on 2026-10-06. Fable `main` is at commit `b9ce2fd` (2026-10-04),
> and the latest release is Fable 5.19.0 (2026-10-03) [F1]. The prior note on the plugin routes
> [R1] holds the facts on Fable's target list. This note does not repeat them.

Line counts in this note come from `wc -l` on Fable `main` at `b9ce2fd` and on the head of the Lua
spike, `e07d3d1` [F2]. A count marked "measured" comes from a command that this research ran on an
Apple M5 Pro with macOS 27.0.1, Neovim v0.12.5, and LuaJIT 2.1.

## The state of draft PR #3290

### What the PR holds

PR #3290, "Lua spike (placeholder)", is an open draft. It carries the work of the closed PR #2509
forward [F2][F3]. Its first commit is from 2021-08-21, and its last commit is from 2024-06-07.

| Part | Files | Lines |
| --- | --- | --- |
| Lua AST | `Lua/Lua.fs` | 73 |
| Transform | `Lua/Fable2Lua.fs` | 547 |
| Printer | `Lua/LuaPrinter.fs` | 369 |
| Compiler shim | `Lua/Compiler.fs` | 17 |
| Replacements | none | 0 |
| Interop | `Fable.Core.LuaInterop.fs` | 90 |
| Runtime, generated from TypeScript | `fable-library-lua/Util.lua` | 1629 |
| Runtime, F# | `Array.fs`, `Choice.fs`, `Native.fs`, `Global.fs`, `Timer.fs` | 1483 |
| Tests, F# | 5 test files, `Util.fs`, `Main.fs` | 396 |
| Vendored | `luaunit.lua`, two copies | 6825 |

- The vendored `luaunit.lua` copies hold 6825 of the 11,803 added lines [F2].
- `Util.lua` carries the line `Generated with https://github.com/TypeScriptToLua/TypeScriptToLua`.
  The commit `9d7e8758` says "Migrate to lua 5.2. Util generated from ts" [F2].
- The library project comments out `List.fs`, `Seq.fs`, `Map.fs`, `Set.fs`, `Range.fs`, the
  `BigInt` files, `Async.fs`, and the mutable collections [F2].
- The tests hold 48 `[<Fact>]` tests: 23 for arithmetic, 11 for control flow, 6 for unions, 4 for
  records, and 4 for arrays. They run through `lua` and `luaunit` [F2].
- The spike adds a `Lua` case to `Language` in `src/Fable.AST/Plugins.fs`, and it reports the status
  `experimental` [F2].

### What the transform lacks

`Fable2Lua.fs` emits an `Unknown` node, which the printer writes as a comment, for these cases [F2]:

- `LetRec`, `ObjectExpr`, `Curry`, `Debugger`, and every unresolved call.
- `ListHead`, `ListTail`, `UnionTag`, and `FieldSet`.
- Each binary operator other than the arithmetic and comparison operators. This includes the shift
  operators and the bitwise operators.
- Each unary operator other than `not`.
- Each `Logical` operation, which Fable uses for `&&` and `||`.

Other gaps:

- The spike has no `Replacements.fs`. `Replacements.Api.fs` sends a language without its own file
  to the JavaScript replacements, so each BCL call becomes a call into a JavaScript library module
  name [F2][F4].
- `Throw` becomes `error("There was an error, todo")`. The output drops the thrown value [F2].
- An array that is not a literal, for example from `Array.zeroCreate`, becomes an empty table [F2].
- The transform erases `Option`: `Some x` becomes `x`, and `None` becomes `nil` [F2].

### A compile of this repo's pure F# files (measured)

This research built the spike's `Fable.Cli` and compiled each of the three files as an `.fsx`
script. The current F# compiler rejects one line of `FSharp2Fable.fs` in the spike with
`FS0038`. The build ran with a one-line local rename at that line.

| Input | Compiler result | `loadfile` in LuaJIT |
| --- | --- | --- |
| A two-function smoke script | Lua output | Parses. `'hi ' + name` fails at run time with "attempt to perform arithmetic on a string value". The exported wrapper `mod.add` returns `nil`. |
| `src/host/Envelope.fs` | Lua output | Fails: `')' expected near '='`. The output assigns inside an `and`/`or` expression, prints each shift as a comment, uses the Lua 5.3 operator `&`, and calls `Uint8Array` and `.length`. |
| `src/host/Refusals.fs` | Lua output | Fails: `unfinished string`. The output prints the `catalog` list as the text `unknown NewList` and a dump of the Fable AST. |
| `src/host/Protocol.fs` | Compiler exception | None. `Fable2Lua.fs` line 317 indexes past the end of a list in `DecisionTreeSuccess`. |

The output for `Envelope.fs` and `Refusals.fs` calls `require` on library modules that the spike
does not have: `Map`, `Option`, `String`, and `Encoding`. These are the JavaScript library names
from the JavaScript replacements.

### Drift from `main`

- The merge base of the spike and `main` is `1a1854a` (2024-05-29). `main` holds 969 commits that
  the spike does not have [F2].
- A trial merge (`git merge-tree`) reports 17 files with conflicts. Ten are shared compiler, build,
  and editor files, for example `Plugins.fs`, `Entry.fs`, `Pipeline.fs`, and `Replacements.Api.fs`.
  Seven are `fable-library-py` files that the spike changed [F2].

### Why work stopped

- PR #2509 closed on 2022-05-20 when a maintainer deleted its base branch, `beyond`. The maintainer
  asked for a new PR against `snake_island` [F3].
- The author of #2509 replied: "There were a couple of quite fundamental issues that needed
  addressing first such as exactly what target version to use and how it would work with imports
  etc." The author moved to the Rust target [F3].
- The author of #2509 listed the open items on 2022-01-06: a merge from `beyond`, the choice of Lua
  version, IEnumerable and classes, and import paths [F3].
- On #3290, the spike author wrote on 2024-06-04 that "Personal life got a bit busy". The last
  comment, on 2024-08-30, discusses a trim of the library for Neovim [F2].
- No comment on #3290 states that work ended. The PR has no commit in 851 days. (Inference: the
  work is stalled.)

## The parts of a Fable target

### What a target contains

Fable compiles F# through one pipeline: FCS, then `FSharp2Fable`, then `FableTransforms`, then a
transform and a printer for each target [F5]. A maintainer listed the modules of a new target on
2022-09-22 [F6]:

> - **MyLang.fs**: The AST for your language
> - **Fable2MyLang**: Transform Fable AST into your language AST
> - **MyLangPrinter**: Print your language AST
> - **MyLangReplacements**: Tell Fable how calls to FSharp.Core or .NET BCL should be replaced

The same comment says that "there are a few places you need to wire manually to add a new language".

### The wiring outside the target folder

The Beam target is the most recent target. Its PR #4340 changed these shared files [F7]:

- `src/Fable.AST/Plugins.fs`: a new `Language` case.
- `src/Fable.Cli/Entry.fs`, `Main.fs`, and `Pipeline.fs`: the `--lang` switch, the status, and the
  compile step.
- `src/Fable.Compiler/ProjectCracker.fs` and `Util.fs`: the library path.
- `src/Fable.Core/Fable.Core.BeamInterop.fs`: interop attributes.
- `src/Fable.Transforms/Replacements.Api.fs`, `Transforms.Util.fs`, and `FSharp2Fable.Util.fs`.
- `src/fable-standalone`: the standalone compiler.
- `src/Fable.Build`: a `FableLibrary`, a `Test`, and a `Quicktest` module for the target.
- `.github/workflows/build.yml`: a CI job for the target. A maintainer also asked for a change to
  `.devcontainer/Dockerfile` before the merge [F7].
- The changelogs of `Fable.AST`, `Fable.Cli`, `Fable.Compiler`, and `Fable.Core`.

`Fable.Cli.fsproj` packs `fable-library-rust`, `fable-library-dart`, `fable-library-beam`, and
`fable-library-php` into the NuGet package. The Python library ships on PyPI, and the JavaScript and
TypeScript libraries ship on npm [F8].

### Size of each target on `main`

| Target | Fable status | Target folder in `Fable.Transforms` | Runtime library | F# tests |
| --- | --- | --- | --- | --- |
| JavaScript (reference) | `stable` | 7683 (AST 960, transform 5065, printer 1658) plus `Replacements.fs` 4989 | 11,061 TS, 10,826 F# | 30,609 lines |
| Python | `beta` | 16,914 (AST 1513, transform 9442, printer 1129, replacements 4400) | 16,141 Python, 5589 F#, 12,096 Rust | 28,825 lines, 2603 tests |
| Dart | `beta` | 9124 (AST 462, transform 3255, printer 1164, replacements 4243) | 4211 Dart, 6703 F# | 14,712 lines, 1755 tests |
| Rust | `alpha` | 21,328 (AST 11,156, transform 6462, printer 23, replacements 3687) | 12,605 Rust, 7992 F# | 32,575 lines, 3181 tests |
| Beam | `alpha` in the CLI | 13,083 (AST 83, transform 5127, printer 676, replacements 6429, prelude 768) | 11,353 Erlang, 227 F# | 26,177 lines, 2810 tests |
| PHP | `experimental` | 3050 (AST 129, transform 2199, printer 722), JavaScript replacements | 2265 PHP | 1175 lines, 102 tests |
| Lua spike | none | 1006, JavaScript replacements | 1629 Lua, 1483 F# | 396 lines, 48 tests |

- The Python library has a Rust core through PyO3. The Fable 5 announcement gives the reason as
  correct .NET semantics for sized integers and arrays [D2].
- Each target also uses the shared code: `FSharp2Fable.fs` (3052 lines), `FableTransforms.fs`
  (1021 lines), and `Replacements.Util.fs` (1918 lines) [F5].
- The F# files of a runtime library compile through the target itself. Python and Beam link
  shared F# modules, for example `Seq2.fs`, from `fable-library-ts`. Dart keeps its own copies
  [F5][F9].
- `build.yml` has a job for JavaScript, TypeScript, Python, Rust, Dart, and Beam. It has no job for
  PHP [F8].

### Time for each target to reach its status

Fable introduced the four statuses in the Fable 4 theta release on 2022-09-28. The same commit
added the status to the CLI [D1][F10].

| Target | First commit in Fable history | First PR | Status | Set on | Days from first commit |
| --- | --- | --- | --- | --- | --- |
| Python | 2021-01-10 | #2345, opened 2021-01-13, merged 2021-07-16 | `beta` | 2022-09-28 | 626 |
| Dart | 2021-07-16 | commits to `beyond` | `beta` | 2022-09-28 | 439 |
| Rust | 2021-09-01 | #2523, opened 2021-09-01, merged 2021-11-24 | `alpha` | 2022-09-28 | 392 |
| PHP | 2021-06-03 | #2447, opened 2021-05-21, merged 2021-06-03 | `experimental` | 2022-09-28 | 482 |
| Beam | 2026-02-07 | #4340, opened 2026-02-08, merged 2026-02-17 | `alpha` | 2026-02-26 (5.0.0-rc.1) | 19 |
| Lua | 2021-08-21 | #2509 closed unmerged, #3290 draft | none | none | 1872 to date |

- The day counts for Python, Dart, Rust, and PHP end on 2022-09-28, the first day that any target
  had a status.
- Python, Dart, Rust, and PHP keep the status that they received on 2022-09-28 [F10].
- Fable 4.0.0 (2023-03-14) shipped JavaScript as stable. Fable 5.0.0 (2026-04-21) is the current
  major version [F11].
- Beam arrived as one PR with 2022 Erlang tests that passed. The author, Dag Brattli, also wrote the
  Python target [F7]. Fable 5.0.0-rc.1 lists the Beam target with "2086 tests passing" [F11].
- The CLI shows Beam as `alpha`. The documentation lists Beam as `Experimental` [F10][D3].

## Maintainer conditions and their position on Lua

### Conditions for a new target

- The Fable README: "If you are up to contribute a fix or a feature yourself, you're more than
  welcome! Please send first an issue or a minimal Work In Progess PR so we can discuss the
  implementation details in advance." [F12]
- Maxime Mangel, member, 2026-01-14: "I think we are open to adding new targets to Fable, however
  the work needs to come from the community. We can provide guidance / explanation regarding how
  Fable works in general." [F13]
- The same comment: "It is important to note that creating a target is a long term project because
  it is a complex task and will need maintenance over time. At first need to live in a long living
  PR until it reaches a "usable" state." [F13]
- The same thread: "Custom targets need to be build into Fable itself." A plugin system would
  freeze the internal APIs [F13].
- Maxime Mangel, 2023-12-03: a new target means "adding a new compiler target and for contributors
  to come to add it and maintain it." [F14]
- The Fable 4 theta announcement defines `experimental` as "The target has been added by a
  contributor but it's not currently maintained and may be removed in the future." [D1]
- Fable's `AGENTS.md` says that a change to `src/Fable.AST/` is breaking "and require[s] a new major
  version of Fable". A new target adds a `Language` case to `Plugins.fs` in that project [F5].
  Beam added its case in the 5.0.0-rc.1 cycle [F15].
- The same file binds each target to these rules [F5]:
  - "Targets should stay aligned in how they solve similar features."
  - "Never modify a test to make it pass on a target if it already passes on .NET."

The repo has no `CONTRIBUTING.md`. The documentation site has no contributor guide for a new
target [F12][D3].

### Their position on a Lua target

- Maxime Mangel, 2023-11-18, on #2509: "As Python is maintained and Lua target is kinda dead,
  perhaps it is worth trying to consume the API using Python instead." [F3]
- Maxime Mangel, 2024-08-26, on #3290, about Neovim: "Vim people are often focused on performance.
  So perhaps, they will not like having a dependency like `fable-library` which takes some
  "place"." [F2]
- Alfonso Garcia-Caro closed issue #2753, "Lua Support for Fable", on 2022-01-19 as a duplicate of
  #2509 [F16].
- A search of Fable issues, PRs, and discussions finds no maintainer comment on Lua after
  2024-08-26 [F17].

## Lua semantics that an F# target must map

### Strings

- .NET `String.Length` counts UTF-16 code units [X1]. A Lua 5.1 string is a byte array: "Lua is
  8-bit clean", and `#s` is "its number of bytes" [L1].
- The Lua 5.1 string library "assumes one-byte character encodings" [L1].
- Neovim converts byte indices to UTF-16 or UTF-32 indices with `vim.str_utfindex()` [N2].
- JavaScript and Dart strings are UTF-16, so `Length` maps to the native length [D4][X3].
- Fable Python maps `String.Length` to `int32(len(string))` [F9]. A Python `str` is a sequence of
  code points [X2]. (Inference: Fable Python returns a count that differs from .NET for a character
  outside the Basic Multilingual Plane.)

### Numbers

- A Lua 5.1 number is a double [L1]. LuaJIT numbers are also doubles [L3].
- LuaJIT adds 64-bit integers as FFI `cdata` with the `LL` and `ULL` literal suffixes. These values
  are "sticky": an expression with one 64-bit operand gives a 64-bit result [L3].
- Neovim says that LuaJIT extensions "cannot be assumed to be available". The manual tells plugin
  code to check the `jit` global before it uses them [N1].
- Neovim always provides `require("bit")`, also on PUC Lua [N1]. Each `bit` operation returns a
  signed 32-bit number [N3]. The FFI extends `bit` to 64-bit `cdata` on LuaJIT only [L2].
- Fable JavaScript also runs on doubles. It maps `int64` and `uint64` to the native JS `BigInt`
  since Fable 4.0.5 [D4].
- Fable Python implements each sized integer in Rust through PyO3 for "Correct .NET semantics" and
  "Proper overflow behavior" [D2][D5].
- Fable Dart maps each integer kind from `Int8` to `UInt128` to the Dart type `int` [F9].

### `nil` holes in tables

- `#t` gives any border of a table. "If the array has "holes" (that is, nil values between other
  non-nil values), then #t can be any of the indices" [L1].
- The spike erases `None` to `nil` and stores arrays as plain tables [F2]. (Inference: an array of
  options with a `None` element has an undefined length in the spike.)
- Fable JavaScript and Python erase options. Each has a `Some` wrapper class for a nested option
  [F9][D5]. Fable Dart maps an option to a nullable `Some<T>` reference [F9].
- Fable Python wraps arrays in a custom `FSharpArray` type "to maintain F# semantics" [D5].

### Exceptions

- Lua code raises an error with `error` and catches it with `pcall` or `xpcall` [L1].
- LuaJIT can yield across `pcall` and `xpcall`. The standard Lua 5.1 VM cannot [L2].
- The spike maps `TryCatch` to `pcall` and `Throw` to `error` with a fixed string [F2].
- Fable Dart maps the exception type to `dynamic`, because "there is no single type that catches
  all errors in Dart" [F9].

## The Lua version to emit

- "Lua 5.1 is the permanent interface for Nvim Lua." Neovim excludes "extensions such as `goto`
  that some Lua 5.1 interpreters like LuaJIT may support" [N1].
- LuaJIT is "API+ABI-compatible with Lua 5.1". It enables `goto` unconditionally. It enables
  `table.unpack` and other Lua 5.2 features only in a build with `LUAJIT_ENABLE_LUA52COMPAT` [L2].
- The spike targets Lua 5.2. Its README says that tests ran against Lua 5.2.4 [F2].
- The spike's `Util.lua` uses `goto` in its generated `switch` code. The spike printer emits the
  Lua 5.3 bitwise operator `&` [F2].
- The author of #2509 named the Lua version as an open issue in 2022 [F3].

## Size and load time of the runtime in a Neovim plugin

### Measured

| Item | Size | Load time in `nvim --headless --clean` |
| --- | --- | --- |
| Spike `Util.lua` | 49,163 bytes | 0.46 to 0.61 ms, three runs |
| 15 copies of `Util.lua`, each in a function, as a size proxy | 737,930 bytes | 5.15 to 5.27 ms, three runs |

The proxy matches the unpacked size of the npm package `@fable-org/fable-library-js` 2.8.0, which is
736,067 bytes in 71 files [X4]. (Inference: a Lua runtime as large as the JavaScript runtime loads in
about 5 ms on this machine.)

### Reference sizes of shipped runtimes

- `@fable-org/fable-library-js` 2.8.0: 736,067 bytes unpacked, 71 files [X4].
- `@fable-org/fable-library-ts` 2.8.0: 889,055 bytes unpacked, 72 files [X4].
- `fable-library` 5.19.0 on PyPI: 249,385 bytes as an sdist, and 1,313,863 bytes as a CPython 3.12
  macOS arm64 wheel [X5].

### Neovim facts that affect load time

- Neovim guidance tells a plugin to keep `plugin/<name>.lua` small and to defer each `require` until
  a command runs [N4].
- `require` searches `lua/` under each `'runtimepath'` directory. "Any "." in the module name is
  treated as a directory separator" [N1].
- The spike emits `require('./fable-lib/Util')` and relative paths with `../` [F2]. (Inference:
  Neovim's module search cannot resolve these names.)
- `vim.loader.enable()` adds a byte-compilation cache. The manual marks it "experimental/unstable"
  [N1].

## The smallest target for `Envelope.fs`, `Protocol.fs`, and `Refusals.fs`

### Library calls

The three files hold 389 lines (42, 232, and 115). The full set of library calls and
conversions is:

| File | Calls |
| --- | --- |
| `Envelope.fs` | `Array.append`, `Array.sub`, array `.Length`, array index, `Encoding.UTF8.GetBytes`, `Encoding.UTF8.GetString`, `<<<`, `>>>`, `\|\|\|`, `int` of a byte, `byte` of an int |
| `Protocol.fs` | `sprintf` with `%d` and `%s`, `String.concat`, `String.EndsWith`, `String.Equals` with `StringComparison.OrdinalIgnoreCase`, `List.map`, `List.tryFind`, `Option.map`, `Option.defaultValue`, `defaultArg`, `snd`, `Result.map`, `Convert.FromBase64String`, structural equality on `string option` |
| `Refusals.fs` | `Map.ofList`, the `Map` indexer `table.["unaddressable"]`, `Map.tryFind`, `Option.defaultValue`, `sprintf` with `%s`, string `+` |

An earlier list of these calls, from the planning of #251, omits `String.EndsWith`,
`String.Equals` with `OrdinalIgnoreCase`, `defaultArg`, `snd`, the `Map` indexer, structural equality, string `+`, the
shift and bitwise operators, and the byte conversions.

### Language features

- `Envelope.fs`: a class with a primary constructor, a mutable field, and a method. A `while` loop,
  a mutable local, and a closure call.
- `Protocol.fs`: unions with fields, records with nested records and lists of tuples, and options.
  A match on tuples with `when` guards, a match on `[<Literal>]` strings, and `try`/`with` that
  catches every exception.
- `Refusals.fs`: a list of tuples, records, and module-level values.

### What the smallest target needs (inference)

- A transform that covers every case above, a printer, and a `Replacements.fs` for Lua. The
  JavaScript replacements name JavaScript library modules, as the spike output shows.
- A runtime with these parts: printf formatting, `List`, `Map`, `Option`, `Result`, string
  helpers, `Array.append` and `Array.sub`, base64 decode, and UTF-8 encode and decode.
- The shared F# `Map.fs` (1090 lines) uses class inheritance, `IComparer`, `Seq`, and `Array` [F5].
  To compile it, the target needs those features too. The alternative is a hand-written Lua `Map`.
- A choice of representation for `byte[]`: a table of numbers or a Lua string.
- Shift and bitwise operators through `require("bit")` [N1].
- `Encoding.UTF8` can map to the identity on a Lua string, because a Lua string holds bytes [L1].

## Facts the language decision depends on

1. PR #3290 is a draft with no commit since 2024-06-07. It is 969 commits behind `main`, and a
   trial merge reports 17 files with conflicts [F2].
2. The spike compiles none of the three pure F# files to valid Lua. `Protocol.fs` crashes the
   compiler. The output for `Envelope.fs` and `Refusals.fs` fails to parse in LuaJIT.
3. The spike has no Lua replacements. It sends BCL calls to JavaScript library names that its
   runtime does not have [F2][F4].
4. The spike targets Lua 5.2 and emits `goto` and `&`. Neovim supports Lua 5.1 only, and excludes
   `goto` [N1][F2].
5. The maintainers accept a new target that the community builds and maintains. They expect a
   long-lived PR until it is usable [F13].
6. A maintainer called the Lua target "kinda dead" in 2023, and raised the runtime size for Neovim
   users in 2024 [F3][F2].
7. A new target adds a case to `Fable.AST`. Fable's own rules tie a `Fable.AST` change to a major
   version [F5].
8. Python, Dart, and Rust held `alpha` or `beta` 392 to 626 days after their first commit, on the
   day that Fable introduced statuses. Beam reached a release 19 days after its first commit
   [F10][F11].
9. The current targets hold 3050 to 21,328 lines in their target folder, plus a runtime of 2265 to
   about 34,000 lines.
10. LuaJIT loads a 738 KB Lua file in about 5 ms on the test machine (measured).
11. The three pure F# files use 28 distinct library calls and operators, among them `Map`, `sprintf`,
    base64, and UTF-8.

## Sources

- [R1] Prior note, "implementation routes for a Neovim client" (branch `research/nvim-plugin-languages`): https://github.com/tw0po1nt/FsHttp.Studio/blob/research/nvim-plugin-languages/docs/research/nvim-plugin-languages.md
- [F1] Fable releases: https://github.com/fable-compiler/Fable/releases
- [F2] Fable PR #3290, "Lua spike (placeholder)", its comments, and the branch `voronoipotato/Fable:lua-spike` at `e07d3d1`: https://github.com/fable-compiler/Fable/pull/3290
- [F3] Fable PR #2509, "[WIP] Lua Language support", and its comments: https://github.com/fable-compiler/Fable/pull/2509
- [F4] `Replacements.Api.fs` on `main`: https://github.com/fable-compiler/Fable/blob/main/src/Fable.Transforms/Replacements.Api.fs
- [F5] Fable `AGENTS.md` and source tree on `main`: https://github.com/fable-compiler/Fable/blob/main/AGENTS.md
- [F6] Alfonso Garcia-Caro on the modules of a new target, discussion #3196: https://github.com/fable-compiler/Fable/discussions/3196#discussioncomment-3706720
- [F7] Fable PR #4340, "[Beam] Fable Beam (alpha)", its files and comments: https://github.com/fable-compiler/Fable/pull/4340
- [F8] `Fable.Cli.fsproj` and `build.yml` on `main`: https://github.com/fable-compiler/Fable/blob/main/src/Fable.Cli/Fable.Cli.fsproj, https://github.com/fable-compiler/Fable/blob/main/.github/workflows/build.yml
- [F9] Target sources on `main`: `Python/Replacements.fs`, `Dart/Fable2Dart.fs`, `Dart/Replacements.fs`, `fable-library-py/fable_library/string_.py`, `fable-library-ts/Option.ts`: https://github.com/fable-compiler/Fable/tree/main/src
- [F10] Commit `9f0ac79a`, "Add status and runScript to all languages", and `getStatus` in `Entry.fs`: https://github.com/fable-compiler/Fable/commit/9f0ac79a, https://github.com/fable-compiler/Fable/blob/main/src/Fable.Cli/Entry.fs
- [F11] `Fable.Cli` changelog: https://github.com/fable-compiler/Fable/blob/main/src/Fable.Cli/CHANGELOG.md
- [F12] Fable README, "Contributing": https://github.com/fable-compiler/Fable#contributing
- [F13] Maxime Mangel on new targets, discussion #4335: https://github.com/fable-compiler/Fable/discussions/4335#discussioncomment-15493437, https://github.com/fable-compiler/Fable/discussions/4335#discussioncomment-15497930
- [F14] Maxime Mangel on a JVM target, discussion #3636: https://github.com/fable-compiler/Fable/discussions/3636#discussioncomment-7744225
- [F15] `Fable.AST` changelog: https://github.com/fable-compiler/Fable/blob/main/src/Fable.AST/CHANGELOG.md
- [F16] Fable issue #2753, "Lua Support for Fable": https://github.com/fable-compiler/Fable/issues/2753
- [F17] GitHub search of `fable-compiler/Fable` for "lua" in issues, PRs, comments, and discussions: https://github.com/fable-compiler/Fable/issues?q=lua
- [D1] "Announcing Fable 4 Theta Release", 2022-09-28: https://fable.io/blog/2022/2022-09-28-fable-4-theta.html
- [D2] "Fable 5 RC is Here", 2026-02-27: https://fable.io/blog/2026/2026-02-27-Fable_5_release_candidate.html
- [D3] Fable documentation, target list and status: https://fable.io/docs/
- [D4] Fable JavaScript compatibility: https://fable.io/docs/javascript/compatibility.html
- [D5] Fable Python compatibility: https://fable.io/docs/python/compatibility.html
- [L1] Lua 5.1 Reference Manual, §2.2, §2.5.5, §2.7, §5.4: https://www.lua.org/manual/5.1/manual.html
- [L2] LuaJIT extensions: https://luajit.org/extensions.html
- [L3] LuaJIT FFI semantics, 64-bit integer arithmetic: https://luajit.org/ext_ffi_semantics.html
- [N1] `:help lua-compat`, `:help lua-luajit`, `:help lua-module-load`, `:help vim.loader` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
- [N2] `:help vim.str_utfindex()` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua.txt
- [N3] `:help lua-bit` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua-bit.txt
- [N4] `:help lua-plugin-lazy` (v0.12.5): https://github.com/neovim/neovim/blob/v0.12.5/runtime/doc/lua-plugin.txt
- [X1] .NET `String.Length`: https://learn.microsoft.com/en-us/dotnet/api/system.string.length
- [X2] Python text sequence type: https://docs.python.org/3/library/stdtypes.html#text-sequence-type-str
- [X3] Dart `String` class: https://api.dart.dev/stable/dart-core/String-class.html
- [X4] npm `@fable-org/fable-library-js` and `@fable-org/fable-library-ts`: https://www.npmjs.com/package/@fable-org/fable-library-js, https://www.npmjs.com/package/@fable-org/fable-library-ts
- [X5] PyPI `fable-library` 5.19.0: https://pypi.org/project/fable-library/5.19.0/#files
