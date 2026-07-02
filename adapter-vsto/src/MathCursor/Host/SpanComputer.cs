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

using System;
using System.Collections.Generic;

namespace MathCursor.Host
{
    /// <summary>
    /// Calcul PUR (sans dépendance Word) de la span autour du caret pour le
    /// trigger manuel Ctrl+Espace : bornes = délimiteur de phrase / stopword /
    /// OMath / début-fin de ¶. Extrait de <see cref="ConversionController"/>
    /// pour être testable en unitaire (le contrôleur, lui, porte l'interop Word).
    /// <para>
    /// NB : ce chemin sert UNIQUEMENT au Ctrl+Espace explicite. L'auto-détection
    /// passe par le NER (zones), pas par ces délimiteurs. Cf. ADR
    /// 2026-06-18-Fix-input-autocorrect-fraction-factorial : <c>!</c> a été
    /// RETIRÉ du set (opérateur postfixe factoriel, pas une ponctuation — sinon
    /// Ctrl+Espace après <c>n!</c> donnait une span vide → pas de popup).
    /// </para>
    /// </summary>
    internal static class SpanComputer
    {
        // ── Données de span : stopwords + délimiteurs FR (table portée de
        //    l'ex data/locale/fr.yml, le YAML appartenait à l'ancien moteur).
        private static readonly HashSet<string> Stopwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "soit", "soient", "et", "ou", "donc", "alors", "avec", "si", "on",
            "car", "mais", "ainsi", "puis", "comme", "tout", "un", "une",
            "le", "la", "les", "des", "du", "de", "pour", "par", "sur",
            "dans", "au", "aux",
        };

        // '!' VOLONTAIREMENT ABSENT : c'est un postfixe (factorielle), pas une
        // fin de phrase. Cf. ADR 2026-06-18-Fix-input-autocorrect-fraction-factorial.
        private static readonly HashSet<char> SpanDelimiters = new HashSet<char>
        {
            '.', ';', '?', '=', '<', '>', '\n', '\r',
        };

        /// <summary>
        /// Début de la span : max(début ¶, après le dernier délimiteur hors
        /// brackets/parens AVANT le caret, fin du dernier OMath avant le caret,
        /// dernier stopword mot-entier). Logique reprise de
        /// l'ex-ManualTriggerController (comportement validé).
        /// </summary>
        public static int ComputeSpanStart(string text, int caret,
            IReadOnlyList<(int start, int end)> omathRegions)
        {
            int start = 0;

            // Groupe ( … ou [ … NON FERMÉ englobant le caret (matrice / tuple /
            // intervalle en cours de frappe) : la zone démarre à l'ouvrante, les
            // ; et , internes sont structurels (pas des fins de phrase). Cf. ADR
            // 2026-06-19-Fix-spancomputer-unclosed-bracket-matrix.
            int openBracket = EnclosingOpenBracket(text, caret);
            if (openBracket >= 0)
            {
                start = openBracket;
            }
            else
            {
                // Après le dernier délimiteur — walk backward, suivi profondeur.
                int bracketDepth = 0, parenDepth = 0;
                for (int k = caret - 1; k >= 0; k--)
                {
                    char c = text[k];
                    if (c == ']') { bracketDepth++; continue; }
                    if (c == '[') { if (bracketDepth > 0) bracketDepth--; continue; }
                    if (c == ')') { parenDepth++; continue; }
                    if (c == '(') { if (parenDepth > 0) parenDepth--; continue; }

                    if (!SpanDelimiters.Contains(c)) continue;
                    if ((c == ';' || c == ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
                    start = Math.Max(start, k + 1);
                    break;
                }
            }

            // Après la fin du dernier OMath qui se termine avant le caret.
            if (omathRegions != null)
                foreach (var (s, e) in omathRegions)
                    if (e <= caret) start = Math.Max(start, e);

            // Après le dernier stopword (mot entier).
            int i = caret - 1;
            while (i >= start)
            {
                while (i >= start && char.IsWhiteSpace(text[i])) i--;
                if (i < start) break;
                int wordEnd = i + 1;
                while (i >= start && IsWordChar(text[i])) i--;
                int wordStart = i + 1;
                if (wordEnd <= wordStart) { i--; continue; }
                string w = text.Substring(wordStart, wordEnd - wordStart);
                if (Stopwords.Contains(w)) { start = wordEnd; break; }
            }

            return start;
        }

        /// <summary>
        /// Fin de la span : min(fin ¶ [sans \r virtuel], premier délimiteur
        /// hors brackets/parens APRÈS le caret, début du premier OMath après
        /// le caret, premier stopword mot-entier après le caret). Miroir
        /// avant de <see cref="ComputeSpanStart"/> — un caret replacé au
        /// milieu d'une expression la capture en entier.
        /// </summary>
        public static int ComputeSpanEnd(string text, int caret,
            IReadOnlyList<(int start, int end)> omathRegions)
        {
            int end = text.Length;
            while (end > caret && (text[end - 1] == '\r' || text[end - 1] == '\n')) end--;

            // Premier délimiteur — walk forward avec suivi profondeur brackets/parens.
            // Init = ouvrantes NON fermées avant le caret (caret replacé au milieu
            // d'un groupe ( … / [ … en cours de frappe) → les ; , du groupe
            // englobant ne coupent pas. Cf. ADR 2026-06-19-Fix-spancomputer-
            // unclosed-bracket-matrix.
            OpenDepthBehind(text, caret, out int parenDepth, out int bracketDepth);
            for (int k = caret; k < end; k++)
            {
                char c = text[k];
                if (c == '[') { bracketDepth++; continue; }
                if (c == ']') { if (bracketDepth > 0) bracketDepth--; continue; }
                if (c == '(') { parenDepth++; continue; }
                if (c == ')') { if (parenDepth > 0) parenDepth--; continue; }

                if (!SpanDelimiters.Contains(c)) continue;
                if ((c == ';' || c == ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
                end = k;
                break;
            }

            // Début du premier OMath qui commence après le caret.
            if (omathRegions != null)
                foreach (var (s, _) in omathRegions)
                    if (s >= caret && s < end) end = s;

            // Premier stopword (mot entier) après le caret.
            int i = caret;
            while (i < end)
            {
                while (i < end && char.IsWhiteSpace(text[i])) i++;
                if (i >= end) break;
                int wordStart = i;
                while (i < end && IsWordChar(text[i])) i++;
                int wordEnd = i;
                if (wordEnd <= wordStart) { i++; continue; }
                string w = text.Substring(wordStart, wordEnd - wordStart);
                if (Stopwords.Contains(w)) { end = wordStart; break; }
            }

            return end;
        }

        /// <summary>
        /// Position de l'ouvrante <c>(</c> ou <c>[</c> NON fermée qui englobe le
        /// caret (groupe en cours de frappe), ou -1. On ne traverse pas un saut
        /// de ligne (un groupe ne s'étend pas sur plusieurs lignes). Le <c>.</c>
        /// n'arrête PAS le scan (sinon un séparateur décimal <c>(1,5 ;2,5</c>
        /// casserait la détection).
        /// </summary>
        private static int EnclosingOpenBracket(string text, int caret)
        {
            int depth = 0;
            for (int k = caret - 1; k >= 0; k--)
            {
                char c = text[k];
                if (c == '\n' || c == '\r') return -1;
                if (c == ')' || c == ']') { depth++; continue; }
                if (c == '(' || c == '[')
                {
                    if (depth > 0) { depth--; continue; }
                    return k; // ouvrante non appariée englobant le caret
                }
            }
            return -1;
        }

        /// <summary>
        /// Nombre d'ouvrantes <c>(</c> / <c>[</c> NON fermées avant le caret
        /// (remis à zéro à chaque saut de ligne). Sert à initialiser la marche
        /// avant de <see cref="ComputeSpanEnd"/> quand le caret est au milieu
        /// d'un groupe ouvert.
        /// </summary>
        private static void OpenDepthBehind(string text, int caret, out int parenOpen, out int bracketOpen)
        {
            parenOpen = 0;
            bracketOpen = 0;
            int n = Math.Min(caret, text.Length);
            for (int k = 0; k < n; k++)
            {
                char c = text[k];
                if (c == '\n' || c == '\r') { parenOpen = 0; bracketOpen = 0; continue; }
                if (c == '(') parenOpen++;
                else if (c == ')') { if (parenOpen > 0) parenOpen--; }
                else if (c == '[') bracketOpen++;
                else if (c == ']') { if (bracketOpen > 0) bracketOpen--; }
            }
        }

        private static bool IsWordChar(char c) => char.IsLetter(c) || c == '\'' || c == '-';
    }
}
