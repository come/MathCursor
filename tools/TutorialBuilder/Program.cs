using System;
using System.IO;
using System.Linq;
using MathCursor.TutorialBuilder.Models;

namespace MathCursor.TutorialBuilder;

internal static class Program
{
    private static int Main(string[] args)
    {
        var inPath = ResolveDefaultSpecPath();
        var outPath = Path.Combine(AppContext.BaseDirectory, "MathCursor-Tutoriel.docx");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--in" when i + 1 < args.Length:
                    inPath = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "--help" or "-h":
                    PrintUsage();
                    return 0;
            }
        }

        if (!File.Exists(inPath))
        {
            Console.Error.WriteLine($"erreur : spec introuvable : {inPath}");
            PrintUsage();
            return 2;
        }

        var json = File.ReadAllText(inPath);
        var spec = TutorialSpecLoader.Load(json);

        // DocxRenderer (étape 3) — branchement à venir.
        // Pour l'instant : log de cohérence pour valider le pipeline.
        var totalItems = spec.Sections.Sum(s => s.Items.Count);
        Console.WriteLine($"loaded : {spec.Title} (v{spec.Version}, {spec.Lang})");
        Console.WriteLine($"        {spec.Sections.Count} sections, {totalItems} items");

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        DocxRenderer.Render(spec, outPath);
        Console.WriteLine($"écrit : {outPath}");
        return 0;
    }

    private static string ResolveDefaultSpecPath()
    {
        // Spec copiée à côté du binaire via <None CopyToOutputDirectory>.
        // V1 default = FR ; passe --in pour cibler EN ou autre.
        return Path.Combine(AppContext.BaseDirectory, "tutorial-spec.fr.json");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage : TutorialBuilder [--in <spec.json>] [--out <output.docx>]");
        Console.WriteLine("  --in   chemin vers tutorial-spec.json (défaut : à côté du binaire)");
        Console.WriteLine("  --out  chemin de sortie du .docx (défaut : à côté du binaire)");
    }
}
