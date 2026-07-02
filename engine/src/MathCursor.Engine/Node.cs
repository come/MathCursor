// MathCursor — capture d'intention mathématique depuis une saisie clavier linéaire.
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

namespace MathCursor.Engine;

// ── Node : nœud de la FORÊT (AST). Portage des objets JS dynamiques de forest/.
// Types : "atom" | "infix" | "prefix" | "nary" | "postfix" | "matrix" | "interval"
//         | "list" | "set" | "tuple" | "paren".
// Tous les champs sont portés tels quels ; un Clone() shallow reproduit le spread
// JS `{...e, grouped:true}` (la liste Parts est partagée par référence, comme en JS).
internal sealed class Node
{
    public string Type = "";
    public string? Sym;
    public List<Node>? Parts;
    public List<List<Node>>? Rows;   // matrix : lignes de cellules

    public bool Spaced;
    public bool Implicit;
    public bool Grouped;
    public bool Hole;
    public bool Choice;              // infixe qui AVAIT la place de déborder à droite (>1 token)

    public string? Sig;
    public string? Coh;
    public int Ai;

    public string? Delim;            // matrix : clé de délimiteur ("(")
    public bool Alt;                 // matrix : orientation non-défaut
    public string? Lb;               // interval : crochet gauche
    public string? Rb;               // interval : crochet droit

    public Node Clone() => new Node
    {
        Type = Type,
        Sym = Sym,
        Parts = Parts,   // shallow (comme le spread JS)
        Rows = Rows,
        Spaced = Spaced,
        Implicit = Implicit,
        Grouped = Grouped,
        Hole = Hole,
        Choice = Choice,
        Sig = Sig,
        Coh = Coh,
        Ai = Ai,
        Delim = Delim,
        Alt = Alt,
        Lb = Lb,
        Rb = Rb,
    };

    // parts(n) de score.js : matrix → cellules aplaties ; sinon Parts ; sinon vide.
    private static readonly List<Node> Empty = new();
    public List<Node> EffectiveParts()
    {
        if (Parts != null) return Parts;
        if (Rows != null)
        {
            var flat = new List<Node>();
            foreach (var row in Rows) flat.AddRange(row);
            return flat;
        }
        return Empty;
    }
}

// ── Token : sortie du lexer. Portage des objets-tokens JS.
// Kind : "atom" | "infix" | "prefix" | "nary" | "postfix" | "lparen" | "rparen"
//        | "lbrace" | "rbrace" | "bracket" | "bar" | "sep".
internal sealed class Token
{
    public string Kind = "";
    public string? Sym;
    public List<string>? Syms;       // lectures multiples (x2 → ^ et _ ; ensemble R → [R, \mathbb{R}])
    public bool Spaced;
    public bool SpaceBefore;
    public bool Virtual;             // ) ou ] ajouté en fin de frappe
    public bool Num;                 // atome numérique
    public bool Sticky;              // infixe qui ne prend qu'un atome à droite
    public bool SignSup;             // sup issu d'un postSign (0⁺) : signe d'exposant, jamais un chapeau orphelin
    public bool Implicit;            // juxtaposition (rendu collé)
    public int Rank;                 // sep : rang (0 = "," , 2 = ";")
    public bool UnitWord;            // atome mot-unité
    public string? Coh;              // groupe de cohérence (atome ambigu)
}
