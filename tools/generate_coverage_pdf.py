# -*- coding: utf-8 -*-
"""Génère un PDF récapitulant la couverture MathCursor — version exhaustive.

Liste *toutes* les syntaxes acceptées et parsées par le moteur, telles que
définies dans data/yaml_domains/ (alias de tokens + exemples de patterns).

Colonne gauche : saisie clavier ASCII pure (une ligne par variante).
Colonne droite : rendu visuel de la formule produite.
"""

import io
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib import mathtext
from PIL import Image

from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.units import cm
from reportlab.lib import colors
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    Image as RLImage, KeepTogether
)
from reportlab.lib.enums import TA_LEFT

OUT = r"D:\Software\DocMath\docs\exports\mathcursor-couverture.pdf"

plt.rcParams["mathtext.fontset"] = "cm"


# ---------- Rendu LaTeX -> PNG ----------

def _sanitize(latex):
    rep = {
        r"\coloneqq": r":=",
        r"\bmod": r"\,\mathrm{mod}\,",
        r"\limsup": r"\mathrm{lim\,sup}",
        r"\liminf": r"\mathrm{lim\,inf}",
    }
    out = latex
    for k, v in rep.items():
        out = out.replace(k, v)
    return out


def render_expr(latex, fontsize=13, dpi=220):
    latex_s = _sanitize(latex)
    buf = io.BytesIO()
    try:
        mathtext.math_to_image(
            f"${latex_s}$", buf, dpi=dpi, format="png",
            prop=matplotlib.font_manager.FontProperties(size=fontsize))
        buf.seek(0)
        return Image.open(buf).convert("RGBA")
    except Exception:
        fig, ax = plt.subplots(figsize=(4, 0.4), dpi=dpi)
        ax.axis("off")
        ax.text(0, 0.5, latex, fontsize=fontsize, family="monospace",
                verticalalignment="center")
        b = io.BytesIO()
        fig.savefig(b, format="png", bbox_inches="tight",
                    pad_inches=0.02, transparent=True)
        plt.close(fig)
        b.seek(0)
        return Image.open(b).convert("RGBA")


def render_stack(exprs, fontsize=13, dpi=220):
    imgs = [render_expr(e, fontsize, dpi) for e in exprs]
    gap = 5
    W = max(i.width for i in imgs)
    H = sum(i.height for i in imgs) + gap * (len(imgs) - 1)
    canvas = Image.new("RGBA", (W, H), (255, 255, 255, 0))
    y = 0
    for i in imgs:
        canvas.paste(i, (0, y), i)
        y += i.height + gap
    return canvas


def pil_to_rlimage(pil_img, max_width_cm=9.0, max_height_cm=4.0):
    b = io.BytesIO()
    pil_img.save(b, format="PNG")
    b.seek(0)
    w_pt = pil_img.width * 72 / 220
    h_pt = pil_img.height * 72 / 220
    max_w = max_width_cm * cm
    max_h = max_height_cm * cm
    scale = min(max_w / w_pt, max_h / h_pt, 1.0)
    return RLImage(b, width=w_pt * scale, height=h_pt * scale)


# ---------- Styles ----------

styles = getSampleStyleSheet()
h1 = ParagraphStyle("H1", parent=styles["Heading1"], fontSize=17,
                    leading=21, spaceAfter=10,
                    textColor=colors.HexColor("#1a2b4a"))
h2 = ParagraphStyle("H2", parent=styles["Heading2"], fontSize=12.5,
                    leading=15, spaceBefore=14, spaceAfter=5,
                    textColor=colors.HexColor("#2a4a7a"))
h3 = ParagraphStyle("H3", parent=styles["Heading3"], fontSize=10.5,
                    leading=13, spaceBefore=8, spaceAfter=3,
                    textColor=colors.HexColor("#4a6a8a"))
body = ParagraphStyle("Body", parent=styles["BodyText"], fontSize=9,
                      leading=11.5, alignment=TA_LEFT)
cell_kb = ParagraphStyle("CellKb", parent=styles["BodyText"], fontSize=8.5,
                         leading=11, fontName="Courier")
cell_note = ParagraphStyle("CellNote", parent=styles["BodyText"], fontSize=8,
                           leading=10, fontName="Helvetica-Oblique",
                           textColor=colors.HexColor("#666666"))


def esc(s):
    return (s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;"))


def kb_cell(inputs, note=None):
    html = "<br/>".join(esc(x) for x in inputs)
    if note:
        html += f'<br/><font size="7" color="#888888"><i>{esc(note)}</i></font>'
    return Paragraph(html, cell_kb)


def make_table(rows):
    """rows : list[(inputs_list, latex_list, note_or_None)]"""
    data = [[Paragraph("<b>Saisies clavier acceptées</b>", cell_kb),
             Paragraph("<b>Rendu</b>", cell_kb)]]
    for row in rows:
        if len(row) == 2:
            inputs, latex_list = row
            note = None
        else:
            inputs, latex_list, note = row
        data.append([kb_cell(inputs, note),
                     pil_to_rlimage(render_stack(latex_list))])
    t = Table(data, colWidths=[7.5 * cm, 10 * cm], repeatRows=1)
    t.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), colors.HexColor("#2a4a7a")),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("GRID", (0, 0), (-1, -1), 0.4, colors.HexColor("#aaaaaa")),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("LEFTPADDING", (0, 0), (-1, -1), 5),
        ("RIGHTPADDING", (0, 0), (-1, -1), 5),
        ("TOPPADDING", (0, 0), (-1, -1), 4),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 4),
        ("ROWBACKGROUNDS", (0, 1), (-1, -1),
         [colors.white, colors.HexColor("#f2f5fa")]),
    ]))
    return t


# ============================================================
# Contenu exhaustif — toutes saisies acceptées tirées des YAML
# ============================================================

SECTIONS = [
    # ----------------------------------------------------------------
    ("1. Opérateurs arithmétiques de base", [
        (["a + b"], [r"a + b"]),
        (["a - b"], [r"a - b"]),
        (["a * b", "a . b", "a b"],
         [r"a \cdot b"],
         "multiplication : *, . ou juxtaposition"),
        (["a / b"], [r"\frac{a}{b}"]),
        (["a ^ b", "a ** b"], [r"a^{b}"]),
        (["a _ b"], [r"a_{b}"]),
    ]),

    # ----------------------------------------------------------------
    ("2. Relations & égalités", [
        (["a = b", "a == b"], [r"a = b"]),
        (["a != b", "a <> b", "a =/= b"], [r"a \neq b"]),
        (["a < b"], [r"a < b"]),
        (["a > b"], [r"a > b"]),
        (["a <= b", "a =< b"], [r"a \leq b"]),
        (["a >= b"], [r"a \geq b"]),
        (["a ~= b", "a ~~ b", "a approx b"], [r"a \approx b"]),
        (["a := b", "a \\coloneqq b"], [r"a := b"], "définition"),
        (["a ~ b", "a \\sim b"], [r"a \sim b"], "équivalence"),
        (["a propto b", "a \\propto b"], [r"a \propto b"]),
    ]),

    # ----------------------------------------------------------------
    ("3. Implications et flèches", [
        (["a -> b", "a --> b"], [r"a \to b"]),
        (["a => b", "a ==> b"], [r"a \Rightarrow b"]),
        (["a <=> b", "a <-> b"], [r"a \iff b"]),
        (["f : x |-> x^2", "f : x |--> x^2"],
         [r"f:\ x \mapsto x^2"]),
    ]),

    # ----------------------------------------------------------------
    ("4. Fractions", [
        (["1/2"], [r"\frac{1}{2}"]),
        (["a/b"], [r"\frac{a}{b}"]),
        (["(x+1)/(x-1)"], [r"\frac{x+1}{x-1}"]),
    ]),

    # ----------------------------------------------------------------
    ("5. Racines", [
        (["sqrt(x)", "sqrt x", "rac(x)", "rac x",
          "racine de x", "racine x"], [r"\sqrt{x}"]),
        (["sqrt(x+1)", "rac(x+1)", "racine de x+1"],
         [r"\sqrt{x+1}"]),
        (["sqrt[3](x)"], [r"\sqrt[3]{x}"], "racine n-ième explicite"),
        (["3 sqrt(4)", "3sqrt(4)"],
         [r"3\, \sqrt{4}", r"\sqrt[3]{4}"],
         "deux lectures : 3×√4 (par défaut) ou racine cubique"),
        (["V(x+1)"], [r"\sqrt{x+1}"],
         "lecture alternative — ambigu avec variance"),
    ]),

    # ----------------------------------------------------------------
    ("6. Puissances & indices", [
        (["x^2", "x**2"], [r"x^2"]),
        (["(x+1)^3", "(x+1)**3"], [r"(x+1)^3"]),
        (["A^*"], [r"A^*"], "dual / conjugué"),
        (["u_n"], [r"u_n"]),
        (["x_0"], [r"x_0"]),
        (["x_{i+1}"], [r"x_{i+1}"]),
        (["n!", "5!", "(n+1)!"], [r"n!", r"5!", r"(n+1)!"]),
        (["30%", "75%"], [r"30\%", r"75\%"]),
    ]),

    # ----------------------------------------------------------------
    ("7. Ensembles canoniques — tous les alias", [
        (["R", "IR"], [r"\mathbb{R}"]),
        (["N", "IN"], [r"\mathbb{N}"]),
        (["Z", "IZ"], [r"\mathbb{Z}"]),
        (["Q", "IQ"], [r"\mathbb{Q}"]),
        (["C", "IC"], [r"\mathbb{C}"]),
    ]),

    # ----------------------------------------------------------------
    ("8. Ensembles avec suffixes", [
        (["R*", "N*", "Z*", "Q*", "C*"],
         [r"\mathbb{R}^*"], "privé de 0"),
        (["R+", "N+", "Z+", "Q+"], [r"\mathbb{R}^+"], "positifs (≥ 0)"),
        (["R-", "Z-"], [r"\mathbb{R}^-"], "négatifs (≤ 0)"),
        (["R*+", "R+*"], [r"\mathbb{R}^{*+}"], "strictement positifs"),
        (["R*-", "R-*"], [r"\mathbb{R}^{*-}"], "strictement négatifs"),
    ]),

    # ----------------------------------------------------------------
    ("9. Exclusion d'éléments dans un ensemble", [
        (["R-{0}", "R-{-1}", "R-{1,2}"],
         [r"\mathbb{R} \setminus \{0\}",
          r"\mathbb{R} \setminus \{-1\}"]),
        (["R/{2}"], [r"\mathbb{R} \setminus \{2\}"]),
    ]),

    # ----------------------------------------------------------------
    ("10. Intervalles — convention française", [
        (["[0;1]", "[0,1]", "[0 1]"], [r"[0,\ 1]"],
         "séparateur ; ou , ou espace"),
        (["[0;+inf[", "[0,+inf[", "[0 +inf["],
         [r"[0,\ +\infty["], "semi-ouvert à droite"),
        (["]0;1]", "]0,1]", "]-inf;5]"],
         [r"]0,\ 1]", r"]-\infty,\ 5]"], "semi-ouvert à gauche"),
        (["]0;1[", "]-inf;+inf[", "]-inf,+inf["],
         [r"]0,\ 1[", r"]-\infty,\ +\infty["], "ouvert des deux côtés"),
    ]),

    # ----------------------------------------------------------------
    ("11. Union & intersection", [
        (["[0;12]U[16;20[", "[0;12] U [16;20["],
         [r"[0,\ 12] \cup [16,\ 20["]),
        (["[0;10] inter [5;15]"],
         [r"[0,\ 10] \cap [5,\ 15]"]),
        (["A union B"], [r"A \cup B"]),
        (["A inter B"], [r"A \cap B"]),
    ]),

    # ----------------------------------------------------------------
    ("12. Appartenance & quantificateurs (FR)", [
        (["x in R", "x dans R", "x app R",
          "x appartient R"],
         [r"x \in \mathbb{R}"]),
        (["pour tout x in R", "quel que soit x in R",
          "qq x dans R", "qqe x in R",
          "forall x in R"],
         [r"\forall x \in \mathbb{R}"]),
        (["il existe alpha tq alpha > 0",
          "existe alpha tq alpha > 0",
          "exists alpha tq alpha > 0"],
         [r"\exists \alpha \mid \alpha > 0"]),
    ]),

    # ----------------------------------------------------------------
    ("13. Raccourcis FR ultra-courts (∀ et ∈)", [
        (["Vx(R", "Vx C R"],
         [r"\forall x \in \mathbb{R}"],
         "V préfixé + ident collé"),
        (["Vx,y(R", "Vx,y C R"],
         [r"\forall x, y \in \mathbb{R}"]),
        (["V x ( R", "V x(R", "V x C R"],
         [r"\forall x \in \mathbb{R}"]),
        (["V x ( R*", "V x ( R-{0}"],
         [r"\forall x \in \mathbb{R}^*",
          r"\forall x \in \mathbb{R} \setminus \{0\}"]),
        (["V x,y ( R", "V x, y ( R+", "V x,y,z ( R"],
         [r"\forall x, y \in \mathbb{R}",
          r"\forall x, y \in \mathbb{R}^+",
          r"\forall x, y, z \in \mathbb{R}"]),
        (["V x ( [0;1]", "V x ( [0;1]U[3;4]"],
         [r"\forall x \in [0, 1]",
          r"\forall x \in [0, 1] \cup [3, 4]"]),
        (["V x"], [r"\forall x"], "sans ensemble"),
        (["x ( R", "x C R", "x(R"],
         [r"x \in \mathbb{R}"]),
        (["x ( R*", "x ( R-{0}"],
         [r"x \in \mathbb{R}^*",
          r"x \in \mathbb{R} \setminus \{0\}"]),
        (["x,y ( R", "x, y ( R*", "x,y C R"],
         [r"x, y \in \mathbb{R}", r"x, y \in \mathbb{R}^*"]),
        (["x ( [0;1]", "x ( [0;1]U[3;4]"],
         [r"x \in [0, 1]", r"x \in [0, 1] \cup [3, 4]"]),
    ]),

    # ----------------------------------------------------------------
    ("14. Courbes & ensembles de définition (lycée FR)", [
        (["Cf", "Cg", "Ch"],
         [r"\mathcal{C}_{f}", r"\mathcal{C}_{g}"],
         "courbe représentative"),
        (["Df", "Dg", "Dh"],
         [r"D_{f}", r"D_{g}"],
         "ensemble de définition"),
    ]),

    # ----------------------------------------------------------------
    ("15. Fonctions trigonométriques directes", [
        (["cos(x)", "cos x"], [r"\cos(x)"]),
        (["sin(x)", "sin x"], [r"\sin(x)"]),
        (["tan(x)", "tan x", "tg(x)", "tg x"], [r"\tan(x)"]),
        (["cot(x)", "cotan(x)"], [r"\cot(x)"]),
        (["sec(x)"], [r"\sec(x)"]),
        (["csc(x)", "cosec(x)"], [r"\csc(x)"]),
    ]),

    # ----------------------------------------------------------------
    ("16. Fonctions trigonométriques inverses", [
        (["arccos(x)", "acos(x)"], [r"\arccos(x)"]),
        (["arcsin(x)", "asin(x)"], [r"\arcsin(x)"]),
        (["arctan(x)", "atan(x)"], [r"\arctan(x)"]),
    ]),

    # ----------------------------------------------------------------
    ("17. Fonctions hyperboliques", [
        (["sinh(x)", "sh(x)"], [r"\sinh(x)"]),
        (["cosh(x)", "ch(x)"], [r"\cosh(x)"]),
        (["tanh(x)", "th(x)"], [r"\tanh(x)"]),
        (["argsh(x)", "arcsinh(x)", "asinh(x)"],
         [r"\mathrm{argsh}(x)"]),
        (["argch(x)", "arccosh(x)", "acosh(x)"],
         [r"\mathrm{argch}(x)"]),
        (["argth(x)", "arctanh(x)", "atanh(x)"],
         [r"\mathrm{argth}(x)"]),
    ]),

    # ----------------------------------------------------------------
    ("18. Logarithme & exponentielle", [
        (["ln(x)", "ln x"], [r"\ln(x)"]),
        (["log(x)", "log x", "lg(x)"], [r"\log(x)"]),
        (["exp(x)", "exp x"], [r"\exp(x)"]),
    ]),

    # ----------------------------------------------------------------
    ("19. Puissances de fonctions nommées", [
        (["sin^2(x)", "sin^2 x"], [r"\sin^2(x)"]),
        (["cos^3 x", "cos^3(x)"], [r"\cos^3(x)"]),
        (["ln^2 x", "ln^2(x)"], [r"\ln^2(x)"]),
        (["tan^2 x"], [r"\tan^2(x)"]),
        (["sin2x", "cos3(x)"], [r"\sin^2(x)", r"\cos^3(x)"],
         "lecture alternative sin² / cos³"),
    ]),

    # ----------------------------------------------------------------
    ("20. Appel et composition de fonctions", [
        (["f(x)"], [r"f(x)"]),
        (["g(x,y)", "g(x, y)"], [r"g(x, y)"]),
        (["f o g", "f°g", "f ° g"], [r"f \circ g"]),
    ]),

    # ----------------------------------------------------------------
    ("21. Dérivées — notation prime", [
        (["f'(x)", "g'(t)", "u'(n)"],
         [r"f'(x)", r"g'(t)"]),
        (["f''(x)", 'f"(x)'], [r"f''(x)"]),
    ]),

    # ----------------------------------------------------------------
    ("22. Dérivées — notation Leibniz et Newton", [
        (["df/dx", "dy/dt", "dg/du"],
         [r"\frac{df}{dx}", r"\frac{dy}{dt}"]),
        (["dot x"], [r"\dot{x}"], "dérivée temporelle (Newton)"),
        (["ddot x"], [r"\ddot{x}"], "dérivée seconde temporelle"),
    ]),

    # ----------------------------------------------------------------
    ("23. Dérivées partielles", [
        (["partial f / partial x"],
         [r"\frac{\partial f}{\partial x}"]),
    ]),

    # ----------------------------------------------------------------
    ("24. Équations différentielles", [
        (["y' = ky"], [r"y' = ky"]),
        (["f'(x) = 2x"], [r"f'(x) = 2x"]),
    ]),

    # ----------------------------------------------------------------
    ("25. Sommes", [
        (["Sum k=1 to 10 f(k)", "sum k=1 a n f(k)",
          "somme k=1 to n k^2"],
         [r"\sum_{k=1}^{10} f(k)",
          r"\sum_{k=1}^{n} f(k)"]),
        (["Sum k=1 k<10 f(k)+1"],
         [r"\sum_{k=1}^{10} f(k)+1"],
         "borne haute via inégalité"),
        (["Sum(k,1,10) f(k)"],
         [r"\sum_{k=1}^{10} f(k)"],
         "format Mathematica 3-args"),
    ]),

    # ----------------------------------------------------------------
    ("26. Produits", [
        (["prod k=1 to n f(k)", "produit k=1 a 10 k"],
         [r"\prod_{k=1}^{n} f(k)",
          r"\prod_{k=1}^{10} k"]),
        (["prod(k,1,n) k"], [r"\prod_{k=1}^{n} k"]),
    ]),

    # ----------------------------------------------------------------
    ("27. Intégrales — format court", [
        (["int 0 to 1 x^2 dx"],
         [r"\int_{0}^{1} x^2\, dx"]),
        (["integrale 0 a pi sin(x)"],
         [r"\int_{0}^{\pi} \sin(x)\, dx"]),
        (["int(x,0,1) x^2"],
         [r"\int_{0}^{1} x^2\, dx"], "format Mathematica"),
    ]),

    # ----------------------------------------------------------------
    ("28. Intégrales — format FR long", [
        (["int de 0 a 1 de x^2 dx",
          "integrale de 0 a pi de sin(x) dx"],
         [r"\int_{0}^{1} x^2\, dx",
          r"\int_{0}^{\pi} \sin(x)\, dx"]),
        (["int de 0 a 1 x^2", "int 0 a 1 x^2"],
         [r"\int_{0}^{1} x^2\, dx"]),
        (["int x^2 dx", "int x^2",
          "integrale de sin(x)"],
         [r"\int x^2\, dx",
          r"\int \sin(x)\, dx"], "intégrale indéfinie"),
    ]),

    # ----------------------------------------------------------------
    ("29. Intégrales multiples & de contour", [
        (["iint f dx dy"], [r"\iint f\, dx\, dy"]),
        (["oint f dl"], [r"\oint f\, dl"]),
        (["iiint ..."], [r"\iiint"]),
    ]),

    # ----------------------------------------------------------------
    ("30. Limites — formats standard", [
        (["lim x -> inf", "limite x -> +inf",
          "lim x tends vers 1", "lmt x -> 1"],
         [r"\lim_{x \to \infty}",
          r"\lim_{x \to +\infty}",
          r"\lim_{x \to 1}"]),
        (["lim quand x tend vers 0",
          "lim lorsque x tend vers 0",
          "lim qd x tend vers 0"],
         [r"\lim_{x \to 0}"]),
    ]),

    # ----------------------------------------------------------------
    ("31. Limites avec fonction", [
        (["lim x -> 1 f(x)",
          "lim x tend vers 0 f(x)"],
         [r"\lim_{x \to 1} f(x)",
          r"\lim_{x \to 0} f(x)"]),
        (["lim x->0 (2x+1)/(3x+3)^2"],
         [r"\lim_{x \to 0} \frac{2x+1}{(3x+3)^2}"],
         "expression quelconque après la cible"),
        (["lim x->0 x^2"], [r"\lim_{x \to 0} x^2"]),
        (["lim f(x) qd x -> 1",
          "lim f(x) quand x tend vers 1",
          "limite f(x) x -> +inf"],
         [r"\lim_{x \to 1} f(x)",
          r"\lim_{x \to +\infty} f(x)"],
         "ordre FR classique : fonction puis condition"),
        (["limite de f en 0"], [r"\lim_{0} f"]),
        (["Lim(x,0) f(x)", "lim(n,+inf) 1/n"],
         [r"\lim_{x \to 0} f(x)",
          r"\lim_{n \to +\infty} \frac{1}{n}"],
         "format Mathematica"),
    ]),

    # ----------------------------------------------------------------
    ("32. Limites unilatérales", [
        (["lim x->0+ f(x)"],
         [r"\lim_{x \to 0^+} f(x)"]),
        (["lim x->0- f(x)"],
         [r"\lim_{x \to 0^-} f(x)"]),
        (["lim x -> 0+ 1/x"],
         [r"\lim_{x \to 0^+} \frac{1}{x}"]),
        (["lim x -> 0 +"], [r"\lim_{x \to 0^+}"],
         "sans fonction"),
    ]),

    # ----------------------------------------------------------------
    ("33. Limsup & liminf", [
        (["limsup x -> inf f(x)"],
         [r"\mathrm{lim\,sup}_{x \to \infty} f(x)"]),
        (["liminf n -> inf u_n"],
         [r"\mathrm{lim\,inf}_{n \to \infty} u_n"]),
    ]),

    # ----------------------------------------------------------------
    ("34. Vecteurs — notation explicite", [
        (["vec AB", "vecteur AB", "vect AB"],
         [r"\vec{AB}"]),
        (["vec(u)", "vec u"], [r"\vec{u}"]),
        (["vec u . vec v"], [r"\vec{u} \cdot \vec{v}"],
         "produit scalaire"),
        (["vec AB + vec BC"],
         [r"\vec{AB} + \vec{BC}"]),
    ]),

    # ----------------------------------------------------------------
    ("35. Identifiants 2-lettres majuscules — lectures multiples", [
        (["AB"], [r"AB", r"\vec{AB}", r"\|AB\|"],
         "3 lectures proposées : littéral, vecteur, norme"),
        (["AB+BC=AC"],
         [r"\vec{AB}+\vec{BC}=\vec{AC}",
          r"\|AB\|+\|BC\|=\|AC\|"],
         "relation de Chasles — 2 lectures"),
        (["AB.BC"], [r"\vec{AB} \cdot \vec{BC}"],
         "produit scalaire"),
        (["AB^BC"], [r"\vec{AB} \wedge \vec{BC}"],
         "produit vectoriel"),
        (["AB = 5"], [r"AB = 5"], "longueur"),
    ]),

    # ----------------------------------------------------------------
    ("36. Composantes de vecteurs 2D/3D", [
        (["u(x,y)", "u(x y)"], [r"\vec{u}\ \binom{x}{y}"]),
        (["u(x,y,z)", "u(x y z)"],
         [r"\vec{u}\ \binom{x}{\binom{y}{z}}"]),
        (["AB(x,y)", "AB(x y)"],
         [r"\vec{AB}\ \binom{x}{y}"]),
        (["AB(x y z)"],
         [r"\vec{AB}\ \binom{x}{\binom{y}{z}}"]),
    ]),

    # ----------------------------------------------------------------
    ("37. Scalaire × vecteur", [
        (["lambda u", "alpha v", "beta w"],
         [r"\lambda \vec{u}",
          r"\alpha \vec{v}",
          r"\beta \vec{w}"]),
        (["2u", "3v", "-u"],
         [r"2 \vec{u}", r"3 \vec{v}"]),
    ]),

    # ----------------------------------------------------------------
    ("38. Déterminant vectoriel", [
        (["det(u,v)", "Det(u ; v)",
          "det(u, v)", "Det(u,v)"],
         [r"\det(\vec{u},\, \vec{v})"]),
        (["Det(AM ; u)", "det(AM ; v)"],
         [r"\det(\vec{AM},\, \vec{u})"]),
    ]),

    # ----------------------------------------------------------------
    ("39. Normes & valeur absolue", [
        (["|x|", "|x-1|", "|2x+3|"],
         [r"|x|", r"|x-1|"]),
        (["||u||"], [r"\|u\|"], "norme"),
    ]),

    # ----------------------------------------------------------------
    ("40. Segments, droites, demi-droites", [
        (["[AB]", "[CD]"], [r"[AB]", r"[CD]"], "segment"),
        (["(AB)"], [r"(AB)"], "droite"),
        (["[AB)"], [r"[AB)"], "demi-droite fermée en A"),
        (["(AB]"], [r"(AB]"], "demi-droite fermée en B"),
    ]),

    # ----------------------------------------------------------------
    ("41. Suites", [
        (["un", "vn", "wn", "sn", "tn",
          "Un", "Vn", "Wn"],
         [r"u_n", r"v_n", r"w_n",
          r"U_n", r"V_n"]),
        (["(un)", "(vn)"], [r"(u_n)", r"(v_n)"]),
        (["u(n+1) = 2*un + 3"],
         [r"u_{n+1} = 2 \cdot u_n + 3"]),
        (["(un) converge vers 0"], [r"(u_n) \to 0"]),
    ]),

    # ----------------------------------------------------------------
    ("42. Probabilités", [
        (["P(A)", "Pr(A)", "proba(A)"], [r"P(A)"]),
        (["P(A inter B)"], [r"P(A \cap B)"]),
        (["P(A|B)"], [r"P(A \mid B)"], "probabilité conditionnelle"),
    ]),

    # ----------------------------------------------------------------
    ("43. Espérance, variance, covariance", [
        (["E(X)", "esp(X)", "esperance(X)"], [r"E(X)"]),
        (["V(X)", "var(X)", "variance(X)"], [r"V(X)"]),
        (["V(x+1)"], [r"\sqrt{x+1}"],
         "lecture alternative racine carrée"),
        (["cov(X,Y)", "Cov(X,Y)", "covariance(X,Y)"],
         [r"\mathrm{Cov}(X, Y)"]),
        (["corr(X,Y)", "Corr(X,Y)", "correlation(X,Y)"],
         [r"\mathrm{Corr}(X, Y)"]),
    ]),

    # ----------------------------------------------------------------
    ("44. Moyenne statistique", [
        (["xbarre", "ybarre", "xbar"],
         [r"\overline{x}", r"\overline{y}"]),
        (["x barre", "y bar"],
         [r"\overline{x}", r"\overline{y}"]),
    ]),

    # ----------------------------------------------------------------
    ("45. Opérateurs nommés — algèbre linéaire", [
        (["det(A)", "determinant(A)"], [r"\det(A)"]),
        (["tr(A)", "trace(A)"], [r"\mathrm{tr}(A)"]),
        (["rg(A)", "rang(A)"], [r"\mathrm{rg}(A)"]),
        (["rank(A)"], [r"\mathrm{rank}(A)"]),
        (["Vect(u,v)"], [r"\mathrm{Vect}(u, v)"]),
        (["Ker f", "ker(f)", "noyau(f)"], [r"\ker(f)"]),
        (["Image(f)", "image(f)"], [r"\mathrm{Im}(f)"]),
        (["Dom(f)", "dom(f)"], [r"\mathrm{Dom}(f)"]),
        (["card(A)", "cardinal(A)"], [r"\mathrm{card}(A)"]),
        (["diag(1,2,3)"], [r"\mathrm{diag}(1, 2, 3)"]),
        (["dim E", "dim(E)", "dimension(E)"],
         [r"\dim(E)"]),
        (["Re(z)"], [r"\mathrm{Re}(z)"]),
        (["arg(z)"], [r"\arg(z)"]),
    ]),

    # ----------------------------------------------------------------
    ("46. Opérateurs nommés — arithmétique & mesure", [
        (["gcd(m,n)", "pgcd(m,n)"], [r"\gcd(m, n)"]),
        (["ppcm(m,n)"], [r"\mathrm{ppcm}(m, n)"]),
        (["lcm(m,n)"], [r"\mathrm{lcm}(m, n)"]),
        (["mes(A)"], [r"\mathrm{mes}(A)"]),
    ]),

    # ----------------------------------------------------------------
    ("47. Combinatoire — coefficient binomial", [
        (["C(n,k)", "C(5,2)", "C(10,3)"],
         [r"\binom{n}{k}", r"\binom{5}{2}"]),
        (["C_n^k", "C_5^2"],
         [r"\binom{n}{k}", r"\binom{5}{2}"],
         "notation française"),
        (["binom(n,k)", "comb(n,k)", "comb(10,3)"],
         [r"\binom{n}{k}", r"\binom{10}{3}"]),
    ]),

    # ----------------------------------------------------------------
    ("48. Coordonnées indexées par un point", [
        (["xA", "yB", "zM"],
         [r"x_A", r"y_B", r"z_M"]),
        (["xa", "ym"], [r"x_A", r"y_M"],
         "minuscule automatiquement normalisée"),
    ]),

    # ----------------------------------------------------------------
    ("49. Symboles ensemblistes", [
        (["A subset B", "A \\subset B"], [r"A \subset B"]),
        (["A subseteq B", "A \\subseteq B"], [r"A \subseteq B"]),
        (["A \\setminus B", "A \\ B"], [r"A \setminus B"]),
    ]),

    # ----------------------------------------------------------------
    ("50. Symboles & opérateurs divers", [
        (["+-", "+/-"], [r"\pm"], "plus ou moins"),
        (["a otimes b"], [r"a \otimes b"]),
        (["a oplus b"], [r"a \oplus b"]),
        (["a // b", "a \\parallel b"], [r"a \parallel b"]),
        (["perp", "\\perp"], [r"\perp"]),
        (["a mod b", "a modulo b"], [r"a\, \mathrm{mod}\, b"]),
        (["a \\mid b"], [r"a \mid b"], "divise"),
        (["a \\nmid b", "a !| b"], [r"a \nmid b"], "ne divise pas"),
        (["exists!"], [r"\exists!"]),
        (["inf", "infty", "infini", "infinity", "oo"],
         [r"\infty"]),
        (["limsup", "liminf"],
         [r"\mathrm{lim\,sup}", r"\mathrm{lim\,inf}"]),
    ]),

    # ----------------------------------------------------------------
    ("51. Landau", [
        (["Theta(n)"], [r"\Theta(n)"]),
        (["Omega(n)"], [r"\Omega(n)"]),
        (["o(x^2)", "O(n log n)"],
         [r"o(x^2)", r"O(n \log n)"], "rendu direct"),
    ]),

    # ----------------------------------------------------------------
    ("52. Système d'équations", [
        (["syst x+y=5 ; 2x-y=1",
          "systeme x+y=5, 2x-y=1"],
         [r"\begin{cases} x+y=5 \\ 2x-y=1 \end{cases}"]),
    ]),

    # ----------------------------------------------------------------
    ("53. Lettres grecques — minuscules", [
        (["alpha"], [r"\alpha"]),
        (["beta"], [r"\beta"]),
        (["gamma"], [r"\gamma"]),
        (["delta"], [r"\delta"]),
        (["epsilon", "eps"], [r"\varepsilon"]),
        (["zeta"], [r"\zeta"]),
        (["eta"], [r"\eta"]),
        (["theta"], [r"\theta"]),
        (["iota"], [r"\iota"]),
        (["kappa"], [r"\kappa"]),
        (["lambda"], [r"\lambda"]),
        (["mu"], [r"\mu"]),
        (["nu"], [r"\nu"]),
        (["xi"], [r"\xi"]),
        (["pi"], [r"\pi"]),
        (["rho"], [r"\rho"]),
        (["sigma"], [r"\sigma"]),
        (["tau"], [r"\tau"]),
        (["upsilon"], [r"\upsilon"]),
        (["phi"], [r"\phi"]),
        (["chi"], [r"\chi"]),
        (["psi"], [r"\psi"]),
        (["omega"], [r"\omega"]),
    ]),

    # ----------------------------------------------------------------
    ("54. Lettres grecques — majuscules", [
        (["Gamma"], [r"\Gamma"]),
        (["Delta"], [r"\Delta"]),
        (["Theta"], [r"\Theta"]),
        (["Lambda"], [r"\Lambda"]),
        (["Xi"], [r"\Xi"]),
        (["Pi"], [r"\Pi"]),
        (["Sigma"], [r"\Sigma"]),
        (["Upsilon"], [r"\Upsilon"]),
        (["Phi"], [r"\Phi"]),
        (["Psi"], [r"\Psi"]),
        (["Omega"], [r"\Omega"]),
    ]),

    # ----------------------------------------------------------------
    ("55. Lettres grecques — variantes", [
        (["varepsilon", "varep"], [r"\varepsilon"]),
        (["vartheta"], [r"\vartheta"]),
        (["varpi"], [r"\varpi"]),
        (["varrho"], [r"\varrho"]),
        (["varsigma"], [r"\varsigma"]),
        (["varphi"], [r"\varphi"]),
    ]),
]


# ============================================================

NOTES_STOP_PHRASES = [
    "<b>Stop phrases FR</b> — absorbées automatiquement, n'empêchent "
    "pas la conversion : "
    "<font face='Courier' size='8'>on a, on sait que, on pose, on note, "
    "on cherche, soit, soient, posons, notons, considerons, "
    "supposons que, il existe, pour tout, quel que soit, "
    "tel que, tels que, telle que</font>.",

    "<b>Connecteurs FR neutres</b> (traversés par l'analyse) : "
    "<font face='Courier' size='8'>de, du, des, la, le, les, a, au, aux, "
    "pour, par, et, avec, sur, dans, entre, sous, vers, en</font>.",

    "<b>Stop words structurels</b> (absorbés par les patterns) : "
    "<font face='Courier' size='8'>to, from, il, elle, a, ai, as, avons, "
    "avez, ont, est, sont, etait, etaient, sera, seront, vaut, valent, "
    "donne, donnent, donc, alors, or, car, ainsi, ensuite</font>.",

    "<b>Mots-clés fuzzy</b> (distance Levenshtein 1 acceptée) : "
    "<font face='Courier' size='8'>lim/limite/lmt, sqrt/rac/racine, "
    "int/integrale/integ, sum/somme, prod/produit, "
    "derivee/deriv, primitive/primit, inf/infty/infini/oo</font>.",
]


# ============================================================

def build(doc):
    flow = []
    flow.append(Paragraph(
        "MathCursor — Couverture exhaustive des saisies clavier", h1))
    flow.append(Paragraph(
        "Ce document liste toutes les syntaxes acceptées et parsées par "
        "le moteur, telles que définies dans "
        "<font face='Courier' size='8'>data/yaml_domains/</font> "
        "(alias de tokens et exemples de patterns). Langue par défaut : FR. "
        "Les saisies marquées par des lignes séparées sont des alias "
        "strictement équivalents.", body))
    flow.append(Spacer(1, 4))
    flow.append(Paragraph(
        "Quand plusieurs rendus apparaissent à droite, le moteur propose "
        "les lectures en parallèle dans la popup (ex. « AB » → littéral, "
        "vecteur, norme ; « 3sqrt(4) » → 3×√4 ou racine cubique).", body))
    flow.append(Spacer(1, 4))
    flow.append(Paragraph(
        "Colonne gauche : caractères tels que tapés au clavier (ASCII). "
        "Les équivalents Unicode (α, →, ↦, ∈, ∂…) sont également reconnus "
        "quand l'élève les colle, mais ne sont pas listés ici.", body))

    for title, rows in SECTIONS:
        flow.append(Spacer(1, 6))
        flow.append(Paragraph(title, h2))
        flow.append(make_table(rows))

    flow.append(Spacer(1, 10))
    flow.append(Paragraph("Annexes", h2))
    for note in NOTES_STOP_PHRASES:
        flow.append(Spacer(1, 3))
        flow.append(Paragraph(note, body))

    flow.append(Spacer(1, 10))
    flow.append(Paragraph(
        "<i>Généré automatiquement depuis "
        "<font face='Courier' size='8'>data/yaml_domains/</font> "
        "— branche Model+Latex.</i>", body))
    doc.build(flow)


if __name__ == "__main__":
    doc = SimpleDocTemplate(
        OUT, pagesize=A4,
        leftMargin=1.5 * cm, rightMargin=1.5 * cm,
        topMargin=1.5 * cm, bottomMargin=1.5 * cm,
        title="MathCursor — Couverture",
        author="MathCursor",
    )
    build(doc)
    print("OK ->", OUT)
