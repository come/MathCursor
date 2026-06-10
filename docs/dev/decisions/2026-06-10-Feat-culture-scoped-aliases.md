# Feat — Alias du vocabulaire moteur rangés par culture + fix espace final trimé (« R*␣ »)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** forte (mécanisme) / molle (répartition FR-générique des alias, ajustable)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Feat-ribbon-columns-settings-culture.md](2026-06-10-Feat-ribbon-columns-settings-culture.md) (EngineCulture threadée en paramètre), [2026-05-23-Refactor-zonespan-popup-commit-coords.md](2026-05-23-Refactor-zonespan-popup-commit-coords.md) (ZoneSpan)

## Citation acté

> « ok on va commencer par un plan qui va permettre de ranger des alias par culture de maniere propre » — utilisateur, 2026-06-10
>
> « oui ! » (sur les maps déclaratives AliasGeneric / AliasFrOnly / AliasEnOnly et les fusions précalculées) — utilisateur, 2026-06-10, plan approuvé en plan mode.

## Contexte

Les ~33 alias du moteur (`somme`, `dans`, `racine`, `cup`, `U`…) sont des clés
supplémentaires **globales** de `Vocabulary.Vocab` (`Vocab["somme"] = Vocab["sum"]`),
mélangeant FR et génériques : un utilisateur US voit les alias FR et inversement.
On veut pouvoir ajouter des alias génériques ou par langue sans faire grossir le
moteur, en respectant deux règles dures existantes :

- `Vocabulary.cs` reste **le seul fichier qui connaît des opérateurs concrets** ;
- la culture est **threadée en paramètre** (`ForestEngine.Analyze(src, culture)`),
  jamais de statique mutable.

Constat vérifié : tous les lookups vocab hors lexer (Parser, LatexRenderer, Score,
Segment) portent sur des `Sym` canoniques déjà résolus — les alias sont une affaire
**purement lexicale**, on peut donc les résoudre dans le lexer sans toucher l'aval.

**Bug latent au passage** : `existe`/`ilexiste`/`nexiste` contiennent « xi » (ξ,
Splittable) ; ils ne survivent au découpage des runs de lettres que parce que leur
clé alias est dans `Vocab`. Le check « mot connu ? » du lexer doit canonicaliser.

**Régression « R*␣ » jointe à ce chantier** : `V x app R*␣` ne donne plus `∀x∈ℝ^*`
depuis le portage. Diagnostic vérifié en exécutant les deux moteurs : le C# est
correct et identique au JS de référence (`"V x app R* "` → `\forall x\in R^{\ast}`
dans les deux). Le coupable est l'adapter : `ConversionController.Trigger()` trime
les espaces de fin de span avant `Analyze`, or le lexer distingue `R*␣` (étoile
postfixe détachée → `R^{\ast}`) de `R*` en fin d'entrée (multiplication incomplète
→ `R\times\square`) — l'espace tapé EST le signal.

## Décision

### A. Alias = maps lexicales `mot → clé canonique`, portées par EngineCulture

- `Vocabulary.cs` déclare trois maps : `AliasGeneric`, `AliasFrOnly`, `AliasEnOnly`
  (vide au départ), plus deux fusions précalculées en statique readonly :
  `AliasesFr` (générique+FR), `AliasesUs` (générique+EN). Garde-fou à la fusion :
  `throw` si une cible n'est pas une clé canonique de `Vocab`.
- Les anciennes clés alias **sortent** du dictionnaire global `Vocab` (sinon elles
  resteraient actives dans toutes les cultures).
- Cas « V » (variante `forall` avec `WordSpace=true`, pas un alias pur) : devient
  une entrée interne `Vocab["·forallWord"]` (convention `·` existante) ; le mot
  « V » devient un alias générique normal vers cette clé.
- `EngineCulture` porte le set actif : `internal IReadOnlyDictionary<string,string>
  Aliases` + `internal string Canon(string w)`. Ctor → internal 5 params. Nouvelle
  méthode publique `WithOverrides(intervalSep, matrixEnv)` qui clone le preset en
  préservant tous les autres champs (l'adapter ne reconstruit plus champ par champ).
- Le lexer **canonicalise au lexing** : lookup sur `Canon(w)`, tokens porteurs de
  Sym canoniques (`in` au lieu de `dans`) → Parser/Score/Renderer/Segment intouchés.
  Exceptions : les replis « atome littéral » (infixe en position non binaire,
  WordSpace sans espace) poussent le mot ORIGINAL. Le check « mot connu ? » du
  run de lettres canonicalise aussi (protège `existe` du découpage en ξ).
- Répartition initiale (molle, ajustable en une ligne) : génériques =
  cup/cap/U/Union/Inter/V/exist/nexist ; FR = le reste des alias actuels.
  FR conserve 100 % des alias d'avant → les 280 fixtures restent vertes telles
  quelles.

### B. Fix « R*␣ » : ZoneSpan.TextForEngine

- Nouvelle propriété `ZoneSpan.TextForEngine` = `Text` + un espace si
  `ParagraphText[StringEnd]` est un whitespace. Les bornes restent trimées (le
  commit ne remplace pas l'espace, qui reste dans le document après l'équation ;
  l'ancre popup ne bouge pas) — seul le texte envoyé au moteur garde le signal
  « étoile détachée ». Couvre le trigger manuel, l'extension itérative et le path
  NER auto sans état supplémentaire.
- `ConversionController.AnalyzeAndShow` appelle `Analyze(zone.TextForEngine, …)`.

## Tradeoff & alternatives écartées

- **Muter `Vocabulary.Vocab` selon la culture (ajout/retrait de clés)** : statique
  mutable → fixtures parallèles non déterministes, interdit par l'ADR settings-culture.
- **Enum `AliasLanguage` résolu par le lexer auprès de Vocabulary** : couple le
  lexer à une notion de langue et interdit à une culture custom d'étendre ses
  alias ; le dictionnaire garde le lexer 100 % générique.
- **5ᵉ paramètre au ctor public d'EngineCulture** : l'adapter devrait penser à
  propager chaque nouveau champ à chaque évolution du preset ; `WithOverrides`
  préserve les champs futurs par construction.
- **Alias résolus en aval (Parser/Renderer)** : obligerait chaque consommateur à
  connaître les alias ; la résolution lexicale rend les Sym canoniques partout.
- **JSON externe pour les maps d'alias** : sur-ingénierie au stade actuel ; le
  vocabulaire est déjà du code déclaratif, les maps suivent le même régime.
- **Fix R*␣ en ne trimant plus `spanEnd`** : l'espace entrerait dans la zone
  remplacée au commit (l'espace tapé disparaîtrait du document) et décalerait
  l'ancre popup ; `TextForEngine` n'altère que l'entrée moteur.

## Conséquences

- **Code touché** : `engine/src/MathCursor.Engine/Vocabulary.cs` (section ALIAS),
  `EngineCulture.cs` (Aliases/Canon/WithOverrides, ctor internal),
  `Lexer.cs` (`Word()`, check known), `adapter-vsto/src/MathCursor/Host/Settings/AppSettings.cs`
  (`ToEngineCulture` → `WithOverrides`), `Host/Detection/ZoneSpan.cs`
  (`TextForEngine`), `Host/ConversionController.cs` (appel Analyze).
- **Tests** : 280 fixtures engine (défaut FR) inchangées et vertes ; ~8 nouveaux
  cas `CultureTests` (alias FR actif/inactif selon culture, générique partout,
  V/Vx, `ilexiste` vs ξ) ; test adapter `ZoneSpan.TextForEngine` ;
  `OmmlCoverageTests` inchangés.
- **API publique** : `EngineCulture` gagne `WithOverrides` (public) ; le ctor
  4 params devient internal — unique appelant repo migré (`AppSettings`).
  À vérifier avant merge : le wrapper WASM du site n'utilise que `Analyze`.
- **Comportement produit** : un utilisateur US ne voit plus les alias FR (but du
  chantier). En US, les mots FR contenant ξ (`existe`) peuvent se décomposer —
  conséquence assumée, verrouillée par test.
- **Tension connue, hors scope** : « dans » est à la fois alias moteur (→ `\in`)
  et stopword de span adapter — l'alias n'atteint le moteur que via l'extension
  itérative. Pré-existant, à trancher séparément.

## Validation post-fix

- `dotnet test` engine + serialization + adapter : tout vert, 280 fixtures sans
  modification.
- `CultureTests` exécute FR et US sur le même process (verrouille l'absence
  d'état global).
- Test manuel Word : taper `V x app R* ` puis Ctrl+Espace → la popup propose
  `\forall x\in\mathbb{R}^{\ast}` (et plus `…\times\square`).
