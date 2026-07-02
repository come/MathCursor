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
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Wrapper <see cref="IDisposable"/> autour de
    /// <c>Application.UndoRecord.StartCustomRecord / EndCustomRecord</c>
    /// (Office 2010+, disponible sur toutes les versions Word ciblées).
    ///
    /// <para>Pattern <c>using (var _ = new UndoRecordScope(_app, "name"))</c>
    /// : toutes les opérations Word visibles à l'intérieur du scope sont
    /// regroupées en un seul undo record nommé. Ctrl+Z annule alors le
    /// commit entier d'un coup au lieu d'une étape à la fois.</para>
    ///
    /// <para>Défensif :</para>
    /// <list type="bullet">
    /// <item>Si <c>StartCustomRecord</c> throw (Word vieux, state weird,
    /// ou <c>UndoRecord</c> indispo) → le scope devient no-op silencieux.
    /// Le code à l'intérieur tourne quand même, juste sans regroupement
    /// undo. Pas de dégradation fonctionnelle.</item>
    /// <item><c>Dispose()</c> appelle <c>EndCustomRecord()</c> dans un
    /// try/catch garantissant qu'aucun record half-open n'est laissé
    /// même si une exception se produit dans le scope.</item>
    /// </list>
    ///
    /// <para>Cf. ADR <c>2026-05-11-Fix-commit-grouped-in-single-undo-record</c>.</para>
    /// </summary>
    internal sealed class UndoRecordScope : IDisposable
    {
        private readonly Word.Application _app;
        private readonly bool _started;

        public UndoRecordScope(Word.Application app, string name)
        {
            _app = app;
            _started = false;
            if (_app == null || string.IsNullOrEmpty(name)) return;
            try
            {
                _app.UndoRecord.StartCustomRecord(name);
                _started = true;
            }
            catch (System.Exception ex)
            {
                // UndoRecord indispo ou state Word weird — no-op, MAIS loggé :
                // un Start silencieusement raté = commit non groupé = 3-4
                // Ctrl+Z (diagnostic fragmentation, 2026-06-10).
                _started = false;
                Log("START FAILED: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_app == null) return;
            if (!_started) { Log("END skipped (record jamais ouvert)"); return; }
            try
            {
                // IsRecordingCustomRecord AVANT End : si false alors qu'on a
                // ouvert le record, quelque chose l'a FERMÉ en cours de route
                // (suspect n°1 : InsertXML) → preuve de fragmentation.
                bool stillRecording = false;
                try { stillRecording = _app.UndoRecord.IsRecordingCustomRecord; } catch { }
                if (!stillRecording)
                    Log("record FERMÉ PRÉMATURÉMENT avant Dispose (fragmentation — suspect : InsertXML)");
                _app.UndoRecord.EndCustomRecord();
            }
            catch (System.Exception ex)
            {
                Log("END FAILED: " + ex.Message);
            }
        }

        /// <summary>
        /// Sonde de diagnostic : loggue si Word est encore en train
        /// d'enregistrer le custom record à un point nommé du commit.
        /// Permet de localiser l'opération exacte qui ferme le record
        /// (InsertXML ? CC.Add ? …). No-op silencieux hors record / erreur.
        /// </summary>
        public static void Probe(Word.Application app, string label)
        {
            try
            {
                bool recording = app?.UndoRecord?.IsRecordingCustomRecord ?? false;
                Log($"probe [{label}] recording={recording}");
            }
            catch (System.Exception ex)
            {
                Log($"probe [{label}] error: {ex.Message}");
            }
        }

        private static void Log(string message)
        {
            try
            {
                var dir = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "mathcursor.log"),
                    $"{System.DateTime.UtcNow:o} undo-record {message}{System.Environment.NewLine}");
            }
            catch { }
        }
    }
}
