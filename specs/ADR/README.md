# ADR — Architecture Decision Records

Décisions architecturales numérotées, chacune dans son propre fichier.

| # | Titre | Statut |
|---|-------|--------|
| 001 | Core C# uniquement (pas de TS pour phase 1) | Accepté |
| 002 | Données multilingues JSON embarquées via `EmbeddedResource` | Accepté |
| 003 | Fixtures de test partagées dans `specs/test-fixtures/` | Accepté |
| 004 | Abstraction plateforme via 4 interfaces (IDocumentHost, etc.) | Accepté |
| 005 | Équations wrappées dans ContentControl + tag `MathCursor:{guid}` | Accepté |
| 006 | Trigger conversion explicite (pas d'auto-détection sur frappe) | Accepté |
| 007 | Protection anti-boucle undo via `Application.Undo` détectable (VSTO) | Accepté |
| 008 | Source des équations stocké dans `CustomXMLParts` | Accepté |

Format : chaque ADR est un court fichier Markdown avec Contexte / Décision / Conséquences.
