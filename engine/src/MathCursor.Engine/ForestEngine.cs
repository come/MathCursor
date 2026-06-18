using System.Collections.Generic;
using System.Linq;

namespace MathCursor.Engine;

/// <summary>Un candidat de lecture : LaTeX + coût (+ étiquette mot-clé optionnelle).</summary>
public sealed class EngineCandidate
{
    public string Latex { get; }
    public double Cost { get; }

    /// <summary>Mot-clé complet quand ce candidat vient de l'expansion d'un préfixe
    /// tapé (ex. « arcsin »), pour l'afficher sous le candidat dans la popup. Null
    /// pour les candidats ordinaires. Cf. ADR backlog moteur #2 (préfixes).</summary>
    public string? Hint { get; }

    public EngineCandidate(string latex, double cost, string? hint = null)
    { Latex = latex; Cost = cost; Hint = hint; }
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

    private AnalyzeResult Finish(List<(Node N, double Off, string? Hint)> all, string? note)
    {
        if (all.Count == 0) return new AnalyzeResult("erreur", new List<EngineCandidate>(), false);
        var seen = new HashSet<string>();
        var ranked = all
            .Select(p => new EngineCandidate(LatexRenderer.Render(p.N, _culture), Score.Cost(p.N) + p.Off, p.Hint))
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
    private List<EngineCandidate> PairSkeletons(List<(Node N, double Off, string? Hint)> all, List<EngineCandidate> kept)
    {
        var bestP = all[0]; double bestC = double.PositiveInfinity;
        foreach (var p in all)
        {
            double c = Score.Cost(p.N) + p.Off;
            if (c < bestC) { bestC = c; bestP = p; }
        }
        var n = bestP.N;
        if (n.Type != "nary" || n.Sym == null || n.Parts == null) return kept;
        if (!n.Parts.Any(c => c.Hole)) return kept;
        if (Vocabulary.Vocab[n.Sym].Variants == null) return kept;

        (Node N, double Off, string? Hint) sib = default; double sibC = double.PositiveInfinity;
        foreach (var p in all)
        {
            var m = p.N;
            if (m.Type != "nary" || m.Sym != n.Sym || m.Parts == null) continue;
            if (m.Parts.Count == n.Parts.Count || !m.Parts.Any(c => c.Hole)) continue;
            double c = Score.Cost(p.N) + p.Off;
            if (c < sibC) { sibC = c; sib = p; }
        }
        if (sib.N == null) return kept;

        var pair = new[] { (P: bestP, C: bestC), (P: sib, C: sibC) }
            .OrderByDescending(x => x.P.N.Parts!.Count)        // forme LONGUE d'abord
            .Select(x => new EngineCandidate(LatexRenderer.Render(x.P.N, _culture), x.C, x.P.Hint))
            .ToList();
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

    // Expansion de préfixes (backlog moteur #2) : on analyse l'entrée ORIGINALE
    // (lecture littérale, comportement historique inchangé) PLUS des variantes où
    // un mot préfixe-extensible est remplacé par chaque mot-clé candidat. Les
    // candidats de toutes les variantes concourent dans Finish (tri par coût) ;
    // ceux issus d'une expansion portent un Hint = le mot-clé complet. Approche
    // par substitution d'entrée (≠ forking lexer) → le littéral reste toujours
    // analysé tel quel, zéro régression sur l'existant. Cf. ADR backlog #2.
    private const int MaxPrefixSpots = 3;   // mots préfixe-extensibles considérés
    private const int MaxVariants = 12;     // garde-fou combinatoire (entrées analysées)

    private AnalyzeResult Run(string src)
    {
        var all = new List<(Node N, double Off, string? Hint)>();
        string? note = null;
        foreach (var (input, hint) in BuildInputVariants(src))
        {
            var (cands, n) = CollectFromInput(input);
            foreach (var (node, off) in cands) all.Add((node, off, hint));
            if (hint == null) note = n;     // note de l'entrée littérale d'origine
        }
        return Finish(all, note);
    }

    // Lex (multi-flux découpe) + assemble + pénalité de distance, pour UNE entrée.
    // = l'ancien Run, isolé pour être rejoué sur chaque variante.
    private (List<(Node N, double Off)> Cands, string? Note) CollectFromInput(string input)
    {
        _deepNote = false;
        var streams = new List<(List<Node> Parses, int Splits, string? Note)>();
        int maxS = 0;
        foreach (var (toks, splits) in Lexer.LexAll(input, _culture))
        {
            var r = Assemble(toks);
            if (r.Parses.Count == 0) continue;
            streams.Add((r.Parses, splits, r.Note));
            if (splits > maxS) maxS = splits;
        }
        var cands = new List<(Node, double)>();
        string? note = null;
        foreach (var (parses, splits, n) in streams)
        {
            foreach (var p in parses) cands.Add((p, SplitPenalty * (maxS - splits)));
            if (splits == maxS && n != null) note = n;
        }
        return (cands, note ?? (_deepNote ? Note_ : null));
    }

    private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    // Entrée littérale (toujours) + une variante par combinaison de substitutions
    // de mots préfixe-extensibles. Hint = mot-clé SI exactement un mot substitué.
    private List<(string Input, string? Hint)> BuildInputVariants(string src)
    {
        var spots = new List<(int Start, int Len, List<(string Form, string Canon)> M)>();
        int i = 0;
        while (i < src.Length && spots.Count < MaxPrefixSpots)
        {
            if (!IsLetter(src[i])) { i++; continue; }
            int st = i;
            while (i < src.Length && IsLetter(src[i])) i++;
            var pm = _culture.PrefixMatches(src.Substring(st, i - st));
            if (pm.Count > 0) spots.Add((st, i - st, pm));
        }

        var variants = new List<(string, string?)> { (src, null) };   // littéral d'abord
        if (spots.Count == 0) return variants;

        var optionCounts = spots.Select(s => s.M.Count + 1).ToArray(); // 0 = littéral
        long total = 1; foreach (var c in optionCounts) total *= c;
        for (long code = 1; code < total && variants.Count < MaxVariants; code++)
        {
            var choice = new int[spots.Count];
            long rem = code;
            for (int s = 0; s < spots.Count; s++) { choice[s] = (int)(rem % optionCounts[s]); rem /= optionCounts[s]; }

            string outp = src; string? onlyForm = null; int subs = 0;
            for (int s = spots.Count - 1; s >= 0; s--)   // droite→gauche : positions stables
            {
                if (choice[s] == 0) continue;
                string form = spots[s].M[choice[s] - 1].Form;
                outp = outp.Substring(0, spots[s].Start) + form + outp.Substring(spots[s].Start + spots[s].Len);
                subs++; onlyForm = form;
            }
            if (subs > 0) variants.Add((outp, subs == 1 ? onlyForm : null));
        }
        return variants;
    }
}
