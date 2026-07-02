// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
// Copyright (C) 2026  Côme Percin
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Engine;

/// <summary>Un candidat de lecture : LaTeX + coût.</summary>
public sealed class EngineCandidate
{
    public string Latex { get; }
    public double Cost { get; }
    public EngineCandidate(string latex, double cost) { Latex = latex; Cost = cost; }
}

/// <summary>Résultat de l'analyse : décision (popup|auto|erreur) + candidats classés.</summary>
public sealed class AnalyzeResult
{
    public string Decision { get; }
    public IReadOnlyList<EngineCandidate> Ranked { get; }
    public bool HasNote { get; }   // message « expression dense/ambiguë »
    public AnalyzeResult(string decision, IReadOnlyList<EngineCandidate> ranked, bool hasNote)
    { Decision = decision; Ranked = ranked; HasNote = hasNote; }
}

// index.js — orchestrateur GÉNÉRIQUE : src → candidats classés + décision.
//   lex → SEGMENTE aux coupes → forêt par segment (borné) → recombine → filtre coupes
//   → coût → tri → dédoublonnage → popup/auto.
public sealed class ForestEngine
{
    private const double PopupGap = 2;
    private const int CombineCap = 128;
    private const int MaxShow = 5;
    private static readonly int[] Cat = { 1, 1, 2, 5, 14, 42, 132, 429 };
    private const string Note_ = "expression dense/ambiguë — ajoutez des espaces ou des parenthèses pour préciser.";

    private bool _deepNote;
    private readonly EngineCulture _culture;

    private ForestEngine(EngineCulture culture) { _culture = culture; }

    /// <summary>Analyse <paramref name="src"/> avec la culture donnée (défaut : <see cref="EngineCulture.Fr"/>).</summary>
    public static AnalyzeResult Analyze(string src, EngineCulture? culture = null)
        => new ForestEngine(culture ?? EngineCulture.Fr).Run(src);

    private static int Catalan(int n) => n >= 0 && n < Cat.Length ? Cat[n] : 429;

    private readonly struct Asm
    {
        public readonly List<Node> Parses;
        public readonly string? Note;
        public Asm(List<Node> parses, string? note) { Parses = parses; Note = note; }
    }

    private List<Node> ParsesOf(List<Token> toks) =>
        Forest.Parse(toks, OnGroup, _culture).Where(p => !Score.CrossesCut(p)).ToList();

    private Node? BestOf(List<Token> toks)
    {
        Node? best = null; double bc = double.PositiveInfinity;
        foreach (var p in ParsesOf(toks)) { var c = Score.Cost(p); if (c < bc) { bc = c; best = p; } }
        return best;
    }

    // REPLI par PRÉCÉDENCE (ADR 2026-06-12-Fix-fold-by-precedence) : remplace
    // le pliage à gauche aveugle — toutes précédences confondues — qui
    // fabriquait ((((2×x+2)×x)²+3)×x)³… en candidat UNIQUE (auto !) sur
    // « 2x+2x2+3x3+x4 » (la lecture naturelle coûtait 1,00 dans la forêt
    // complète, mesuré). Coupe aux opérateurs les plus LÂCHES présents,
    // assemble chaque part par le pipeline normal (récursif — les niveaux
    // décroissent strictement, ça termine), recombine sous les caps
    // existants ; chaîne encore trop longue à UN niveau → pli gauche PAR
    // NIVEAU (associatif à l'affichage : a+b+c plat). Vieux Fold = repli
    // ultime (part vide, rien d'assemblable).
    private Asm FoldSmart(List<Token> seg)
    {
        var (parts, ops) = Segment.SplitLoosest(seg);
        if (ops.Count >= 1 && parts.TrueForAll(p => p.Count > 0))
        {
            var lists = new List<List<Node>>();
            bool ok = true;
            foreach (var p in parts)
            {
                var r = Assemble(p);
                if (r.Parses.Count == 0) { ok = false; break; }
                lists.Add(r.Parses);
            }
            if (ok)
            {
                if (ops.Count < Segment.MaxChain)
                {
                    var rec = Recombine(lists, ops, Note_);
                    if (rec.Parses.Count > 0) return rec;
                }
                else
                {
                    var node = Cheapest(lists[0]);
                    for (int k = 0; k < ops.Count; k++)
                        node = new Node { Type = "infix", Sym = ops[k].Sym, Spaced = ops[k].Spaced, Parts = new() { node, Cheapest(lists[k + 1]) } };
                    return new Asm(new List<Node> { node }, Note_);
                }
            }
        }
        var f = Fold(seg);
        return new Asm(f != null ? new List<Node> { f } : new List<Node>(), Note_);
    }

    // REPLI ultime : pliage à gauche, chaque opérande parsée à part.
    private Node? Fold(List<Token> seg)
    {
        var (operands, ops) = Segment.SplitOperands(seg);
        var node = BestOf(operands[0]);
        for (int k = 0; k < ops.Count && node != null; k++)
        {
            var right = BestOf(operands[k + 1]);
            if (right == null) return null;
            node = new Node { Type = "infix", Sym = ops[k].Sym, Spaced = ops[k].Spaced, Parts = new() { node, right } };
        }
        return node;
    }

    private static Node Cheapest(List<Node> list)
    {
        var pool = list.Where(p => !Score.CrossesCut(p)).ToList();
        var src = pool.Count > 0 ? pool : list;
        return src.Aggregate((b, p) => Score.Cost(p) < Score.Cost(b) ? p : b);
    }

    // taille de combine par SIG DISTINCT (≠ dimKey de combine : ici les atomes co-varient).
    private static long DistinctProd(List<List<Node>> lists)
    {
        var m = new Dictionary<string, int>();
        for (int i = 0; i < lists.Count; i++)
        {
            var c = lists[i];
            string k = c.Count > 0 && c[0].Sig != null ? "s:" + c[0].Sig : "u:" + i;
            if (!m.ContainsKey(k)) m[k] = c.Count;
        }
        long p = 1; foreach (var v in m.Values) p *= v;
        return p;
    }

    private AnalyzeResult Finish(List<(Node N, double Off)> all, string? note)
    {
        if (all.Count == 0) return new AnalyzeResult("erreur", new List<EngineCandidate>(), false);
        var seen = new HashSet<string>();
        var ranked = all
            .Select(p => new EngineCandidate(LatexRenderer.Render(p.N, _culture), Score.Cost(p.N) + p.Off))
            .OrderBy(r => r.Cost)                       // tri stable (comme V8)
            .Where(r => seen.Add(r.Latex))              // garde le meilleur par rendu
            .ToList();
        double best = ranked[0].Cost;
        var win = ranked.Where(r => r.Cost < best + PopupGap).ToList();
        var kept = win.Take(MaxShow).ToList();
        bool hasNote = note != null || win.Count > MaxShow;
        kept = PairSkeletons(all, kept);
        return new AnalyzeResult(kept.Count > 1 ? "popup" : "auto", kept, hasNote);
    }

    // Paire de squelettes (ADR 2026-06-12 nary-skeleton-pair-preselection) :
    // si le MEILLEUR parse est un n-aire À TROUS directs dont l'entrée vocab a
    // des variantes, le squelette frère (autre arité, à trous, même tête) est
    // proposé AUSSI — forme LONGUE en tête (présélection = comportement
    // historique), décision popup. Pas de frère (guards, pas assez d'unités)
    // → rien ne change. Règle de PRÉSENTATION : le Score n'est pas touché.
    private List<EngineCandidate> PairSkeletons(List<(Node N, double Off)> all, List<EngineCandidate> kept)
    {
        var bestP = all[0]; double bestC = double.PositiveInfinity;
        foreach (var p in all)
        {
            double c = Score.Cost(p.N) + p.Off;
            if (c < bestC) { bestC = c; bestP = p; }
        }
        var n = bestP.N;
        if (n.Type != "nary" || n.Sym == null || n.Parts == null) return kept;
        if (Vocabulary.Vocab[n.Sym].Variants == null) return kept;

        // Le frère est TOUJOURS un squelette (à trous). Selon que le meilleur parse
        // est lui-même un squelette ou une forme COURTE complète, on ne cherche pas
        // la même chose :
        //  • INCOMPLET (le meilleur a des trous) : frère d'AUTRE arité (ADR 2026-06-12).
        //  • COMPLET (forme courte sans trous) : frère PLUS LONG, qui réinterprète la
        //    forme tapée en remplissant des slots supplémentaires (ADR 2026-06-22).
        bool incomplete = n.Parts.Any(c => c.Hole);
        (Node N, double Off) sib = default; double sibC = double.PositiveInfinity;
        foreach (var p in all)
        {
            var m = p.N;
            if (m.Type != "nary" || m.Sym != n.Sym || m.Parts == null) continue;
            if (!m.Parts.Any(c => c.Hole)) continue;
            if (incomplete ? m.Parts.Count == n.Parts.Count : m.Parts.Count <= n.Parts.Count) continue;
            double c = Score.Cost(p.N) + p.Off;
            if (c < sibC) { sibC = c; sib = p; }
        }
        if (sib.N == null) return kept;

        // INCOMPLET : forme LONGUE présélectionnée (sélection de gabarit, ADR 2026-06-12).
        // COMPLET : forme COURTE (ce qui est tapé, la moins chère) présélectionnée, la
        // longue en alternative — règle GÉNÉRIQUE, l'utilisateur tranche (ADR 2026-06-22).
        var pairNodes = new List<(Node N, double C)> { (bestP.N, bestC), (sib.N, sibC) };
        if (incomplete) pairNodes = pairNodes.OrderByDescending(x => x.N.Parts!.Count).ToList();
        var pair = pairNodes.Select(x => new EngineCandidate(LatexRenderer.Render(x.N, _culture), x.C)).ToList();
        var outk = new List<EngineCandidate>(pair);
        foreach (var k in kept)
            if (outk.All(o => o.Latex != k.Latex)) outk.Add(k);
        return outk.Take(MaxShow).ToList();
    }

    // callback de parsing d'un intérieur de parenthèse : MÊME pipeline (récursif).
    private List<Node> OnGroup(List<Token> interior)
    {
        var r = Assemble(interior);
        if (r.Note != null) _deepNote = true;
        return r.Parses;
    }

    private static List<Node> WindowOf(List<Node> list)
    {
        var pool = list.Where(p => !Score.CrossesCut(p)).ToList();
        var src = pool.Count > 0 ? pool : list;
        double best = double.PositiveInfinity;
        foreach (var p in src) best = System.Math.Min(best, Score.Cost(p));
        return src.Where(p => Score.Cost(p) < best + PopupGap).ToList();
    }

    private Asm Recombine(List<List<Node>> lists, List<Token> ops, string? note)
    {
        foreach (var c in lists) if (c.Count == 0) return new Asm(new List<Node>(), note);
        if (ops.Count >= 1) lists = lists.Select(WindowOf).ToList();
        if (ops.Count >= 1)
        {
            int br = Catalan(ops.Count);
            if (br * DistinctProd(lists) > CombineCap)
            {
                note = Note_;
                lists = lists.ToList();
                var order = Enumerable.Range(0, lists.Count).OrderByDescending(i => lists[i].Count).ToList();
                foreach (var i in order)
                {
                    if (br * DistinctProd(lists) <= CombineCap) break;
                    lists[i] = new List<Node> { Cheapest(lists[i]) };
                }
            }
        }
        var parses = Segment.Combine(lists, ops).Where(p => !Score.CrossesCut(p)).ToList();
        return new Asm(parses, note);
    }

    private Asm Assemble(List<Token> toks)
    {
        // 1) RELATIONS = coupe la plus forte : chaque MEMBRE assemblé à part puis recombiné.
        var rel = Segment.SplitRel(toks);
        if (rel.Ops.Count >= 1)
        {
            // Relation en TÊTE (membre gauche VIDE, ex. « =2x ») : pas une
            // expression — c'est le marqueur de la couche CHAÎNE multiligne,
            // qui vit HORS moteur. Sans cette garde, le membre vide devenait
            // un trou → « □ = 2x » (requalifié bug par l'auteur, 2026-06-10 ;
            // divergence assumée avec le JS figé, aucune fixture impactée).
            // Le membre DROIT vide (« a= » → a=□), lui, reste un aperçu de
            // saisie en cours légitime.
            if (rel.Segs[0].Count == 0) return new Asm(new List<Node>(), null);
            if (rel.Ops.Count >= Segment.MaxChain) return FoldSmart(toks);
            string? note = null;
            var lists = new List<List<Node>>();
            foreach (var s in rel.Segs) { var r = Assemble(s); if (r.Note != null) note = r.Note; lists.Add(r.Parses); }
            return Recombine(lists, rel.Ops, note);
        }

        // 2) N-AIRE en TÊTE : scope toute la suite → PAS de segmentation (forêt entière).
        if (toks.Count > 0 && toks[0].Kind == "nary" && Segment.ChainLen(toks) < Segment.MaxChain)
            return new Asm(Forest.Parse(toks, OnGroup, _culture).Where(p => !Score.CrossesCut(p)).ToList(), null);

        // 3) sinon : segmentation aux signes espacés + repli si trop long.
        var (segs, ops) = Segment.Split(toks);
        if (ops.Count >= Segment.MaxChain) return FoldSmart(toks);
        string? note3 = null;
        var lists3 = new List<List<Node>>();
        foreach (var seg in segs)
        {
            if (Segment.ChainLen(seg) < Segment.MaxChain) lists3.Add(Forest.Parse(seg, OnGroup, _culture));
            else { note3 = Note_; lists3.Add(FoldSmart(seg).Parses); }
        }
        return Recombine(lists3, ops, note3);
    }

    // distance de découpe : un terme de COÛT, pas un filtre — la lecture
    // « mot entier » reste un CHOIX de la popup (ADR 2026-06-10-Feat-split-
    // distance-cost-vec). > PopupGap pour qu'une découpe propre (sinx) reste
    // auto ; < widen+trou pour que le mot entier batte une découpe sale (avec).
    private const double SplitPenalty = 3;

    private AnalyzeResult Run(string src)
    {
        _deepNote = false;
        // NIVEAU 2 — découpe de run : TOUS les flux concourent, pénalisés de
        // leur distance au flux le plus découpé qui parse.
        var streams = new List<(List<Node> Parses, int Splits, string? Note)>();
        int maxS = 0;
        foreach (var (toks, splits) in Lexer.LexAll(src, _culture))
        {
            var r = Assemble(toks);
            if (r.Parses.Count == 0) continue;
            streams.Add((r.Parses, splits, r.Note));
            if (splits > maxS) maxS = splits;
        }
        var all = new List<(Node N, double Off)>();
        string? note = null;
        foreach (var (parses, splits, n) in streams)
        {
            foreach (var p in parses) all.Add((p, SplitPenalty * (maxS - splits)));
            if (splits == maxS && n != null) note = n;   // note du flux principal
        }
        return Finish(all, note ?? (_deepNote ? Note_ : null));
    }
}
