using System;
using System.IO;
using System.Text;
using MathCursor.Detection;
using MathCursor.Host.Detection;

// Helper NER PERSISTANT piloté par l'extension VSCode. Charge le modèle ONNX une
// fois (~1,1 s) et le garde chaud ; répond aux requêtes de détection (~7 ms).
// Réutilise MathNerDetector + ZoneRefiner À L'IDENTIQUE de l'add-in Word.
//
// argv[0] = dossier du modèle (model_quantized.onnx + vocab.txt).
// stdin  : DETECT <caret> <text>          |  QUIT
// stdout : READY | FATAL | ZONE <s> <e> | NONE
internal static class Program
{
    private static MathNerDetector _det;

    private static int Main(string[] args)
    {
        // UTF-8 sur les pipes stdin/stdout : le texte source (accents : « limite »,
        // « racine », variables accentuées) doit arriver intact au tokenizer NER.
        var utf8 = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));

        string modelDir = args.Length > 0 ? args[0] : @"D:\Software\MathCursor\models\latest";

        try
        {
            _det = new MathNerDetector(modelDir);
            _det.WarmUpAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ner] load fail: " + ex.Message);
            Console.Out.WriteLine("FATAL");
            Console.Out.Flush();
            return 1;
        }

        Console.Out.WriteLine("READY");
        Console.Out.Flush();

        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            var parts = line.Split('\t');
            if (parts[0] == "QUIT") break;
            if (parts[0] == "DETECT" && parts.Length >= 3)
            {
                int caret = int.TryParse(parts[1], out int c) ? c : 0;
                Respond(parts[2], caret);
            }
        }
        _det.Dispose();
        return 0;
    }

    private static void Respond(string text, int caret)
    {
        string outLine = "NONE";
        try
        {
            var zones = _det.Detect(text);
            var pick = ZoneRefiner.PickNearestZone(zones, caret, out _);
            if (pick != null)
            {
                // Même raffinage que Word : fusion fragments, extension au caret,
                // récupération du mot-clé en tête (lim/racine/somme…).
                pick = ZoneRefiner.MergeWhitespaceAdjacent(zones, text, pick);
                pick = ZoneRefiner.TryExtendForwardWhitespace(text, pick, caret);
                pick = ZoneRefiner.ExtendBackwardWithKeyword(text, pick, ZoneRefiner.DefaultMathPrefixKeywords);
                outLine = "ZONE\t" + pick.Start + "\t" + pick.End;
            }
        }
        catch { outLine = "NONE"; }
        Console.Out.WriteLine(outLine);
        Console.Out.Flush();
    }
}
