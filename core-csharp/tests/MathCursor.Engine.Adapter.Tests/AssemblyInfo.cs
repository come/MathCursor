using Xunit;

// Désactive la parallélisation des classes de test de cet assembly.
//
// Raison : le moteur (RewriteEngine/Tokenizer) n'est PAS conçu pour un accès
// concurrent — `Tokenizer._multiCharCache` est un `static Dictionary` non
// thread-safe. Plusieurs classes de test construisant chacune un moteur (donc
// un vocab distinct) en parallèle déclenchent une race intermittente sur ce
// cache → exception catchée par EngineZoneSource → ResolvedZone null → flake.
//
// En production l'add-in VSTO est mono-thread (STA), donc le bug est inoffensif
// là-bas ; on l'évite ici en sérialisant les tests. Le fix thread-safe du cache
// est suivi séparément (cf. ADR 2026-05-30-Fix-partial-match-anchors, §Limite).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
