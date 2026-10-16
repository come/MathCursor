# Feat — Mode physique-chimie activable par document

**Date :** 2026-10-16
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Supersedes :** —
**Lié à :** rapport BO 2026-10-16 (`Fe2+ + H_2O`, v0.11.7) ; commit 4ed2461 (double exposant) ; [2026-10-14-Feat-scientific-notation-linear-minus](2026-10-14-Feat-scientific-notation-linear-minus.md) (alternative portée par un atome `Syms`) ; documents du testeur (`T=T2-T1`, `x0=2` lus en puissances)

## Citation acté

> « je m'interroge à faire une "grosse" modif ce sera d'avoir un jeu de data optionnel pour la physique Chimie eventuellement activable ou desactivable par option.. […] en gros Fe2+ + H2O ca serait cool que ca marche (dans un contexte de chimie) avec evidemment une bonne exaustivité sur les molecules / atomes ! […] desactivable / activable pour eviter d'allourdir quand on est dans un contexte de math » — utilisateur, 2026-10-16
>
> Plan chimie : « yes ». Choix : portée « Par document », hôtes « Word + démo web », ambiguïté chiffre/charge « Toujours la popup ». Ruban : « je verrais bien plutot toute une section "domaines" ».
>
> « ok integre la physique dans la chimie (du coup le mode devient physiques chimie) avec les trucs usuels qui passent dans les choix du coup. v0 => v_0 ET v^0 etc. ? » puis, sur la contre-proposition (0/1 → indice d'abord, 2+ → puissance d'abord, cohérence, liste de grandeurs) : « parfait ! »

## Contexte

Le moteur lit tout en maths, ce qui est juste en maths mais faux en physique-chimie :
- chimie : `H2O` → H²O, `CO2` → CO², `Fe2+` → (Fe²)⁺ (avant le commit 4ed2461 : `Fe^{2}^{+}`, LaTeX invalide, le `+` disparaissait dans Word) ;
- physique : `T=T2-T1` → T²−T¹, `x0=2` → x⁰=2, `Ec` → E·c.

Un chiffre collé est une puissance en maths (`x2` = x², règle du tuto), un indice en chimie, et l'un ou l'autre en physique (`v0` = v₀, mais `mv2` = mv²).

## Décision

**Un mode « Physique-chimie » optionnel, désactivé par défaut, activé par document.**

### Données — `data/engine/physchem.json`
- `elements` : les 118 symboles ; `states` : `aq s l g`.
- `particles` : `e-` → e⁻, `e+` → e⁺, bornées comme une charge (lues aussi en fin de saisie : `2Fe + 2e-`, retour testeur 2026-10-16 ; `e-x` reste l'exponentielle).
- `quantities` : grandeurs usuelles indicées en lettres, mot entier → LaTeX (`Ec` → `E_{\mathrm{c}}`, `Ep`, `Epp`, `Em`, `Req`, `Ceq`, `Vm`, `vx`/`vy`/`vz`, `Fx`/`Fy`/`Fz`). S'enrichit sans code.

### Moteur (C# + Rust, parité fixtures)
- L'option voyage dans la culture (`EngineCulture.PhysChem` / `Culture.physchem`, cloné par `WithPhysChem`), seul paramètre déjà transmis partout. Mode désactivé = moteur identique.
- **Formules chimiques** (`ChemFormula` / `chem.rs`) : une formule bornée devient **un atome** portant ses lectures (`Syms`), comme l'alternative `10^{x}`. Parser, Score et Renderer n'en savent rien.
  - Exhaustivité par **grammaire**, pas par liste : `formule := groupe+ charge? état?`, `groupe := Élément indice? | '(' groupe+ ')' indice?`.
  - Une formule peut commencer par un groupe parenthésé **s'il porte un indice** (`(NH4)2SO4`) ; `(BC)`, `(AB) // (CD)` restent de la géométrie, `(BC)2` garde BC² en alternative.
  - Rendu : éléments en romain (`\mathrm{H}_{2}\mathrm{O}`, `\mathrm{Fe}^{2+}`, `\mathrm{Ca}(\mathrm{OH})_{2}`, `\mathrm{Na}^{+}(\mathrm{aq})`).
  - Chiffres juste avant un signe (`Fe2+`, `NO3-`, `VO2+`) : **deux lectures à égalité** (charge / indice), l'élève tranche. `Fe^2+` (charge explicite) : une seule lecture. `H_2O` toléré.
  - Lecture maths gardée en alternative seulement quand la formule y ressemble : lettres-éléments seules sans chiffre (`BC`, `UI` → maths d'abord), un seul élément d'une lettre indicé (`V2`, `O2` → chimie d'abord).
  - Un élément seul reste une variable s'il fait une lettre (`C`, `H`), ou garde sa lecture s'il est déjà un mot du moteur : unité (`2 Pa`), fonction (`Re(z)`).
- **Grandeurs numérotées** (règle de juxtaposition lettre+chiffre et unité+chiffre) : les deux lectures, indice et puissance, sont proposées.
  - `0` ou `1` → **indice d'abord** (`v0`, `x0`, `T1`) : une puissance 0 ou 1 n'a pas de sens.
  - `2` et plus → **puissance d'abord** (`mv2`, `gt2`, `f(x2)`), indice en alternative.
  - **Cohérence par lettre** : si la lettre porte aussi 0/1 dans la saisie, toutes ses occurrences lisent l'indice d'abord (`T2 - T1` → T₂ − T₁, `v2 - v1`, `R1 + R2`, `m1 + m2`) ; les autres lettres gardent la puissance (`gt2` à côté de `v0`).
  - Une unité juste après un nombre reste une puissance (`5 m2`, `20 cm2`).
  - La préférence est portée par le **score** (`Token.Ranked` → `Node.Secondary`, +1,5, sous le seuil de popup de 2), pas par l'ordre de proposition : elle tient quel que soit l'emboîtement (`f(x2)`, `cos(x2)`). Conséquence assumée : une lecture qui cumule deux choix secondaires (T²−T¹) sort de la popup.

### Word
- Bouton bascule « Physique-chimie » dans un groupe de ruban **« Domaines »** (qui accueillera d'autres matières si pertinent), mémorisé dans le document (`Document.Variables["MathCursor.PhysChem"]`), lu à chaque appel du moteur, resynchronisé au changement de document.

### Démo web
- Bouton « Physique-chimie » à côté de FR/US (démo et page d'essai en direct).

## Critère : quand une règle devient un domaine

> « et du coup par exemple (et sauf erreur de ma part) le 3*10-5 qu'on a fait l'autre jour c'est tres "physique" comme domaine non ? » puis « yes » — utilisateur, 2026-10-16

Une règle passe derrière un interrupteur de domaine **seulement si sa lecture contredit la lecture maths sur des saisies courantes**.

- **Domaine** : la physique-chimie. `H2O`, `v0`, `Ec` : la lecture maths et la lecture physique-chimie s'opposent sur la même saisie.
- **Toujours actif** : notation scientifique `3*10-5` (motif étroit, popup jamais auto, aussi au programme de maths), unités `5 m/s`, `20µs`. Rien ne contredit les maths. Les cacher derrière un bouton les ferait perdre à qui n'a pas pensé à l'activer.
- **Pistes non ouvertes** (pas de signal utilisateur) : SVT/génétique (`F1`, génotypes `A//a`), NSI (code à ne pas convertir).

## Tradeoff & alternatives écartées

- **Physique-chimie toujours active, avec alternatives** : pollue les maths (`BC`, `V2`, `x2` deviendraient ambigus partout).
- **Réglage global** : à basculer à la main en changeant de cours ; le document de maths ouvert à côté serait touché.
- **Deux domaines séparés Physique / Chimie** : les profs enseignent les deux ensemble, les documents mélangent formules et grandeurs.
- **Liste de molécules** : jamais exhaustive ; la grammaire couvre tout.
- **Règle de priorité charge/indice** : fausse sur des ions réels (`VO2+` existe dans les deux lectures) ; l'utilisateur préfère la popup.
- **Indice d'abord pour tous les chiffres** : casse `mv2`, `gt2`, `πR2`, les formules les plus courantes.
- **Règle générique « majuscule + minuscule = indice »** (`Ec` → E_c) : entre en conflit avec les éléments (`Fe` = fer) ; une liste en données est sûre.
- **Préférence par l'ordre des lectures seul** (0,5 de pénalité) : instable sous une fonction, `f(x2)` passait en x₂, à cause d'une asymétrie existante (`(x_2)` garde ses parenthèses comme nœud, `(x^2)` non : inversion +1).
- **Corriger cette asymétrie dans le score maths** (script atomique sous une application = pas une inversion) : mesuré, 692 sorties maths changées (dont `f_n(x)`), hors périmètre.
- **Grammaire 100 % en données** (règles de lexique déclaratives + interpréteur générique C# et Rust) : beaucoup plus de code pour un seul domaine, moins lisible que ~150 lignes explicites. Les tables sont en données, la grammaire est du code moteur pur à parité C#/Rust.

## Conséquences

- **Code moteur** : `data/engine/physchem.json` (dont `subscriptFirst` : chiffres « indice d'abord ») ; `EngineCulture.cs` / `culture.rs` ; **`PhysChem.cs` / `physchem.rs`** (module du domaine : données, grandeurs, politique d'ordre indice/puissance `ScriptOrder`) ; `ChemFormula.cs` / `chem.rs` (grammaire des formules seule) ; `Lexer.cs` / `lexer.rs` (trois points d'appel au module, aucune règle du domaine en dur) ; `Token.Ranked`, `Node.Secondary`, `Score.SECONDARY` (C# + Rust) ; `Parser.cs` / `parser.rs`.
- **Code hôtes** : `DocumentMode.cs`, `Ribbon.xml` (groupe Domaines), `RibbonCallback.cs`, `Strings.cs`, `ThisAddIn.cs` (resynchro au changement de document), `ConversionController.cs` (2 appels moteur) ; `Bridge.cs`, `demo.js`, `live.js`, `index.html`, `live.html`, `demo.css`.
- **Tests** : champ `"physchem": true` dans `fixtures.json` (59 fixtures, total 599), rejoué par `FixtureTests`, `FixtureToleranceTests`, `OmmlCoverageTests`, `conformance.rs` ; audits popup WPF et walker OMML couvrent aussi les candidats du mode ; le tuto ignore ces fixtures.
- **Protocole** : culture `fr+physchem` / `us+physchem` acceptée par `analyze` (Rust) et `Bridge.Analyze` (démo) ; inconnu → fr.
- **StarMath (LibreOffice)** : les atomes physique-chimie portent du LaTeX (`\mathrm{…}`) recopié tel quel dans la sortie StarMath ; à traduire quand le mode sera branché sur LibreOffice.
- **Hors lot** : détection auto (le modèle NER n'a vu que des maths → Ctrl+Espace ; corpus + réentraînement ensuite), équilibre ⇌ (`<=>` est réservé aux chaînes), hydrates `·5H2O`, isotopes, `v02` (vitesse initiale au carré), VSCode / LibreOffice (protocole `analyze` à brancher).

## Validation post-fix

- Corpus de 16 625 entrées :
  - mode désactivé : sorties **identiques octet pour octet** ;
  - mode activé : la 1re proposition ne change que sur **6** entrées, toutes par la règle 0/1 → indice ; ailleurs, seule l'alternative s'ajoute.
- Fixtures 599/599 (C# + Rust), gate `scripts/run-tests.ps1` vert.
- Moteur du dernier commit vs moteur actuel, mode maths, 17 358 entrées (corpus + fixtures + 788 entrées physique-chimie), cultures fr **et** us : **0 écart** (décision, candidats, coûts).
- Parité C#/Rust hors fixtures, mêmes 17 358 entrées, mode maths **et** physique-chimie : **0 écart**.
- Outil permanent `tools/engine-diff/engine_diff.py` (17 689 entrées, référence git + parité C#/Rust, modes fr, us, fr+physchem, us+physchem), à relancer avant chaque évolution du moteur (cf. `engine/README.md`).
- Word : activer « Physique-chimie », `Fe2+ + H2O` et `T=T2-T1` + Ctrl+Espace → Fe²⁺ + H₂O, T = T₂ − T₁ ; réglage conservé après fermeture ; autre document non touché.
