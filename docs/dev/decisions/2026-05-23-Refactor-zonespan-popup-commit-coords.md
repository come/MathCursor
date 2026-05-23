# Refactor — `ZoneSpan` unifié pour le passage popup → commit

**Date :** 2026-05-23
**Kind :** Refactor
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :**
- Bug observé « Soit f gt g » (= 2e commit Ctrl+Espace sur le même ¶ qui contient déjà une OMath ↦ ¶ corrompu).
- ADR [2026-05-22-Feat-engine-poc-isolation](2026-05-22-Feat-engine-poc-isolation.md) (= engine v2 trigger les span courts 1-char qui ont révélé la latence).
- Commit `3f277bb` « Fix F=1=1 — traduire string-pos NER → Word interne au commit » (= 1ère moitié du contrat de traduction, partielle).

## Citation acté

> « redige et go phase 1 » — utilisateur, 2026-05-23

(Décision contextuellement adossée à : « fix profond, pas un if », et « je suis dans une phase globale big bang donc je veux faire les choses proprement c'est le plus important. et les vieux trucs qu'on a fait en mode empilage de if ou correctifs, je veux les virer si ils sont over compliqués »).

## Contexte

Le pipeline du commit d'OMath transporte les bornes du span dans **deux référentiels de coordonnées différents** sans contrat explicite :

- **string-pos** = index dans `paragraph.Range.Text` (= text plat sans wrappers).
- **internal Word** = position absolue dans `Document.Range` (= avec wrappers structurels invisibles : OMath markers, CC containers, surrogate pairs).

Le path **NER auto-detect** (`SuggestionService.cs:1124-1126`) alimente trois fields parallèles `_lastZoneStringStart/End/_lastZoneParaRangeStart` avant `ShowPopup`. `TranslateNerToInternal` (`SuggestionService.cs:1630`) lit ces fields au moment du commit, itère `paragraph.Range.Characters` via `ParagraphPositionTranslator.StringPosToInternal`, et produit les coords internes correctes pour `SetRange`.

Le path **manual trigger** (Ctrl+Espace, `ManualTriggerController.cs:152-153`) **n'alimente PAS** ces fields. Il fait simplement `absStart = paragraphAbsStart + spanStart` — ce qui mélange interne et string. `TranslateNerToInternal` voit `_lastZoneStringStart < 0`, no-op, et propage le mix au pipeline. Symptômes :

- **¶ vierge d'OMath** : aucun wrapper structurel dans le ¶ ↦ string-pos == internal-pos par coïncidence ↦ pas de bug observable.
- **¶ avec OMath déjà présente** : N wrappers cachés entre paragraphAbsStart et la position visible du span ↦ `absStart` décalé de −N ↦ pointe DANS le wrapper de l'OMath précédente. Sur span de 1 lettre (cas où engine v2 trigger maintenant), la range entière tombe dans la zone protégée. `Range.Delete()` plante (« Impossible d'éditer la plage »), ZoneCleaner avale l'erreur, l'inserter continue, doc final corrompu : `"Soit f gt g"` au lieu de `"Soit f g"`.

Le bug est **latent depuis l'introduction du anchor-CC pattern + intra-merge revival** (ADR 2026-05-18 + 2026-05-19). Il devient visible aujourd'hui parce que `MathCursor.Engine` v2 accepte les span 1-char qui n'étaient pas convertis par `LatticeEngine` legacy.

La cause est structurelle : 10 fields séparés répartis sur 2 classes véhiculent l'état d'une même zone, avec un contrat implicite que chaque caller doit alimenter. Toute nouvelle entrée dans le pipeline (= nouvelle source de popup) doit deviner qu'elle doit alimenter ces fields ou casser silencieusement. C'est l'inverse de la règle MC « les coords Word sont source de bug → un seul chemin de traduction ».

## Décision

Introduire un type `ZoneSpan` immuable qui **encapsule toute l'info nécessaire au commit** et voyage du show au commit comme un objet unique :

```csharp
internal sealed class ZoneSpan
{
    int ParagraphAbsStart;                          // interne Word
    int StringStart;                                // string-pos
    int StringEnd;                                  // string-pos
    string ParagraphText;                           // snapshot
    IReadOnlyList<(int start, int end)> OMaths;     // string-pos

    bool TryToInternal(Word.Document, out int absStart, out int absEnd);
    string Text { get; }    // = ParagraphText.Substring(StringStart, StringEnd-StringStart)
}
```

`TryToInternal` est le **seul point d'interop Word** pour traduire string→interne. Il délègue à `ParagraphPositionTranslator.StringPosToInternal` qui itère `paragraph.Range.Characters` (gère correctement OMath wrappers + surrogate pairs).

### Conséquences sur l'état interne

**Supprimés dans `SuggestionService`** :
- `_lastZoneAbsStart`, `_lastZoneAbsEnd` (= coords mixtes, source du bug)
- `_lastZoneStringStart`, `_lastZoneStringEnd`
- `_lastZoneParaRangeStart`
- `_lastZoneSource`

**Remplacés par** : `private ZoneSpan? _currentZoneSpan;`

**Supprimés dans `ManualTriggerController`** :
- `_iterativeSpanStart`, `_iterativeSpanEnd`
- `_iterativeParagraph`
- `_iterativeParaAbsStart`
- `_iterativeOMaths`

**Remplacés par** : `private ZoneSpan? _iterativeSpan;`

### Conséquences sur les signatures

- `ShowPopup(ResolvedZone, int absStart, int absEnd, int rawLen, string dbg)` ↦ `ShowPopup(ResolvedZone, ZoneSpan, int rawLen, string dbg)`
- `Action<ResolvedZone, int, int, int, string> showPopupAndEnterNavMode` ↦ `Action<ResolvedZone, ZoneSpan, int, string>` (-1 arg)
- `InitFromAutoZone(string, int, int, int, IReadOnlyList<…>)` ↦ `InitFromAutoZone(ZoneSpan)` (-3 args)
- `Action<string> setLastZoneSource` ↦ **supprimé** (la source vit dans `ZoneSpan.Text`)

### Conséquences sur les fonctions

- `TranslateNerToInternal` (l.1630-1652) : **supprimée**. Sa raison d'être (`isEditMode || _lastZoneStringStart < 0`) disparaît avec ZoneSpan toujours présent.
- Le commentaire `SuggestionService.cs:90-94` qui décrivait le hack des coords mixtes : **supprimé**.

### Politique de traduction

- Au **show** : on traduit une fois pour positionner la popup et alimenter `_lastZoneAbsStart/End` (= dérivés, pour anti-spam + edit-mode-entry-check). Échec gracieux possible (= popup ne s'affiche pas).
- Au **commit** : on RE-traduit (`ZoneSpan.TryToInternal(doc)`) au cas où le doc a bougé entre show et commit. Échec ↦ abort propre du commit, doc intact.

## Tradeoff & alternatives écartées

- **Patch tactique : faire que `ManualTriggerController.Trigger()` alimente les 3 fields string-pos via une callback dédiée**. Rejetée — c'est exactement le « empilage d'if » que la phase big bang vise à virer. Le contrat reste implicite (fields séparés à synchroniser à chaque nouvelle source de popup), classe de bug intacte pour la prochaine source qu'on ajoutera.

- **Faire la traduction au tout début du `Trigger()` et passer des coords internes propres en aval**. Rejetée — ça transforme `paragraphAbsStart + spanStart` en `paragraphAbsStart + StringPosToInternal(paraRange, spanStart) - paraRange.Start` à chaque caller. La traduction n'est plus centralisée, deux sources potentielles de divergence (mode polling NER vs manuel).

- **Refactor encore plus profond : type `ZoneSpan` partagé entre core et adapter**. Rejetée — `StringPosToInternal` est COM-bound (Word.Range.Characters), pas exportable en L1. ZoneSpan reste L3 (adapter).

## Conséquences

- **Code touché** :
  - `adapter-vsto/src/MathCursor/Host/Detection/ZoneSpan.cs` (nouveau, ~80 lignes)
  - `adapter-vsto/src/MathCursor/Host/SuggestionService.cs` (suppression de 10 fields + 1 fonction + commentaires hack ; modification de ~15 callsites)
  - `adapter-vsto/src/MathCursor/Host/ManualTrigger/ManualTriggerController.cs` (suppression de 5 fields ; modification de Trigger + ExtendOneStop + InitFromAutoZone + ctor sig)

- **Tests** :
  - xUnit core : non impacté (string-only logic).
  - xUnit adapter : pas de tests sur `ManualTriggerController.Trigger()` ni sur `TranslateNerToInternal` (= zone testée manuellement uniquement, scope « ergo VSTO Word »). Build vert requis.
  - Test manuel Word (procédure définie ci-dessous) : validation principale.

- **API publique** : aucune. Tout est `private` / `internal`.

- **Règles MC impactées** :
  - Aligne sur la règle dure « positions Word normalisées via `sel.SetRange(p,p)` + readback systématiquement » (cf. [feedback_word_api_workflow](../briefs/architecture-flow.md)) en éliminant le mix interne+string qui était sa principale violation latente.

## Validation post-fix

Scénarios à exécuter manuellement dans Word avec l'add-in installé :

1. **Bug primaire** « Soit f gt g » :
   - Doc vierge, taper `Soit f` ↦ Ctrl+Espace ↦ valider ↦ vérifier `Soit *f*` propre.
   - Taper ` et g` à la suite ↦ Ctrl+Espace ↦ valider ↦ vérifier `Soit *f* et *g*` propre (l'ex-bug produisait `Soit f gt g`).
2. **NER auto-detect** : taper `x=1` ↦ popup auto ↦ Enter ↦ vérifier `*x=1*` propre.
3. **Iterative extend** : taper `Soit f sigma` ↦ Ctrl+Espace (1er stop) ↦ Ctrl+Espace (2e stop) ↦ valider ↦ vérifier extension propre.
4. **Edit mode** : cliquer dans une OMath existante ↦ Esc ↦ vérifier doc intact.
5. **Cross-merge align** : `= a` ↦ Enter ↦ `= b` ↦ Ctrl+Espace ↦ vérifier align*.
6. **Cases** : `{ x=1` ↦ Ctrl+Espace ↦ vérifier cases multi-ligne.
7. **Liste numérotée** : taper math dans une bullet list ↦ vérifier inline + bullet préservé.

Critère de succès : 7/7 scénarios passent identiquement à la version pré-refactor (sauf S1 qui produisait le bug).

## Plan en cours — état d'avancement

Phase 1 (ce refactor) — couvre la chaîne popup → commit.

Phase 2 (séparée, après stabilisation Phase 1, ADR dédiée à venir) :
- Diagnostic root cause du bug « ff » 1er commit (paragraphe vierge, `Range.Delete()` qui plante sur du plain trivial — sticky-zone ? autocorrect ? selection state ?).
- Cleanup du silent-fail `ZoneCleaner.cs:236` (`plain_delete_error` avalé) + des 9 catches `(absStart, absEnd, null)` dans `InsertOMathAt` qui ne signalent pas l'abort proprement (= laissent le pipeline penser « succès 0-char »).
- Décision sur les TODO niveau 4 (ZoneCleaner.cs:65 auto-grow CC, InsertOMathAt:2299-2312 Font.Hidden post-cc.Add pour liste).
