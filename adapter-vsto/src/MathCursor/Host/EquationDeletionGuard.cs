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
using MathCursor.Host.SourceMap;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Suppression ATOMIQUE des équations MathCursor au clavier — héritier
    /// minimal de l'ex-AnchorHygiene H1 (ADR hash-source-map : H2 orphelines
    /// et H3 caret piégé sont caducs par construction, plus de CC ni de
    /// caractère caché). Backspace juste après une de NOS OMaths (ou Suppr
    /// juste avant) → l'équation entière est SÉLECTIONNÉE comme une unité,
    /// la frappe suivante la supprime d'un coup — mimétisme inline-shape.
    /// Identification par la map (K1 cheap) : une équation éditée à la main
    /// n'est plus à nous → comportement Word natif (suppression char à char).
    /// </summary>
    internal sealed class EquationDeletionGuard
    {
        private readonly Word.Application _app;
        private readonly SourceMapResolver _resolver;
        private readonly Action<string> _log;

        public EquationDeletionGuard(Word.Application app, SourceMapResolver resolver, Action<string> log = null)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _log = log ?? (_ => { });
        }

        /// <summary>Backspace : caret collé derrière une de nos OMaths →
        /// sélectionne l'équation entière (consomme la frappe).</summary>
        public bool TrySelectEquationBeforeCaret()
        {
            try
            {
                var doc = _app.ActiveDocument; var sel = _app.Selection;
                if (doc == null || sel == null || sel.Start != sel.End) return false;
                var (om, source) = _resolver.ResolveBehindCaret(doc, sel);
                if (om == null || source == null) return false;
                sel.SetRange(om.Range.Start, om.Range.End);
                _log($"deletion-guard: équation sélectionnée [{om.Range.Start},{om.Range.End}) (backspace)");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Suppr : caret collé devant une de nos OMaths →
        /// sélectionne l'équation entière (consomme la frappe).</summary>
        public bool TrySelectEquationAfterCaret()
        {
            try
            {
                var doc = _app.ActiveDocument; var sel = _app.Selection;
                if (doc == null || sel == null || sel.Start != sel.End) return false;
                if (sel.StoryType != Word.WdStoryType.wdMainTextStory) return false;
                int caret = sel.Start;

                Word.OMath om = null;
                int probeEnd = Math.Min(doc.Content.End, caret + 2);
                foreach (Word.OMath o in doc.Range(caret, probeEnd).OMaths) { om = o; break; }
                if (om == null) return false;
                int omStart = om.Range.Start;
                if (omStart != caret && omStart != caret + 1) return false;
                if (!_resolver.IsOurs(doc, om)) return false;

                sel.SetRange(om.Range.Start, om.Range.End);
                _log($"deletion-guard: équation sélectionnée [{om.Range.Start},{om.Range.End}) (suppr)");
                return true;
            }
            catch { return false; }
        }
    }
}
