#!/usr/bin/env python3
"""Extrait toutes les paires input → output des YAML actuels en un corpus
texte indépendant (corpus/yaml-gold-extracted.txt). Sert à valider le
nouveau lattice engine sur le même périmètre que le PatternEngine actuel.
"""
import sys, io, yaml
from pathlib import Path

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

YAML_ROOT = Path("data/yaml_domains")
OUTPUT = Path("core-csharp/tests/MathCursor.Core.Tests/corpus/yaml-gold-extracted.txt")

def main():
    sections = []
    for yf in sorted(YAML_ROOT.rglob("*.yaml")):
        try:
            doc = yaml.safe_load(yf.read_text(encoding='utf-8'))
        except Exception:
            continue
        if not doc:
            continue
        rel = str(yf.relative_to(YAML_ROOT)).replace('\\', '/')
        section_pairs = []
        for p in doc.get('patterns') or []:
            pid = p.get('id', '?')
            for ex in p.get('examples') or []:
                if ex.get('skip'):
                    continue
                inp = (ex.get('input') or '').strip()
                out = (ex.get('output') or '').strip()
                if inp and out:
                    section_pairs.append((pid, inp, out))
        if section_pairs:
            sections.append((rel, section_pairs))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT.open('w', encoding='utf-8') as f:
        f.write("# Corpus extrait des YAML PatternEngine (archive)\n")
        f.write("# Format: input => latex_attendu\n")
        f.write("# Source : data/yaml_domains/**/*.yaml, examples des patterns\n")
        f.write(f"# Total: {sum(len(p) for _, p in sections)} paires sur {len(sections)} fichiers\n\n")
        for rel, pairs in sections:
            f.write(f"# === {rel} ===\n")
            for pid, inp, out in pairs:
                # Skip lignes avec retours-chariot interne (rare)
                if '\n' in inp or '\n' in out:
                    continue
                f.write(f"# [{pid}]\n")
                f.write(f"{inp} => {out}\n")
            f.write("\n")
    print(f"Corpus écrit : {OUTPUT}")
    print(f"Paires : {sum(len(p) for _, p in sections)}")

if __name__ == "__main__":
    main()
