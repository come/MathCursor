"""Orchestrateur GÉNÉRIQUE — port de ForestEngine.cs.

src -> candidats classés + décision :
  lex -> SEGMENTE aux coupes -> forêt par segment (borné) -> recombine
  -> filtre coupes -> coût -> tri -> dédoublonnage -> popup/auto.
"""
import math
from . import segment, score, render, culture as _culture_mod
from .lexer import lex_all
from .parser import parse as forest_parse
from .node import Node
from .vocab import VOCAB

POPUP_GAP = 2.0
COMBINE_CAP = 128
MAX_SHOW = 5
_CAT = [1, 1, 2, 5, 14, 42, 132, 429]
NOTE = "expression dense/ambiguë — ajoutez des espaces ou des parenthèses pour préciser."
SPLIT_PENALTY = 3.0


class EngineCandidate:
    __slots__ = ("latex", "cost")

    def __init__(self, latex, cost):
        self.latex = latex
        self.cost = cost


class AnalyzeResult:
    __slots__ = ("decision", "ranked", "has_note")

    def __init__(self, decision, ranked, has_note):
        self.decision = decision      # "auto" | "popup" | "erreur"
        self.ranked = ranked          # list[EngineCandidate]
        self.has_note = has_note


def _catalan(n):
    return _CAT[n] if 0 <= n < len(_CAT) else 429


class _Engine:
    def __init__(self, cu):
        self._cu = cu
        self._deep_note = False

    # callback de parsing d'un intérieur de parenthèse : MÊME pipeline (récursif).
    def _on_group(self, interior):
        parses, note = self._assemble(interior)
        if note is not None:
            self._deep_note = True
        return parses

    def _parses_of(self, toks):
        return [p for p in forest_parse(toks, self._on_group, self._cu) if not score.crosses_cut(p)]

    def _best_of(self, toks):
        best = None
        bc = math.inf
        for p in self._parses_of(toks):
            c = score.cost(p)
            if c < bc:
                bc = c
                best = p
        return best

    def _cheapest(self, lst):
        pool = [p for p in lst if not score.crosses_cut(p)]
        src = pool if pool else lst
        best = src[0]
        for p in src[1:]:
            if score.cost(p) < score.cost(best):
                best = p
        return best

    def _distinct_prod(self, lists):
        m = {}
        for i, c in enumerate(lists):
            k = ("s:" + c[0].sig) if (len(c) > 0 and c[0].sig is not None) else ("u:" + str(i))
            if k not in m:
                m[k] = len(c)
        p = 1
        for v in m.values():
            p *= v
        return p

    def _window_of(self, lst):
        pool = [p for p in lst if not score.crosses_cut(p)]
        src = pool if pool else lst
        best = math.inf
        for p in src:
            best = min(best, score.cost(p))
        return [p for p in src if score.cost(p) < best + POPUP_GAP]

    def _recombine(self, lists, ops, note):
        for c in lists:
            if len(c) == 0:
                return ([], note)
        if len(ops) >= 1:
            lists = [self._window_of(l) for l in lists]
        if len(ops) >= 1:
            br = _catalan(len(ops))
            if br * self._distinct_prod(lists) > COMBINE_CAP:
                note = NOTE
                lists = list(lists)
                order = sorted(range(len(lists)), key=lambda i: len(lists[i]), reverse=True)
                for i in order:
                    if br * self._distinct_prod(lists) <= COMBINE_CAP:
                        break
                    lists[i] = [self._cheapest(lists[i])]
        parses = [p for p in segment.combine(lists, ops) if not score.crosses_cut(p)]
        return (parses, note)

    def _fold(self, seg):
        operands, ops = segment.split_operands(seg)
        node = self._best_of(operands[0])
        k = 0
        while k < len(ops) and node is not None:
            right = self._best_of(operands[k + 1])
            if right is None:
                return None
            node = Node(type="infix", sym=ops[k].sym, spaced=ops[k].spaced, parts=[node, right])
            k += 1
        return node

    def _fold_smart(self, seg):
        parts, ops = segment.split_loosest(seg)
        if len(ops) >= 1 and all(len(p) > 0 for p in parts):
            lists = []
            ok = True
            for p in parts:
                pr, _n = self._assemble(p)
                if len(pr) == 0:
                    ok = False
                    break
                lists.append(pr)
            if ok:
                if len(ops) < segment.MAX_CHAIN:
                    rec, rnote = self._recombine(lists, ops, NOTE)
                    if len(rec) > 0:
                        return (rec, rnote)
                else:
                    node = self._cheapest(lists[0])
                    for k in range(len(ops)):
                        node = Node(type="infix", sym=ops[k].sym, spaced=ops[k].spaced,
                                    parts=[node, self._cheapest(lists[k + 1])])
                    return ([node], NOTE)
        f = self._fold(seg)
        return ([f] if f is not None else [], NOTE)

    def _assemble(self, toks):
        # 1) RELATIONS = coupe la plus forte
        rel_segs, rel_ops = segment.split_rel(toks)
        if len(rel_ops) >= 1:
            if len(rel_segs[0]) == 0:
                return ([], None)
            if len(rel_ops) >= segment.MAX_CHAIN:
                return self._fold_smart(toks)
            note = None
            lists = []
            for s in rel_segs:
                pr, n = self._assemble(s)
                if n is not None:
                    note = n
                lists.append(pr)
            return self._recombine(lists, rel_ops, note)

        # 2) N-AIRE en tête : forêt entière, pas de segmentation
        if len(toks) > 0 and toks[0].kind == "nary" and segment.chain_len(toks) < segment.MAX_CHAIN:
            parses = [p for p in forest_parse(toks, self._on_group, self._cu) if not score.crosses_cut(p)]
            return (parses, None)

        # 3) segmentation aux signes espacés + repli si trop long
        segs, ops = segment.split(toks)
        if len(ops) >= segment.MAX_CHAIN:
            return self._fold_smart(toks)
        note3 = None
        lists3 = []
        for seg in segs:
            if segment.chain_len(seg) < segment.MAX_CHAIN:
                lists3.append(forest_parse(seg, self._on_group, self._cu))
            else:
                note3 = NOTE
                lists3.append(self._fold_smart(seg)[0])
        return self._recombine(lists3, ops, note3)

    def _pair_skeletons(self, allp, kept):
        best_p = allp[0]
        best_c = math.inf
        for (n, off) in allp:
            c = score.cost(n) + off
            if c < best_c:
                best_c = c
                best_p = (n, off)
        n = best_p[0]
        if n.type != "nary" or n.sym is None or n.parts is None:
            return kept
        if not any(c.hole for c in n.parts):
            return kept
        if VOCAB[n.sym].variants is None:
            return kept

        sib = None
        sib_c = math.inf
        for (m, off) in allp:
            if m.type != "nary" or m.sym != n.sym or m.parts is None:
                continue
            if len(m.parts) == len(n.parts) or not any(c.hole for c in m.parts):
                continue
            c = score.cost(m) + off
            if c < sib_c:
                sib_c = c
                sib = (m, off)
        if sib is None:
            return kept

        pair_src = [(best_p, best_c), (sib, sib_c)]
        pair_src.sort(key=lambda x: len(x[0][0].parts), reverse=True)  # forme LONGUE d'abord
        pair = [(render.render(x[0][0], self._cu), x[1]) for x in pair_src]
        outk = list(pair)
        for k in kept:
            if all(o[0] != k[0] for o in outk):
                outk.append(k)
        return outk[:MAX_SHOW]

    def _finish(self, allp, note):
        if len(allp) == 0:
            return AnalyzeResult("erreur", [], False)
        cands = [(render.render(n, self._cu), score.cost(n) + off) for (n, off) in allp]
        cands.sort(key=lambda r: r[1])  # tri stable
        seen = set()
        ranked = []
        for (latex, c) in cands:
            if latex not in seen:
                seen.add(latex)
                ranked.append((latex, c))
        best = ranked[0][1]
        win = [r for r in ranked if r[1] < best + POPUP_GAP]
        kept = win[:MAX_SHOW]
        has_note = (note is not None) or (len(win) > MAX_SHOW)
        kept = self._pair_skeletons(allp, kept)
        decision = "popup" if len(kept) > 1 else "auto"
        return AnalyzeResult(decision, [EngineCandidate(l, c) for (l, c) in kept], has_note)

    def run(self, src):
        self._deep_note = False
        streams = []
        max_s = 0
        for (toks, splits) in lex_all(src, self._cu):
            parses, note = self._assemble(toks)
            if len(parses) == 0:
                continue
            streams.append((parses, splits, note))
            if splits > max_s:
                max_s = splits
        allp = []
        note = None
        for (parses, splits, n) in streams:
            for p in parses:
                allp.append((p, SPLIT_PENALTY * (max_s - splits)))
            if splits == max_s and n is not None:
                note = n
        return self._finish(allp, note if note is not None else (NOTE if self._deep_note else None))


def analyze(src, cu=None):
    return _Engine(cu if cu is not None else _culture_mod.FR).run(src)
