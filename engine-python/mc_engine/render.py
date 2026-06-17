"""AST -> LaTeX — port de LatexRenderer.cs.

Générique : dispatche vers le `render` déclaré par chaque symbole (data) ; la
culture (env matriciel, séparateur d'intervalle) est threadée en paramètre.
"""
from .vocab import VOCAB


def _child(c, parent, cu):
    # port de LatexRenderer.Child
    if c.type == "paren":
        return render(c.parts[0], cu) if parent.bracketed else render(c, cu)
    s = render(c, cu)
    if parent.bracketed:
        return s
    if c.type == "atom" and not parent.apply:
        return s
    self_grouped = c.sym in VOCAB and VOCAB[c.sym].bracketed
    looser = c.type == "infix" and VOCAB[c.sym].looseness > parent.looseness
    if (c.grouped and not self_grouped) or looser:
        return f"({s})"
    return s


def render(n, cu):
    t = n.type
    if t == "atom":
        return n.sym
    if t == "matrix":
        env = cu["matrixEnv"]
        body = " \\\\ ".join(" & ".join(render(x, cu) for x in row) for row in n.rows)
        return f"\\begin{{{env}}}" + body + f"\\end{{{env}}}"
    if t == "interval":
        return f"{n.lb}{render(n.parts[0], cu)}{cu['intervalSep']}{render(n.parts[1], cu)}{n.rb}"
    if t == "list":
        return ",".join(render(x, cu) for x in n.parts)
    if t == "tuple":
        return "(" + ",".join(render(x, cu) for x in n.parts) + ")"
    if t == "set":
        return "\\{" + ",".join(render(x, cu) for x in n.parts) + "\\}"
    if t == "paren":
        return (n.lb or "(") + render(n.parts[0], cu) + (n.rb or ")")
    if t == "postfix":
        c = n.parts[0]
        s = render(c, cu)
        wrap = c.type in ("infix", "prefix", "nary")
        return VOCAB[n.sym].render([f"({s})" if wrap else s], None)

    d = VOCAB[n.sym]
    if d.shape == "infix":
        if d.sup or d.sub:
            a = [_child(n.parts[0], d, cu), render(n.parts[1], cu)]
        else:
            a = [_child(c, d, cu) for c in n.parts]
        return d.render(a, n)
    # prefix / nary : l'argument paren se dissout (la fonction fournit ses délimiteurs)
    args = [render(p.parts[0], cu) if p.type == "paren" else render(p, cu) for p in n.parts]
    fn = d.render_for(len(n.parts)) if d.shape == "nary" else d.render
    return fn(args, n)
