#!/usr/bin/env python3
"""
Audit des macros LaTeX émises par le core MathCursor.

Phase A : extraction directe depuis les YAML.
- templates : tout ce que le moteur peut générer
- examples.output : ce qu'on attend en gold

Sort dans tools/audit-latex-macros.md une liste classée :
- macros utilisées (avec count + sources)
- catégorie probable de support WpfMath (à valider à la main)
"""
import os
import re
import sys
import yaml
from collections import defaultdict
from pathlib import Path

# Force stdout UTF-8 pour les caractères grecs / unicode
sys.stdout.reconfigure(encoding="utf-8")

YAML_ROOT = Path("data/yaml_domains")
OUTPUT_FILE = Path("tools/audit-latex-macros.md")

# Regex : commande LaTeX = backslash + lettres (au moins 1)
MACRO_RX = re.compile(r"\\([A-Za-z]+)")

# Catégorisation a priori du support WpfMath 2.1
# Source : code source XamlMath / WpfMath + tests publics
WPFMATH_SUPPORTED = {
    # Greek (lower)
    "alpha", "beta", "gamma", "delta", "epsilon", "varepsilon", "zeta", "eta",
    "theta", "vartheta", "iota", "kappa", "lambda", "mu", "nu", "xi", "pi",
    "varpi", "rho", "varrho", "sigma", "varsigma", "tau", "upsilon", "phi",
    "varphi", "chi", "psi", "omega",
    # Greek (upper)
    "Gamma", "Delta", "Theta", "Lambda", "Xi", "Pi", "Sigma", "Upsilon",
    "Phi", "Psi", "Omega",
    # Structural
    "frac", "sqrt", "binom",
    # Operators (big)
    "sum", "prod", "int", "lim",
    # Functions named
    "sin", "cos", "tan", "cot", "sec", "csc",
    "arcsin", "arccos", "arctan",
    "sinh", "cosh", "tanh",
    "log", "ln", "exp", "min", "max",
    "det", "ker", "deg", "dim", "arg", "gcd",
    # Symbols / relations
    "infty", "partial", "nabla",
    "leq", "geq", "neq", "approx", "equiv", "sim", "simeq",
    "in", "notin", "subset", "subseteq", "supset", "supseteq", "cup", "cap",
    "to", "rightarrow", "leftarrow", "Rightarrow", "Leftrightarrow", "iff",
    "implies", "longrightarrow",
    "forall", "exists",
    "cdot", "times", "div", "pm", "mp", "circ",
    "wedge", "vee", "land", "lor", "neg",
    "ldots", "cdots", "vdots",
    # Delimiters
    "left", "right", "lvert", "rvert", "lVert", "rVert",
    "langle", "rangle",
    # Accents (single char)
    "vec", "hat", "tilde", "bar", "dot", "ddot", "acute", "grave",
    "check", "breve",
    # Font styles
    "mathrm", "mathbf", "mathit", "mathsf", "mathtt", "mathcal",
    # Other
    "text", "operatorname",
}

# Macros notoirement pas supportées (ou partielles) par WpfMath 2.1
WPFMATH_KNOWN_MISSING = {
    "mathbb",        # blackboard bold — pas de font
    "mathfrak",      # fraktur — pas de font
    "begin", "end",  # environnements (cases, matrix, pmatrix...)
    "widehat", "widetilde",  # accents stretchy (peut-être supporté ?)
    "overline", "underline",  # accents longs
    "iint", "iiint", "oint",  # intégrales doubled
    "mapsto",        # flèche avec barre
    "limsup", "liminf",
    "stackrel",
    "begin{cases}", "end{cases}",  # déjà couvert par begin/end
    "setminus",      # difference d'ensembles
    "bmod", "pmod",  # modulo
    "complement",
    "triangle",
    "parallel", "perp",
    "coloneqq",
    "lhd", "rhd",
    "Theta", # déjà dans supported, mais le grand-O Landau utilise Omega aussi
    "Omega",  # idem
    "varnothing", "emptyset",
    "Re",  # partie réelle (peut-être pas)
    "otimes", "oplus", "ominus", "odot",
    "propto",
    "leqslant", "geqslant",  # variantes françaises
    "leftrightarrow",
    "varphi",
}

def extract_macros(text):
    """Retourne le set des noms de macros dans une chaîne LaTeX."""
    if not text:
        return set()
    return set(MACRO_RX.findall(text))


def main():
    if not YAML_ROOT.exists():
        print(f"ERR: {YAML_ROOT} introuvable", file=sys.stderr)
        sys.exit(1)

    # macro_name -> { "templates": [(file, pattern_id), ...], "outputs": [...] }
    macros = defaultdict(lambda: {"templates": [], "outputs": []})

    for yf in sorted(YAML_ROOT.rglob("*.yaml")):
        try:
            doc = yaml.safe_load(yf.read_text(encoding="utf-8"))
        except Exception as e:
            print(f"WARN: skip {yf}: {e}", file=sys.stderr)
            continue
        if not doc:
            continue
        rel = str(yf.relative_to(YAML_ROOT)).replace("\\", "/")
        for p in doc.get("patterns") or []:
            pid = p.get("id", "?")
            tpl = p.get("template", "")
            for m in extract_macros(tpl):
                macros[m]["templates"].append(f"{rel}::{pid}")
            for ex in p.get("examples") or []:
                out = ex.get("output", "") or ""
                for m in extract_macros(out):
                    macros[m]["outputs"].append(f"{rel}::{pid}::out")

    # Classement par catégorie
    supported = []
    missing = []
    unknown = []
    for name, info in sorted(macros.items()):
        n_tpl = len(info["templates"])
        n_out = len(info["outputs"])
        total = n_tpl + n_out
        if name in WPFMATH_SUPPORTED:
            supported.append((name, total, info))
        elif name in WPFMATH_KNOWN_MISSING:
            missing.append((name, total, info))
        else:
            unknown.append((name, total, info))

    OUTPUT_FILE.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT_FILE.open("w", encoding="utf-8") as f:
        f.write("# Audit des macros LaTeX émises par le core MathCursor\n\n")
        f.write(f"Source : extraction `templates:` + `examples.output:` de tous les YAML sous `{YAML_ROOT}`.\n\n")
        f.write(f"**Résumé** :\n")
        f.write(f"- {len(macros)} macros distinctes émises\n")
        f.write(f"- {len(supported)} supportées par WpfMath 2.1 a priori\n")
        f.write(f"- {len(missing)} **manquantes connues** (à patcher / substituer)\n")
        f.write(f"- {len(unknown)} inconnues (à valider — peut-être supportées)\n\n")

        def write_section(title, items, note):
            f.write(f"## {title} ({len(items)})\n\n")
            if note:
                f.write(f"> {note}\n\n")
            if not items:
                f.write("_(aucun)_\n\n")
                return
            f.write("| Macro | Count | Sources (sample) |\n")
            f.write("|---|---|---|\n")
            for name, count, info in items:
                # Sample : 3 premières sources distinctes
                all_src = sorted(set(info["templates"] + info["outputs"]))
                sample = ", ".join(all_src[:3])
                if len(all_src) > 3:
                    sample += f" _(+{len(all_src)-3})_"
                f.write(f"| `\\{name}` | {count} | {sample} |\n")
            f.write("\n")

        write_section(
            "Manquantes WpfMath — à traiter",
            missing,
            "Ces macros ne sont pas (ou mal) rendues par WpfMath 2.1. Pour chacune, choisir : "
            "(A) substituer côté core en LaTeX/Unicode équivalent, "
            "(B) ajouter via override XML WpfMath, "
            "(C) patcher WpfMath (fork ciblé)."
        )
        write_section(
            "Inconnues — à vérifier",
            unknown,
            "Pas dans ma liste WpfMath supportée — peut-être supportées en réalité, à confirmer en testant sur la version qu'on utilise."
        )
        write_section(
            "Supportées par WpfMath",
            supported,
            "Couvertes nativement, rien à faire."
        )

    print(f"Rapport écrit dans {OUTPUT_FILE}")
    print(f"  {len(macros)} macros distinctes")
    print(f"  Manquantes : {len(missing)}")
    print(f"  Inconnues  : {len(unknown)}")
    print(f"  Supportées : {len(supported)}")


if __name__ == "__main__":
    main()
