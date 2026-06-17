"""chars -> tokens — port de Lexer.cs.

Générique : consulte le VOCABULAIRE (data) pour savoir si un lexème est
infixe/préfixe/n-aire. Ne nomme aucun opérateur.
"""
from .node import Token
from .vocab import VOCAB, SPLITTABLE, ROLE, SEP, UNITS_COMPOUND

SPLIT_CAP = 4  # <= 2^4 flux ; au-delà, on découpe tout (un seul flux)


def _is_alpha(c): return ('a' <= c <= 'z') or ('A' <= c <= 'Z')
def _is_digit(c): return '0' <= c <= '9'
def _is_upper(c): return 'A' <= c <= 'Z'
# autocorrection FR de Word : U+00A0 / U+202F = espaces pour le lexer.
def _is_space(c): return c in (' ', ' ', ' ')


# jonction de tokens collés (JOIN) — premier match gagne.
JOIN = [
    {"left": ["name"], "right": ["num"], "roles": ["sup"], "sticky": True, "cross": False},
    {"left": ["close"], "right": ["num"], "roles": ["sup"], "sticky": True, "cross": False},
    {"left": ["unit"], "right": ["num"], "roles": ["sup"], "sticky": True, "cross": False},
    {"left": ["num"], "right": ["unit"], "roles": ["unitOp"], "sticky": True, "cross": True},
    {"left": ["name", "unit", "post"], "right": ["open"], "roles": ["apply"], "sticky": False, "cross": False},
    {"left": ["num", "name", "close", "post"], "right": ["name", "open", "func"], "roles": ["implicit"], "sticky": False, "cross": False},
]


def lex(src, culture, choices=None, runs_out=None):
    toks = []
    i = 0
    saw_space = False
    brk = 0
    brc = 0

    def ch(idx):
        return src[idx] if 0 <= idx < len(src) else '\0'

    def spaced(a, b):
        return _is_space(ch(a)) or _is_space(ch(b))

    def push(t):
        nonlocal saw_space
        t.space_before = saw_space
        toks.append(t)
        saw_space = False

    def last():
        return toks[-1] if toks else None

    def word(w, sp):
        k = culture.canon(w)
        v = VOCAB.get(k)
        g = v if (v is not None and v.shape == "atom") else VOCAB.get(w.lower())
        # repli casse (Word AutoCorrect « Sum » -> sum) : mots-clés non-atomes >= 2.
        if v is None and not (g is not None and g.shape == "atom") and len(w) >= 2:
            k2 = culture.canon(w.lower())
            v2 = VOCAB.get(k2)
            if k2 != k and v2 is not None and v2.shape is not None and v2.shape != "atom":
                k = k2
                v = v2

        if g is not None and g.shape == "atom" and g.alts is not None:
            push(Token(kind="atom", syms=list(g.alts), coh=g.coh))
        elif g is not None and g.shape == "atom":
            push(Token(kind="atom", sym=(g.upper if _is_upper(w[0]) else g.lower)))
        elif v is not None and v.shape == "infix":
            prev = last()
            binary = prev is not None and prev.kind in ("atom", "rparen", "bracket", "bar", "postfix")
            if binary:
                push(Token(kind="infix", sym=k, spaced=sp))
            elif v.unary is not None:
                push(Token(kind="prefix", sym=v.unary, spaced=sp))
            else:
                push(Token(kind="atom", sym=w))
        elif v is not None and v.word_space and not _is_space(ch(i)):
            push(Token(kind="atom", sym=w))
        elif v is not None and v.shape is not None:
            push(Token(kind=v.shape, sym=k, spaced=sp))
        elif v is not None and v.unit_word:
            push(Token(kind="atom", sym=w, unit_word=True))
        else:
            push(Token(kind="atom", sym=w))

    def best_splittable_at(s, at):
        b = ""
        for key in SPLITTABLE:
            if len(key) > len(b) and at + len(key) <= len(s) and s.startswith(key, at):
                b = key
        return b

    def decompose(s):
        outp = []
        j = 0
        while j < len(s):
            best = best_splittable_at(s, j)
            if best == "" and j == 0 and _is_upper(s[0]) and len(s) >= 2:
                best = best_splittable_at(s[0].lower() + s[1:], 0)
            if best:
                outp.append(best); j += len(best)
            elif _is_upper(s[j]):
                st = j
                while j < len(s) and _is_upper(s[j]):
                    j += 1
                outp.append(s[st:j])
            else:
                outp.append(s[j]); j += 1
        return outp

    while i < len(src):
        c = src[i]
        if _is_space(c):
            saw_space = True; i += 1; continue
        if c == '\\' and _is_alpha(ch(i + 1)):
            i += 1; continue
        if c == '(':
            push(Token(kind="lparen")); i += 1; continue
        if c == ')':
            push(Token(kind="rparen")); i += 1; continue
        if c == '[' or c == ']':
            push(Token(kind="bracket", sym=c)); brk += 1; i += 1; continue
        if c == '{':
            push(Token(kind="lbrace")); brc += 1; i += 1; continue
        if c == '}':
            push(Token(kind="rbrace")); brc -= 1; i += 1; continue
        if c == '|':
            norm = ch(i + 1) == '|'
            push(Token(kind="bar", sym="norm" if norm else "abs")); i += 2 if norm else 1; continue
        if c in SEP:
            push(Token(kind="sep", sym=c, rank=SEP[c])); i += 1; continue

        # raccourcis grecs « @ » + lettre(s)
        if c == '@' and _is_alpha(ch(i + 1)):
            at0 = i; i += 1
            gr = ""
            while i < len(src) and _is_alpha(src[i]):
                gr += src[i]; i += 1
            gsp = spaced(at0 - 1, i)
            if len(gr) == 1:
                up = _is_upper(gr[0])
                low = "@" + gr[0].lower()
                key = culture.canon(low)
                if key != low:
                    ve = VOCAB[key]
                    if ve.alts is not None:
                        alts = ve.alts_upper if up else ve.alts
                        if alts is not None:
                            push(Token(kind="atom", syms=list(alts))); continue
                    elif (not up) or (ve.upper != ve.lower):
                        push(Token(kind="atom", sym=(ve.upper if up else ve.lower))); continue
            word(gr, gsp)
            continue

        # unité COMPOSÉE juste après un nombre (5 m/s)
        pv = last()
        if pv is not None and pv.num:
            best = ""
            for key in UNITS_COMPOUND:
                if len(key) > len(best) and src.startswith(key, i):
                    best = key
            if best:
                push(Token(kind="atom", sym=UNITS_COMPOUND[best], unit_word=True)); i += len(best); continue

        if _is_alpha(c):  # run de lettres
            st = i; s = ""
            while i < len(src) and _is_alpha(src[i]):
                s += src[i]; i += 1
            sp = spaced(st - 1, i)
            dots_len = 3 if src.startswith("...", i) else (1 if ch(i) == '…' else 0)
            if dots_len > 0:
                dv = VOCAB.get(culture.canon(s + "...")) or VOCAB.get(culture.canon(s.lower() + "..."))
                if dv is not None and dv.shape == "atom":
                    push(Token(kind="atom", sym=dv.lower)); i += dots_len; continue
            known = (culture.canon(s) in VOCAB) \
                or (VOCAB.get(s.lower()) is not None and VOCAB[s.lower()].shape == "atom") \
                or (len(s) >= 2 and culture.canon(s.lower()) in VOCAB)
            if not known:
                pieces = decompose(s)
                any_split = len(pieces) >= 2 and any(p in SPLITTABLE for p in pieces)
                if any_split:
                    if runs_out is not None:
                        runs_out.append(st)
                    if (choices(st) if choices is not None else True):
                        for kk in range(len(pieces)):
                            word(pieces[kk], kk == 0 and sp)
                        continue
            word(s, sp)
            continue

        if _is_digit(c):  # run de chiffres
            s = ""
            while i < len(src) and _is_digit(src[i]):
                s += src[i]; i += 1
            if ch(i) in culture.decimals_in and not (ch(i) == ',' and brc > 0) and _is_digit(ch(i + 1)):
                i += 1; s += culture.decimal_tex
                while i < len(src) and _is_digit(src[i]):
                    s += src[i]; i += 1
            push(Token(kind="atom", sym=s, num=True)); continue

        # opérateur infixe / postfixe / symbole-atome : PLUS-LONG-MATCH (3, 2, 1)
        matched = False
        for length in (3, 2, 1):
            if i + length > len(src):
                continue
            op = src[i:i + length]
            v = VOCAB.get(op)
            if v is None or v.shape not in ("infix", "postfix", "atom"):
                continue
            if v.shape == "atom":
                push(Token(kind="atom", sym=v.lower)); i += length; matched = True; break
            if v.shape == "postfix":
                push(Token(kind="postfix", sym=op)); i += length; matched = True; break
            prev = last()
            binary = prev is not None and prev.kind in ("atom", "rparen", "bracket", "bar", "postfix")
            after = ch(i + length)
            detached_r = _is_space(after) or after in ")],;"
            if length == 1 and v.post_sign is not None and binary and not saw_space and detached_r:
                push(Token(kind="infix", sym=ROLE["sup"], sticky=True))
                push(Token(kind="atom", sym=v.post_sign))
            elif length == 1 and v.unary is not None and not binary:
                push(Token(kind="prefix", sym=v.unary, spaced=spaced(i - 1, i + 1)))
            elif v.alts is not None:
                push(Token(kind="infix", syms=list(v.alts), spaced=spaced(i - 1, i + length)))
            else:
                push(Token(kind="infix", sym=op, spaced=spaced(i - 1, i + length)))
            i += length; matched = True; break
        if matched:
            continue
        raise Exception("caractère inattendu: " + c)

    # saisie en cours : fermer VIRTUELLEMENT les ( non fermées
    depth = 0
    for t in toks:
        depth += 1 if t.kind == "lparen" else (-1 if t.kind == "rparen" else 0)
    while depth > 0:
        toks.append(Token(kind="rparen", space_before=False, virtual=True)); depth -= 1
    if brk % 2 == 1:
        toks.append(Token(kind="bracket", sym="]", space_before=False, virtual=True))

    return _juxtapose(toks)


def _cat(t):
    if t is None:
        return None
    if t.kind == "atom":
        if t.unit_word:
            return "unit"
        all_digits = t.sym is not None and len(t.sym) > 0 and all(_is_digit(x) for x in t.sym)
        return "num" if (t.num or all_digits) else "name"
    return {"lparen": "open", "rparen": "close", "postfix": "post",
            "prefix": "func", "nary": "func"}.get(t.kind, "op")


def _juxtapose(toks):
    outp = []
    for idx in range(len(toks)):
        L = toks[idx]
        R = toks[idx + 1] if idx + 1 < len(toks) else None
        outp.append(L)
        if R is None:
            continue
        lc = _cat(L); rc = _cat(R)
        rule = None
        for r in JOIN:
            if lc in r["left"] and rc in r["right"]:
                rule = r; break
        if rule is None or (R.space_before and not rule["cross"]):
            continue
        syms = [ROLE[role] for role in rule["roles"] if role in ROLE]
        if not syms:
            continue
        t = Token(kind="infix", spaced=(rule["cross"] and R.space_before))
        if len(syms) > 1:
            t.syms = syms
        else:
            t.sym = syms[0]
        if "implicit" in rule["roles"] or "apply" in rule["roles"]:
            t.implicit = True
        if rule["sticky"]:
            t.sticky = True
        outp.append(t)
    return outp


def _popcount(m):
    return bin(m).count("1")


def lex_all(src, culture):
    runs = []
    whole = lex(src, culture, lambda st: False, runs)
    n = len(runs)
    if n == 0:
        return [(whole, 0)]
    if n > SPLIT_CAP:
        return [(lex(src, culture, lambda st: True), n), (whole, 0)]
    outp = []
    for m in range(1 << n):
        outp.append((lex(src, culture, lambda st, mm=m: (mm & (1 << runs.index(st))) != 0), _popcount(m)))
    return outp
