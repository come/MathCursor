# Brief — Mode liste cases `{` Phase 2 (multi-ligne + list-mode visible)

**Date :** 2026-05-05
**Statut :** rédigé, en attente de validation pour ADR

## Contexte

Phase 1 (markers align : `<=>`, `=>`, `<=`, `=`) est validée + list-mode
visible (auto-injection du marker en texte plain post cross-merge) est
validée pour align.

Le marker `{` (système d'équations) était dans le scope du brief
[`2026-04-30-multiline-systems-equivalences.md`](2026-04-30-multiline-systems-equivalences.md)
mais hors V1 (Phase 1). Reste à activer Phase 2 : cross-merge cases +
list-mode visible cases.

**État actuel du codebase** (cf. survey 2026-05-05) :
- AST `MultiLineBlock(Mode="cases", ...)` **existe** (`AstNodes.cs:238`)
- Renderer LaTeX `\begin{cases}...\end{cases}` **existe** (`LatexRenderer.cs:237`)
- Convertion `\begin{cases}` → OOXML `{█(...)┤` **existe** (`LatexToUnicodeMath.cs:48`)
- Parser `TryParseMultiLineBlock` **détecte uniquement align** (`Parser.cs:208`),
  commentaire « peut-être un bloc cases en V2 » → à étendre
- Adapter `AlignMarkers` + cascade `TryCascadeAbsorbMarkerChain` ne
  connaissent pas `{` → cascade cases manquante
- Tests parser/renderer cases : **0** aujourd'hui

## Comportement utilisateur attendu

### Scénario A — création d'un système 2 lignes

```
[Ligne 1] { x+y=5         ← user tape, Ctrl+Espace
           → OMath single-line \begin{cases} x+y=5 \end{cases}
           → list-mode actif, ¶ d'après contient "{ "
[Ligne 2] { 2x-y=1         ← user tape derrière le "{ " auto-injecté
           ⏎
           → cross-merge cases → \begin{cases} x+y=5 \\ 2x-y=1 \end{cases}
           → list-mode actif, nouveau "{ " injecté
[Ligne 3] ⏎  (Enter sur "{ " seul)
           → strip marker, ¶ vide, list-mode désactivé
```

### Scénario B — extension d'un système existant

```
[¶ existant] OMath \begin{cases} x=1 \\ y=2 \end{cases}
[¶ vide]
[¶ user]     { z=3       ← user tape sur ¶ vide
             ⏎
             → cross-merge → \begin{cases} x=1 \\ y=2 \\ z=3 \end{cases}
             → list-mode actif, "{ " injecté en dessous
```

### Scénario C — sortie du mode

```
[¶ OMath cases multi-ligne]
[¶ "{ "] caret après l'espace
   ⏎ direct, sans rien taper
   → strip "{ ", ¶ devient vide, list-mode désactivé
```

### Scénario D — pas de mix cases ↔ align

```
[¶ N]   { x+y=5         ← cases, OK
[¶ N+1] <=> z=0         ← marker align !
        ⏎
        → la cascade cases s'arrête à ¶ N+1 (pas de mix)
        → ¶ N+1 reste tel quel (ou cascade align dans son sens à elle)
```

Cf. brief 30-04 §3.4 « Mix de modes cases vs align ».

## Détection : règle de marker `{`

**Convention** : ligne (après TrimStart) commence par `{` **suivi d'un
espace** → c'est un marker cases. Sans espace, ce n'est pas un système :
- `{ x = 1` → cases (espace après `{`)
- `{1, 2}` → ensemble en extension, **pas** un système
- `{}` → ensemble vide, **pas** un système
- `{x=1` (sans espace) → ambigu, **pas** un système (l'user qui veut
  taper un système met naturellement l'espace après `{`)

**Question ouverte** : la règle "espace obligatoire après `{`" est-elle
suffisante ? Alternative : exiger aussi qu'un signe relationnel
(`=`, `<`, `>`) soit présent dans la ligne (= équation valide). Pour
V2 minimal, on s'en tient à l'espace.

## Architecture

### 1. Détection — adapter

Dans `SuggestionService.cs` :

```csharp
// Markers align (Phase 1, déjà existant)
private static readonly string[] AlignMarkers = { "<==>", "<=>", "==>", "=>", "<==", "<=", "=" };

// Marker cases (Phase 2)
private const string CasesMarker = "{";

private static bool StartsWithCasesMarker(string s)
{
    if (string.IsNullOrEmpty(s)) return false;
    string trimmed = s.TrimStart();
    // Convention : { suivi d'un espace = marker cases.
    // {1,2} ou {x=1 (sans espace) = pas un système.
    return trimmed.Length >= 2 && trimmed[0] == '{' && trimmed[1] == ' ';
}
```

### 2. Cascade cross-merge cases

Nouvelle fonction `TryCascadeAbsorbCasesChain(doc, absStart, absEnd, currentSource)` :
- Précondition : `StartsWithCasesMarker(currentSource) == true`
- Cascade montante :
  - Si ¶ précédent termine par OMath cases existant → ABSORB (top de cascade)
  - Si ¶ précédent texte commence par `{ ` → ABSORB, continue
  - Sinon → STOP (pas de mix, pas de barrière de marker align)
- Retour : `MergedSource = "ligne1\nligne2\n..."` + range absolu

`TryFindCrossMergeAbove` devient un dispatch :
```csharp
private MergeResult TryFindCrossMergeAbove(...)
{
    // Mode 2 (revert) reste prioritaire
    var mode2 = TryAbsorbRevertedMultiLineZone(...);
    if (mode2 != null) return mode2;

    // Dispatch selon le marker du current source
    if (StartsWithCasesMarker(source))
        return TryCascadeAbsorbCasesChain(...);
    return TryCascadeAbsorbMarkerChain(...);  // align (existant)
}
```

### 3. Parser core étendu

Dans `Parser.cs:TryParseMultiLineBlock` :
- Si TOUTES les lignes commencent par `{ ` → `new MultiLineBlock("cases", lines, linePrefix=[])`
- Sinon, si TOUTES commencent par marker align → `new MultiLineBlock("align", ...)` (existant)
- Sinon (mix) → fallback parse ligne-par-ligne, joint avec `\\` ou échec

Le `LinePrefix` pour cases est une liste de chaînes vides — le `{` initial
n'est pas un préfixe LaTeX puisque `\begin{cases}` ouvre déjà l'accolade.
Le parser doit STRIPPER le `{` + espace de chaque ligne avant de parser
le contenu équation.

### 4. List-mode visible cases

`ListModeStateMachine.KnownMarkers` : ajouter `"{"` :
```csharp
private static readonly string[] KnownMarkers =
{
    "<==>", "<=>", "==>", "=>", "<==", "<=", "=",
    "⇔", "⇒", "⇐", "↔", "⟺", "⟹", "⟸",
    "{",  // NEW Phase 2 cases
    "=",
};
```

`StartsWithKnownMarker(trimmed)` : **règle stricte pour `{`** — ce marker
ne match QUE si suivi d'un espace. Sinon `{1,2}` ou `{x=1` (que l'user
peut taper après un Backspace partiel sur le `{ ` injecté) matcherait à
tort, et le list-mode tenterait un ValidateAsIs sur un set ou une
expression qui n'est pas un système.

```csharp
internal static bool StartsWithKnownMarker(string trimmed)
{
    // ...
    foreach (var marker in KnownMarkers)
    {
        if (!trimmed.StartsWith(marker, StringComparison.Ordinal)) continue;
        // `=` solo : exiger qu'il ne soit pas suivi d'un autre `=`.
        if (marker == "=" && trimmed.Length > 1 && trimmed[1] == '=') continue;
        // `{` : exiger espace après (sinon {1,2} ou {x=1 matcherait à tort).
        if (marker == "{" && (trimmed.Length < 2 || trimmed[1] != ' ')) continue;
        return true;
    }
    return false;
}
```

`SuggestionService.ExtractMarkerFromMergedSource` : doit aussi retourner
`{` quand le merged source est un cases.

`SuggestionService.InjectListModeMarker` : aucun changement, l'helper
`ListModeMarkerInjector.Plan` est déjà générique sur le marker —
`Plan("{", ...)` produit `"{ "` ou `"{ \r"` selon `hostParaIsOursAndEmpty`.

#### Activation single-line cases (validé user 2026-05-05)

Le list-mode cases s'active **dès la première conversion `{ x+y=5` solo**
(pas seulement après un cross-merge multi-ligne). Sémantique : un
système ne nécessite pas 2 lignes pour qu'on commence à proposer
l'extension — l'auto-injection `{ ` sur la ligne 2 indique à l'user
qu'il est dans un système et peut continuer.

Détection : sur succès d'un commit non-cross-merge, regarder si le
LaTeX émis commence par `\begin{cases}`. Si oui, activer list-mode
avec marker `{`.

```csharp
// Dans CommitLatexAndOMathCore, après le bloc cross-merge :
// Activation list-mode cases sur single-line conversion (Phase 2).
if (insertionSucceeded && !wasCrossParagraphMerge && IsSingleLineCases(latex))
{
    _listMode.OnCrossMergeSucceeded("{");
    InjectListModeMarker("{", finalizedAnchorIsOursAndEmpty);
}

private static bool IsSingleLineCases(string latex)
    => latex != null && latex.TrimStart().StartsWith(@"\begin{cases}");
```

#### Sortie via Backspace (validé user)

L'user qui ne veut pas continuer le système voit `{ ` en texte plain
sur la ligne d'après, fait Backspace pour effacer, et tape ce qu'il
veut. **C'est la voie de sortie principale**.

Le cas « Enter sur `{ ` seul → ExitListMode » reste un fallback (déjà
implémenté générique dans la state machine via la règle `trimmed ==
ActiveMarker.Trim()`), pour cohérence avec align.

**Cas du Backspace partiel** : l'user efface l'espace mais pas le `{`,
ligne devient `{` ou `{x=1`. Grâce à la règle stricte « `{` + espace »
dans `StartsWithKnownMarker`, l'Enter ne sera pas consommé comme
ValidateAsIs — il tombera dans `PrefixWithActiveMarker` (= exit
silencieux) ou laissera le pipeline normal d'évaluer la zone.

## Edge cases

1. **Set en extension `{1, 2, 3}`** : pas un système (pas d'espace après
   `{` dans l'usage normal de set). Si user tape `{ 1, 2, 3 }` (avec
   espace) → la règle dit "système" — c'est faux mais à la marge. V2 :
   on accepte cette imprécision, le parser cases échouera à parser
   `1, 2, 3` comme équation et le rendu sera bizarre. À documenter.

2. **`{` solo (ligne ne contient que `{` ou `{ ` puis Enter)** : ligne
   avec rien après le marker. La state machine doit détecter ça comme
   `ExitListMode` (déjà supporté pour les autres markers grâce à la
   logique « trimmed == ActiveMarker.Trim() »).

3. **`{` au milieu d'une ligne** : pas un marker, c'est un délimiteur
   de set / d'arg de fonction. Ne déclenche rien. Test couvert par
   `StartsWithCasesMarker` qui exige `TrimStart`.

4. **Mix cases ↔ align** : la cascade cases s'arrête sur align et
   réciproquement (Branche D du brief 30-04). Implémentation : la
   cascade cases ne reconnaît QUE `{ ` ; la cascade align ne reconnaît
   QUE les markers align. Pas de cross-mode.

5. **OMath cases existant comme sommet de cascade** : si ¶ précédent
   contient un OMath dont la source est `{ x=1` (= déjà rendu en cases),
   on l'absorbe comme sommet (idem `TryCascadeAbsorbMarkerChain` pour
   align). Réutiliser `FindOwnedOMathAtEndOfParagraph`.

## Tests-first (avant implémentation)

### Core (`MathCursor.Core.Tests`)

1. **Parser** : `TryParseMultiLineBlock` détecte cases sur source
   `"{ x=1\n{ y=2"` → `MultiLineBlock(mode="cases", lines=[Eq(x,1), Eq(y,2)])`
2. **Parser** : refuse cases sur `"{1, 2}\n{ x=1"` (1re ligne pas un cases)
3. **Renderer** : déjà testé partiellement, ajouter test `MultiLine_cases_renders_begin_cases`
4. **Renderer** : test mix → ne doit pas mixer cases dans align (déjà testé ?)

### Adapter (`MathCursor.Tests`)

5. **`StartsWithCasesMarker`** : tests purs sur la regex/heuristique :
   - `"{ x=1"` → true
   - `"{x=1"` → false (pas d'espace)
   - `"{1, 2}"` → false
   - `"{}"` → false
   - `"  { x=1"` (leading whitespace) → true
6. **Helper `ListModeStateMachine`** : ajouter test
   `Active({) + ligne "{ x=1" → ValidateAsIs` (= cross-merge fera son boulot)
7. **Helper `ListModeStateMachine`** : test
   `Active({) + ligne "{ " seul → ExitListMode`
8. **Helper `ListModeMarkerInjector`** : test `Plan("{", ...)` → `"{ "` ou `"{ \r"`
   (probablement déjà couvert par le `Theory` markers existant, ajouter `{`)

### Cascade cross-merge (à extraire en pure si possible)

9. Difficile à tester sans Word. Stratégie : extraire la logique de cascade
   en helper testable comme on a fait pour `RevertedZoneMerger`. Helper
   prendrait `(IList<string> paraTexts, string currentSource)` et
   retournerait `(int chainStartIndex, string mergedSource, string mode)`
   où mode ∈ {"align", "cases", null}.

## Travail (ordre proposé)

1. **Tests-first** : écrire tests core (parser cases) + adapter
   (StartsWithCasesMarker, state machine, injector) — tous rouges
2. **Parser core** : étendre `TryParseMultiLineBlock` pour détecter cases
3. **Adapter détection** : `StartsWithCasesMarker` + dispatch dans
   `TryFindCrossMergeAbove`
4. **Adapter cascade** : `TryCascadeAbsorbCasesChain` (extraire en helper
   pur si possible, comme `RevertedZoneMerger`)
5. **List-mode** : ajouter `{` dans `KnownMarkers` + extension
   `ExtractMarkerFromMergedSource` + (si validé) activation single-line
   cases
6. **Test manuel dans Word** : scénarios A, B, C, D du brief

## Points validés par l'user (2026-05-05)

- [x] Convention `{` SUIVI d'espace pour distinguer cases vs set
- [x] Pas de mix align/cases (chaque cascade reconnaît son marker exclusivement)
- [x] **List-mode s'active dès single-line cases** : tape `{ x+y=5` →
  Ctrl+Espace → conversion en single-line cases → `{ ` auto-injecté sur
  ligne 2 pour indiquer à l'user qu'il est dans le système
  > Citation user : « pour moi { ligne1 => on active l'auto sur la ligne 2
  > on rajoute { pour indiquer à l'utilisateur qu'il est toujours dans le
  > systeme »
- [x] **Sortie principale = Backspace** : l'user voit `{ `, fait Backspace
  s'il veut sortir, ligne devient vide ou contenu normal
  > Citation user : « si il veut en sortir il fait backspace et il recree
  > une ligne »
  Le cas "Enter sur `{ ` seul → ExitListMode" reste un fallback cohérent
  avec align mais n'est plus la voie principale.
- [x] **Règle stricte `{` + espace dans la state machine** : pour que le
  Backspace partiel (effacer juste l'espace, garder le `{`) ne déclenche
  pas un faux ValidateAsIs sur `{1,2}` — le matching de `{` dans
  `KnownMarkers` doit exiger l'espace, comme `StartsWithCasesMarker`.
- [ ] **OUVERT (à valider)** : extraire la cascade en helper pur testable
  (comme `RevertedZoneMerger`), oui/non ? Recommandation par défaut : oui,
  cohérent avec le pattern établi.

## Liens

- Brief original : [`2026-04-30-multiline-systems-equivalences.md`](2026-04-30-multiline-systems-equivalences.md)
- ADR list-mode visible : [`../decisions/2026-05-05-Feat-multiline-list-mode-visible.md`](../decisions/2026-05-05-Feat-multiline-list-mode-visible.md)
- ADR cross-merge pipeline : [`../decisions/2026-05-04-Meta-cross-merge-pipeline-refactor.md`](../decisions/2026-05-04-Meta-cross-merge-pipeline-refactor.md)
- ADR cascade multi-ligne : [`../decisions/2026-05-04-Feat-multiline-edit-cascade-merge.md`](../decisions/2026-05-04-Feat-multiline-edit-cascade-merge.md)
