# MathCursor

**Type maths the way you think them.** MathCursor captures your mathematical
intent as you type and turns it into real equations, without breaking your flow
and without you having to learn the tool.

Most editors convert a *finished, clean* input (formatted text, a photo, a
palette of buttons) into maths. MathCursor reads the **intent in motion**: you
write `x^2 + 1/2` at typing speed, a caret popup confirms you're on the right
track (missing pieces shown as placeholders, forgiving of shorthand), and one
keystroke snaps it into a properly typeset equation.

It was built for high-school students, in particular those with learning
accommodations (French *PAP*), who need to take maths notes fluidly on a
keyboard. Universal by design: everyone uses the *same* tool, no separate,
stigmatising setup.

> Try it in your browser, nothing to install: **[mathcursor.com](https://mathcursor.com)**

## Status

| Platform | State |
|----------|-------|
| **Word Desktop (Windows, VSTO)** | Available today — the primary target of phase 1 |
| **LibreOffice Writer** | Alpha |
| **VS Code (LaTeX)** | Alpha |
| Word Web / Mac / iPad (Office.js) | Planned (phase 2), once the desktop UX is validated |

The success criterion for phase 1 is simple: daily use by a real student with
learning accommodations and by a handful of maths teachers as beta testers.

## How it works

The **engine** is a pure function (text → ranked LaTeX candidates), fully
portable, with zero platform dependencies. Each **adapter** drives that same
engine for its host — identical recognition everywhere — and turns the chosen
LaTeX into that host's native math format:

```
                          ┌─ adapter-vsto         → OMML      → Word Desktop
engine (text → LaTeX) ────┼─ adapter-libreoffice  → StarMath  → LibreOffice Writer
  pure, portable core     └─ adapter-vscode       → LaTeX     → VS Code
```

**Hard rule:** the engine knows nothing about any host (`netstandard2.0`, no
`Microsoft.Office.*`, no WPF). That purity is what lets every adapter — and the
in-browser demo — reuse the exact same recognition logic. Two mirror
implementations keep parity, both locked to a shared conformance corpus
(`fixtures.json`): the **C#** engine (`engine/`, plus `serialization/` for Word
OMML) backs the Word add-in, while the **Rust** core (`rust/mc-engine`, with
StarMath output) is spawned by the VS Code and LibreOffice adapters.

## Repository layout

| Folder | Role |
|--------|------|
| `engine/` | Pure C# "forest" engine (text → candidates) + tests + `fixtures.json` (source of truth) |
| `serialization/` | LaTeX → OMML (Word insertion) + tests |
| `host-contract/` | Lightweight shared types (`EquationHandle`) |
| `adapter-vsto/` | Word Desktop add-in (orchestration, WPF UI, interop) + tests + installer |
| `adapter-vscode/` | VS Code extension (spawns the Rust binaries) |
| `adapter-libreoffice/` | LibreOffice extension (spawns the Rust binaries) |
| `rust/` | Rust core for non-Word hosts: `mc-engine`, `mc-ner`, `mc-popup` |
| `analyzers/` | Roslyn analyzers (project rules) + tests |
| `web-demo/` | Blazor WASM demo (reuses the compiled engine) |
| `data/` | Embedded data (`symbols.json`, `cultures.json`, NER corpus) |
| `scripts/` | Tooling (`run-tests.ps1` = local test gate) |

## Building from source

This repository is the **corresponding source** required by GPL v3 §6 for any
distributed MathCursor binary. Each module builds independently.

**Prerequisites** (by target): .NET Framework 4.8 + Visual Studio 2022 (Office/VSTO
workload) for the Word add-in; the .NET SDK (`dotnet`) for the engine and tests;
Rust stable (`cargo`) for the Rust core; Python 3.12 for the LibreOffice
extension; Node.js for the VS Code extension.

```powershell
# Engine + serialization + tests (pure, cross-platform)
dotnet build MathCursor.sln -c Release
scripts/run-tests.ps1          # full local xUnit gate

# Word add-in (VSTO) + installer (bundles LICENSE, notices, Apache-2.0.txt)
msbuild adapter-vsto/src/MathCursor/MathCursor.csproj /p:Configuration=Release
powershell -ExecutionPolicy Bypass -File adapter-vsto/installer/build.ps1

# Rust core (mc-engine / mc-ner / mc-popup) — own fixtures.json parity gate
cd rust && cargo build --release && cargo test

# VS Code extension
cd adapter-vscode/extension && npm install && npm run compile

# LibreOffice extension (.oxt)
cd adapter-libreoffice && python build_oxt.py
```

## Contributing

Design decisions live as ADRs under [`docs/dev/decisions/`](docs/dev/decisions/)
— one file per decision, indexed in
[`docs/dev/decisions/README.md`](docs/dev/decisions/README.md). That index plus
`git log` is the most reliable picture of the current state. Non-trivial changes
(features, refactors, ergonomics, product rules) get an ADR before the code
lands.

## License

MathCursor is **free software**, licensed under the **GNU General Public License,
version 3 or (at your option) any later version** — full text in
[`LICENSE`](LICENSE).

```
MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
Copyright (C) 2026  Côme Percin

This program is free software: you can redistribute it and/or modify it under
the terms of the GNU GPL as published by the Free Software Foundation, either
version 3 of the License, or (at your option) any later version. It comes with
ABSOLUTELY NO WARRANTY.
```

The pure recognition core (`engine/`, `serialization/`, `netstandard2.0`) stays
reusable by other hosts — **under the GPL v3**.

Bundled third-party components (math fonts, WpfMath, ONNX Runtime, the DistilBERT
base of the NER model) keep their own permissive licenses; they are aggregated in
the GPL §5 sense. Details and full attributions:
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). The Apache 2.0 text for the
DistilBERT base is in [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt).

| Component | Role | License |
|---|---|---|
| Latin Modern Math | math font | GUST Font License (LPPL 1.3c) |
| STIX Two Math | math font | SIL OFL 1.1 |
| WpfMath / XamlMath.Shared | LaTeX popup rendering | MIT |
| Microsoft.ML.OnnxRuntime | NER inference | MIT |
| DistilBERT (NER base model) | base model | Apache 2.0 |
