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

using System;
using System.Collections.Generic;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host.Detection
{
    /// <summary>
    /// Représentation unifiée de la zone source d'un commit OMath, de
    /// l'affichage popup jusqu'au commit final. Voyage du <c>ShowPopup</c>
    /// au <c>OnPopupCommitRequested</c> comme un objet unique.
    ///
    /// <para><b>Pourquoi</b> : avant 2026-05-23, l'état de la zone était
    /// dispersé sur 10 fields séparés (5 dans <c>SuggestionService</c>, 5
    /// dans <c>ManualTriggerController</c>), mélangeant deux référentiels :
    /// string-pos (offset dans <c>paragraph.Range.Text</c>) et internal Word
    /// (positions absolues avec wrappers cachés). Le path NER alimentait
    /// les fields string-pos, le path Ctrl+Espace ne les alimentait pas et
    /// propageait des coords mixtes <c>paragraphAbsStart + spanStart</c>
    /// correctes par coïncidence sur un ¶ vierge mais décalées de N
    /// wrappers structurels sur le 2ᵉ commit du même ¶ (bug « Soit f gt g »).
    /// Cf. ADR <c>2026-05-23-Refactor-zonespan-popup-commit-coords</c>.</para>
    ///
    /// <para><b>Contrat</b> : les coords portées sont <em>string-pos</em>
    /// (= index dans <see cref="ParagraphText"/>), sauf <see cref="ParagraphAbsStart"/>
    /// qui est en interne Word (= <c>paragraph.Range.Start</c>). Le seul
    /// point d'interop Word pour traduire vers les coords absolues complètes
    /// est <see cref="TryToInternal"/>, qui délègue à
    /// <see cref="ParagraphPositionTranslator.StringPosToInternal"/>.</para>
    /// </summary>
    internal sealed class ZoneSpan
    {
        /// <summary><c>paragraph.Range.Start</c> en interne Word.</summary>
        public int ParagraphAbsStart { get; }

        /// <summary>Offset string-pos du début de la zone dans <see cref="ParagraphText"/>.</summary>
        public int StringStart { get; }

        /// <summary>Offset string-pos exclusif de la fin de la zone dans <see cref="ParagraphText"/>.</summary>
        public int StringEnd { get; }

        /// <summary>Snapshot du texte du ¶ au moment de la construction. Sert
        /// (a) à l'iterative extend pour recalculer les bornes sans relire Word,
        /// (b) à la traduction string→interne via <see cref="TryToInternal"/>.</summary>
        public string ParagraphText { get; }

        /// <summary>Regions OMath du ¶ en string-pos. Utilisé par l'iterative
        /// extend pour ne pas étendre au-delà d'une OMath voisine.</summary>
        public IReadOnlyList<(int start, int end)> OMaths { get; }

        public ZoneSpan(
            int paragraphAbsStart,
            int stringStart,
            int stringEnd,
            string paragraphText,
            IReadOnlyList<(int start, int end)> omaths)
        {
            ParagraphAbsStart = paragraphAbsStart;
            StringStart = stringStart;
            StringEnd = stringEnd;
            ParagraphText = paragraphText ?? string.Empty;
            OMaths = omaths ?? Array.Empty<(int, int)>();
        }

        /// <summary>Texte de la zone (= sous-chaîne de <see cref="ParagraphText"/>
        /// entre <see cref="StringStart"/> et <see cref="StringEnd"/>).</summary>
        public string Text
        {
            get
            {
                if (string.IsNullOrEmpty(ParagraphText)) return string.Empty;
                int s = Math.Max(0, Math.Min(StringStart, ParagraphText.Length));
                int e = Math.Max(s, Math.Min(StringEnd, ParagraphText.Length));
                return ParagraphText.Substring(s, e - s);
            }
        }

        /// <summary>Texte envoyé au MOTEUR : <see cref="Text"/> + restitution de
        /// l'espace final trimé des bornes — signal d'étoile postfixe pour le
        /// lexer. Logique pure dans <see cref="ZoneEngineText"/> (testée sans Word).</summary>
        public string TextForEngine
            => ZoneEngineText.WithTrailingSpaceSignal(Text, ParagraphText, StringEnd);

        /// <summary><c>true</c> si la zone est vide ou inversée.</summary>
        public bool IsEmpty => StringEnd <= StringStart;

        /// <summary>
        /// Traduit les bornes string-pos en positions absolues internes Word
        /// via <see cref="ParagraphPositionTranslator.StringPosToInternal"/>.
        /// Itère <c>paragraph.Range.Characters</c> pour compter correctement
        /// les wrappers structurels invisibles (OMath markers, CC containers,
        /// surrogate pairs).
        ///
        /// <para>Renvoie <c>true</c> en cas de succès. <c>false</c> si le doc
        /// est null ou si le ¶ a disparu / a une frontière inattendue. Sur
        /// échec, <paramref name="absStart"/> et <paramref name="absEnd"/>
        /// retombent sur le mix <c>ParagraphAbsStart + String*</c> (= ancien
        /// comportement, raisonnable sur ¶ vierge d'OMath).</para>
        /// </summary>
        public bool TryToInternal(Word.Document doc, out int absStart, out int absEnd)
        {
            absStart = ParagraphAbsStart + StringStart;
            absEnd = ParagraphAbsStart + StringEnd;
            if (doc == null) return false;
            try
            {
                var paraRange = doc.Range(ParagraphAbsStart, ParagraphAbsStart)
                    .Paragraphs[1].Range;
                absStart = ParagraphPositionTranslator.StringPosToInternal(paraRange, StringStart);
                absEnd = ParagraphPositionTranslator.StringPosToInternal(paraRange, StringEnd);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString()
            => $"ZoneSpan paraStart={ParagraphAbsStart} string=[{StringStart},{StringEnd}) text=\"{Text}\"";
    }
}
