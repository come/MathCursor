# Contributing to MathCursor

Thanks for your interest. MathCursor is in **phase 1**: a small, focused effort
to make Word Desktop usable daily by real high-school students (including those
with learning accommodations) and a few maths teachers. Contributions are
welcome, with that goal in mind.

## Ground rules

- **The engine stays pure.** `engine/` and `serialization/` are `netstandard2.0`
  with **zero** `Microsoft.Office.*` / WPF / platform dependencies. That purity
  is what lets the browser demo and the non-Word hosts reuse the exact same
  recognition logic. Platform code lives in an adapter, never the other way
  round.
- **The corpus is the source of truth.** Engine behaviour is locked by
  `engine/tests/.../fixtures.json`, replayed by several pipelines (C# engine,
  OMML serialization, and the Rust core parity gate). New recognition behaviour
  means new fixtures.
- **Decisions are recorded as ADRs.** Non-trivial changes (features, refactors,
  ergonomics, product rules) get a short ADR under
  [`docs/dev/decisions/`](docs/dev/decisions/) before the code lands. The format
  is described in
  [`docs/dev/decisions/2026-04-24-Meta-adr-format.md`](docs/dev/decisions/2026-04-24-Meta-adr-format.md);
  the chronological index in
  [`docs/dev/decisions/README.md`](docs/dev/decisions/README.md) plus `git log`
  is the most reliable picture of the current state.

## Before you open a PR

1. Read `docs/dev/decisions/README.md` and skim recent `git log` to understand
   where things stand.
2. Build and run the local test gate:

   ```powershell
   dotnet build MathCursor.sln -c Release
   scripts/run-tests.ps1
   ```

   For the Rust core, `cd rust && cargo test` (the `fixtures.json` parity gate is
   part of it).
3. Keep changes scoped. If a change touches Word interop (OMath / ContentControls
   / selection ranges), read
   [`docs/dev/architecture/word-api-helpers.md`](docs/dev/architecture/word-api-helpers.md)
   first — that area is subtle and has hard-won helpers.

## Reporting bugs

Open a GitHub issue with what you typed, what you expected, and what you got
(a screenshot of the caret popup helps). For the Word add-in specifically, the
built-in one-click feedback button is the fastest channel.

## License

By contributing, you agree that your contributions are licensed under the project
license, the **GNU General Public License v3.0** (see [`LICENSE`](LICENSE)).
