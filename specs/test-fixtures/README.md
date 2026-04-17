# Test Fixtures — Source de vérité cross-implémentations

Fichiers JSON lus par les tests unitaires de chaque implémentation du core.
Permettent de garantir la conformité si plusieurs cores existent un jour
(C# aujourd'hui, TS demain).

| Fichier | Contenu | Nombre de cas |
|---------|---------|---------------|
| `phase1-zone-detection.json` | Détection de zone math (prose → math boundary) | 47 cas FR/EN/DE/ES |
| `phase2-disambiguation.json` | *(à venir)* Désambiguïsation variable vs stopword | — |
| `phase3-operators.json` | *(à venir)* Parsing opérateurs nommés (lim, sum, int) | — |

## Format du JSON

```json
{
  "cases": [
    {
      "id": "fr-01",
      "lang": "fr",
      "input": "On a f(x) = 2x + 1",
      "expectedZone": "f(x) = 2x + 1",
      "description": "Phrase classique cours de maths FR"
    }
  ]
}
```

Le champ `expectedZone` est `null` si aucune zone math ne doit être détectée.

## Règle

**Modifier ces fichiers, c'est potentiellement casser l'implémentation.**
Ne jamais toucher à un cas existant sans documenter la raison. Ajouter
librement des nouveaux cas.
