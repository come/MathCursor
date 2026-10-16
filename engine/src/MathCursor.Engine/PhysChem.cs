// MathCursor: capturing mathematical intent from linear keyboard input.
// Copyright (C) 2026  Côme de Percin
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

// Mode physique-chimie (ADR 2026-10-16-Feat-physics-chemistry-mode) : tout ce que
// le domaine ajoute au moteur vit ici ou dans ChemFormula (grammaire des formules).
// Données : data/engine/physchem.json. Le lexer n'appelle que ces points d'entrée ;
// parser, score et rendu ignorent le domaine.
internal static class PhysChem
{
    private static readonly Dictionary<string, object?> Data = EngineData.Obj(EngineData.Load("physchem.json"));

    internal static readonly HashSet<string> Elements = new(EngineData.Arr(Data["elements"]).Select(EngineData.Str));
    internal static readonly List<string> States = EngineData.Arr(Data["states"]).Select(EngineData.Str).ToList();
    private static readonly Dictionary<string, string> Quantities =
        EngineData.Obj(Data["quantities"]).ToDictionary(kv => kv.Key, kv => EngineData.Str(kv.Value));
    private static readonly HashSet<string> SubscriptFirst = new(EngineData.Arr(Data["subscriptFirst"]).Select(EngineData.Str));
    private static readonly Dictionary<string, string> Particles =
        EngineData.Obj(Data["particles"]).ToDictionary(kv => kv.Key, kv => EngineData.Str(kv.Value));

    /// <summary>Grandeur usuelle indicée en lettres (Ec → E_{\mathrm{c}}), mot entier exact.</summary>
    public static bool TryQuantity(string word, out string latex) => Quantities.TryGetValue(word, out latex!);

    /// <summary>Particule en <paramref name="i"/> (e- → e⁻) : longueur consommée, 0 sinon.
    /// Bornée comme une charge : pas de lettre avant, fin/espace/fermant après.</summary>
    public static int ScanParticle(string src, int i, out string latex)
    {
        latex = "";
        char before = i > 0 ? src[i - 1] : '\0';
        if (char.IsLetter(before) || before == '\\') return 0;
        foreach (var kv in Particles)
            if (string.CompareOrdinal(src, i, kv.Key, 0, kv.Key.Length) == 0 && ChemFormula.ChargeEnd(src, i + kv.Key.Length))
            {
                latex = kv.Value;
                return kv.Key.Length;
            }
        return 0;
    }

    // lettre de base d'un atome, lectures multiples comprises (R → [R, ℝ])
    private static string? Base(Token t) => t.Sym ?? t.Syms?[0];

    /// <summary>
    /// Ordre indice/puissance des grandeurs numérotées, pour UNE saisie :
    /// v0, T1 (chiffres subscriptFirst) → indice d'abord ; v2 → puissance d'abord ;
    /// une lettre qui porte aussi un chiffre subscriptFirst ailleurs dans la saisie
    /// lit l'indice d'abord partout (cohérence : v2 - v1 → v₂ − v₁).
    /// </summary>
    internal sealed class ScriptOrder
    {
        private readonly HashSet<string> _indexed = new();

        public ScriptOrder(List<Token> toks)
        {
            for (int i = 0; i + 1 < toks.Count; i++)
                if (toks[i] is { Kind: "atom" } && Base(toks[i]) is { } b && toks[i + 1] is { Num: true, Sym: { } d }
                    && !toks[i + 1].SpaceBefore && SubscriptFirst.Contains(d))
                    _indexed.Add(b);
        }

        /// <summary>Lectures ordonnées (préférée d'abord) pour la juxtaposition
        /// base+chiffre en <paramref name="i"/>, ou null si elle reste une puissance
        /// seule (unité juste après un nombre : 5 m2 = 5 m²).</summary>
        public List<string>? For(List<Token> toks, int i, bool unitBase)
        {
            if (unitBase && i > 0 && toks[i - 1].Num) return null;
            string sub = Vocabulary.Role["sub"], sup = Vocabulary.Role["sup"];
            bool subFirst = SubscriptFirst.Contains(toks[i + 1].Sym ?? "") || (Base(toks[i]) is { } b && _indexed.Contains(b));
            return subFirst ? new List<string> { sub, sup } : new List<string> { sup, sub };
        }
    }
}
