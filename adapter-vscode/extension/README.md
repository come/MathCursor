# MathCursor (VSCode)

Write maths the way they come to you: keyboard notation → **clean inline LaTeX**,
with a **formula preview at the caret** (Word-style popup).

Sits on top of LaTeX Workshop (coexistence, zero coupling). Type an expression
(`1/x+1`, `vec AB . vec BC`, `lim x 0 1/x`, `R*`…): MathCursor detects the math
zone, proposes the rendered LaTeX, you confirm → insertion with delimiters and
packages added automatically.

## How it works

- **Detection** of the math zone as you type (NER model) + `Ctrl+Space` to
  force / grow the zone.
- **Engine** text → ranked LaTeX candidates (multi-candidate popup at the caret).
- **100% native**: engine, NER and popup are embedded **Rust** binaries (no .NET
  runtime). Windows-x64 for now.

## Settings

`mathcursor.culture` (fr/us), `delimiters` (auto/inline/display/paren/none),
`maxCandidates`, `autoDetect`, `autoPackages`, `inlineDisplaystyle`.

## Shortcuts

- `Ctrl+Space` — popup at caret / force / grow the zone.
