# Fix — Commit groupé dans un seul `UndoRecord` Word

**Date :** 2026-05-11
**Kind :** Fix
**Température :** forte
**Statut :** acté
**Supersedes :** —
**Lié à :** ADR `2026-05-06-Meta-l4-pipeline-and-session` (commit pipeline)

## Citation acté

> "j'ai un soucis sur le controle Z quand ca se met à merdouiller on
> perd des infos.. comment on peut gerer l'ajout d'info dans le systeme
> d'histoque de word de maniere propre ?" — utilisateur, 2026-05-11
>
> "oui ok, mais rappelle toi, ca doit etre propre"

## Décision

Chaque commit MathCursor (= chaque exécution complète du
`CommitPipeline.Run`) est encapsulé dans un seul **`Application.UndoRecord`
custom** Word, nommé d'après l'opération utilisateur (ex. *« Convertir
formule »*).

Implémentation :

1. **Nouveau helper `UndoRecordScope`** (`adapter-vsto/src/MathCursor/Host/`)
   — struct/class `IDisposable` qui wrap `StartCustomRecord(name)` et
   `EndCustomRecord()`. Try/catch défensif :
   - Si l'API throw (Word vieux, state weird, ou `UndoRecord` indispo) →
     le scope devient no-op silencieux, sans propager l'exception.
   - `Dispose()` appelle `EndCustomRecord()` même en cas d'exception
     pendant le scope, pour ne jamais laisser un record half-open.

2. **Intégration ponctuelle** dans `SuggestionService.CommitLatexAndOMathCore`
   autour du `_commitPipeline.Run(ctx)` :
   ```csharp
   using (var _ = new UndoRecordScope(_app, "Convertir formule"))
   {
       try { ctx = _commitPipeline.Run(ctx); }
       catch (Exception ex) { LogDiag("commit_pipeline_error: " + ex.Message); }
   }
   ```
   1 ligne ajoutée + un `using`. Toutes les opérations Word visibles
   (insertion OMath, splice XML, ajout/déplacement de bookmark, layout
   ¶ vide post-display, caret) sont à l'intérieur du pipeline → couvertes
   en un seul record.

3. **Localisation du scope (1 point unique)** : `CommitLatexAndOMathCore`
   est le point d'entrée du commit utilisateur (déclenché par Ctrl+Espace
   ou validation popup). Les autres flows qui touchent au doc (revert,
   edit mode, list-mode insertion) ne passent pas par ce chemin et
   restent inchangés pour cet ADR — un ADR ultérieur les couvrira si
   nécessaire (cf. *Suivi*).

## Pourquoi

- **Symptôme observé (user 2026-05-11)** : Ctrl+Z après un commit
  MathCursor « merdouille » et fait perdre des infos. Cause : chaque
  appel API Word (insert OMath, ajout bookmark, modif paragraphe,
  ajustement caret) crée son propre undo record côté Word. Le commit
  utilisateur (1 action perçue) = N undo records distincts → Ctrl+Z
  annule une étape à la fois → l'utilisateur se retrouve dans un état
  partiel incohérent (OMath disparue mais bookmark resté, ou inverse).

- **`Application.UndoRecord` API Office** (Office 2010+, disponible
  Word 2016+ que MathCursor cible) permet de **regrouper** N opérations
  en un seul record nommé. Ctrl+Z annule alors tout le commit d'un
  coup, état cohérent.

- **Bénéfice UX secondaire** : l'utilisateur voit dans le menu Word
  *« Annuler : Convertir formule »* (au lieu du générique *« Annuler »*),
  ce qui humanise l'historique et aide à comprendre ce qui se passe.

## Pas dans cette ADR

- **`CustomXMLPart` sidecar de résolutions** : modifs au sidecar
  (stockage source brute + SpanPins JSON) ne sont **PAS** annulées par
  Word natif — Word ne tracke pas l'historique des `CustomXMLPart`.
  Donc même avec `UndoRecord`, le sidecar peut diverger du contenu doc
  post-undo. Fix séparé (snapshot/restore) → ADR ultérieur.

- **Revert mode + Edit mode** : flows alternatifs qui ne passent pas
  par `CommitLatexAndOMathCore`. À couvrir si bug équivalent observé.

- **List-mode marker injection** : opération distincte du commit
  pipeline, ses propres N appels Word. Pareil.

## Risques

- **API `UndoRecord` indisponible** sur certaines versions / states de
  Word. Mitigation : try/catch dans `UndoRecordScope` → no-op si KO,
  le pipeline tourne quand même normalement (juste pas de regroupement
  undo). Aucune dégradation fonctionnelle.

- **Performance** : `StartCustomRecord`/`EndCustomRecord` sont des
  appels API très légers (juste un push/pop sur la pile interne Word).
  Aucun impact mesurable attendu.

- **Tests xUnit impossibles** : `Word.Application.UndoRecord` est un
  type COM, non mockable sans réécrire une abstraction `IUndoSink` +
  wrapper. Pour ~20 LoC, le coût d'abstraction > le bénéfice de test.
  Validation manuelle en Word réel.

## Suivi

Si après ce fix d'autres pertes d'infos sont observées au Ctrl+Z,
créer un ADR séparé pour :
- Snapshot/restore du sidecar JSON synchronisé avec l'undo Word
  (détection via comparaison nombre d'OMath actuels vs. cache).
- Application du même pattern (`UndoRecordScope`) aux flows Revert /
  Edit / List-mode si ils créent aussi de multiples records.
