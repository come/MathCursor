using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Word = Microsoft.Office.Interop.Word;

namespace MathCursor.Host
{
    /// <summary>
    /// Prépare un rapport "signaler un souci" : zip contenant log tail,
    /// screenshot de la fenêtre Word, et contexte (versions + paragraphe courant).
    /// On dépose le zip en %Temp% et on met son chemin dans le presse-papier
    /// comme fichier droppable — l'utilisateur n'a plus qu'à Ctrl+V dans
    /// WhatsApp/email/Signal.
    ///
    /// Pas de réseau : le bundle est local, c'est l'utilisateur qui l'envoie
    /// (respect du principe "pas de télémétrie silencieuse").
    /// </summary>
    public static class FeedbackBundle
    {
        public const string ContactEmail = "come2percin@wanadev.fr";
        public const string WhatsAppGroupUrl = "https://chat.whatsapp.com/DewkjN8rwoAJeDtAd8yAth";

        private const int LogTailMaxBytes = 64 * 1024; // 64 KB de log suffisent

        /// <summary>
        /// Crée le zip et renvoie son chemin. Ne lève jamais : en cas d'échec
        /// d'un des éléments (screenshot, context...) on joint ce qu'on a pu
        /// et on continue. Renvoie null seulement si la création du zip
        /// elle-même échoue.
        /// </summary>
        public static string Create(Word.Application app)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string bundleName = $"mathcursor-rapport-{timestamp}.zip";
            string zipPath = Path.Combine(Path.GetTempPath(), bundleName);

            try
            {
                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    TryAddContext(zip, app);
                    TryAddLogTail(zip);
                    TryAddScreenshot(zip);
                }
                return zipPath;
            }
            catch
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                return null;
            }
        }

        /// <summary>
        /// Place le chemin du fichier dans le presse-papier comme "file drop",
        /// ce qui permet de le coller (Ctrl+V) directement dans WhatsApp Web,
        /// Outlook, Gmail, etc. au lieu de parcourir l'arborescence.
        /// </summary>
        public static void CopyToClipboardAsFile(string path)
        {
            try
            {
                var files = new StringCollection { path };
                Clipboard.SetFileDropList(files);
            }
            catch { /* certains contextes (RDP, session locked) refusent l'accès clipboard — non bloquant */ }
        }

        /// <summary>
        /// Ouvre l'Explorateur avec le fichier pré-sélectionné. Plan B si le
        /// clipboard ne marche pas : l'utilisateur drag-drop depuis l'Explorer.
        /// </summary>
        public static void RevealInExplorer(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            catch { }
        }

        private static void TryAddContext(ZipArchive zip, Word.Application app)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== MathCursor — Rapport de souci ===");
                sb.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
                sb.AppendLine();

                sb.AppendLine("-- Versions --");
                try { sb.AppendLine($"MathCursor : {Assembly.GetExecutingAssembly().GetName().Version}"); } catch { }
                try { sb.AppendLine($"Word       : {app?.Version ?? "?"}"); } catch { sb.AppendLine("Word       : ?"); }
                sb.AppendLine($"OS         : {Environment.OSVersion}");
                sb.AppendLine($".NET       : {Environment.Version}");
                sb.AppendLine();

                sb.AppendLine("-- Contexte --");
                try
                {
                    var sel = app?.Selection;
                    if (sel != null)
                    {
                        int caret = sel.Start;
                        var paraRange = sel.Paragraphs[1].Range;
                        int paraStart = paraRange.Start;
                        int paraEnd = paraRange.End;
                        string paraText = paraRange.Text ?? "";
                        sb.AppendLine($"Caret (doc offset) : {caret}");
                        sb.AppendLine($"Paragraphe [{paraStart},{paraEnd}] :");
                        sb.AppendLine(paraText);

                        // Compte les OMaths dans le paragraphe pour aider au diagnostic.
                        int omathCount = 0;
                        try
                        {
                            foreach (Word.OMath om in paraRange.OMaths)
                            {
                                omathCount++;
                                var r = om.Range;
                                sb.AppendLine($"  OMath [{r.Start},{r.End}] : {(r.Text ?? "").Replace("\r", " ").Replace("\n", " ")}");
                            }
                        }
                        catch { }
                        sb.AppendLine($"Nombre d'OMaths dans le paragraphe : {omathCount}");
                    }
                    else
                    {
                        sb.AppendLine("(pas de sélection active)");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"(erreur lecture sélection : {ex.Message})");
                }
                sb.AppendLine();

                sb.AppendLine("-- Note utilisateur --");
                sb.AppendLine("(Décris ici ce qui ne va pas — ce que tu essayais de faire,");
                sb.AppendLine(" ce qui s'est passé à la place. Sera envoyé avec le zip.)");

                WriteZipEntry(zip, "contexte.txt", sb.ToString());
            }
            catch { }
        }

        private static void TryAddLogTail(ZipArchive zip)
        {
            try
            {
                var tail = ReadLogTail();
                if (tail == null || tail.Length == 0) return;
                var entry = zip.CreateEntry("mathcursor.log", CompressionLevel.Optimal);
                using (var es = entry.Open())
                {
                    es.Write(tail, 0, tail.Length);
                }
            }
            catch { }
        }

        /// <summary>
        /// Lit la queue (LogTailMaxBytes derniers bytes) du log local. Public
        /// pour que la fenêtre "Signaler une erreur" puisse joindre le log au
        /// rapport HTTP sans passer par le zip. Renvoie null si pas de log ou
        /// erreur d'I/O (jamais throw).
        /// </summary>
        public static byte[] ReadLogTail()
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MathCursor", "logs", "mathcursor.log");
                if (!File.Exists(logPath)) return null;
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length <= LogTailMaxBytes)
                    {
                        var buf = new byte[fs.Length];
                        fs.Read(buf, 0, buf.Length);
                        return buf;
                    }
                    fs.Seek(-LogTailMaxBytes, SeekOrigin.End);
                    var tail = new byte[LogTailMaxBytes];
                    fs.Read(tail, 0, tail.Length);
                    return tail;
                }
            }
            catch { return null; }
        }

        private static void TryAddScreenshot(ZipArchive zip)
        {
            try
            {
                var png = CaptureScreenshotPng();
                if (png == null) return;
                var entry = zip.CreateEntry("screenshot.png", CompressionLevel.Optimal);
                using (var es = entry.Open())
                {
                    es.Write(png, 0, png.Length);
                }
            }
            catch { }
        }

        /// <summary>
        /// Capture la fenêtre Word courante en PNG (bytes). Public pour que la
        /// fenêtre "Signaler une erreur" puisse l'inclure base64 dans le
        /// rapport HTTP sans passer par le zip. Renvoie null si capture
        /// impossible (jamais throw).
        /// </summary>
        public static byte[] CaptureScreenshotPng()
        {
            try
            {
                // Word.Application.Hwnd n'est pas exposé par le PIA ; on passe par
                // le process hôte (on tourne DANS Word, MainWindowHandle pointe
                // sur la fenêtre principale de Word).
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd == IntPtr.Zero) return null;

                if (!GetWindowRect(hwnd, out RECT rc)) return null;
                int w = rc.Right - rc.Left;
                int h = rc.Bottom - rc.Top;
                if (w <= 0 || h <= 0) return null;

                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(rc.Left, rc.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }

        private static void WriteZipEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var es = entry.Open())
            using (var sw = new StreamWriter(es, new UTF8Encoding(false)))
            {
                sw.Write(content);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
