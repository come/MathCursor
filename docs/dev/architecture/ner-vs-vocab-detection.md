# NER vs détection heuristique : le NER est-il nécessaire ?

**Statut : réflexion ouverte (pas de décision actée).** Mesuré le 2026-06-15 sur
de la vraie donnée. À reprendre si on veut alléger l'installeur (le NER = 129 Mo +
ONNX natif x86/x64 + VC redists + le cold-start de ~1-2 s).

## La question

Le NER (`MathNerDetector`, modèle ONNX `distilmult-v6`) ne sert QU'À la détection
**passive** de la zone math pendant la frappe (`AutoDetectController`). Le
déclencheur manuel `Ctrl+Espace` (`ConversionController.Trigger`) utilise déjà une
heuristique pure (`ComputeSpanStart` : délimiteurs + stopwords + frontières d'OMath
+ début de ¶), **sans NER**. Et le NER est déjà optionnel au runtime (modèle absent
→ auto-détection inerte, Ctrl+Espace marche).

Hypothèse à challenger : « une boucle O(n) sur le vocabulaire suffit à dire si on est
dans des maths, le NER est superflu ».

## Distinction clé

Détecter ≠ reconnaître. Le moteur (`ForestEngine.Analyze`) **reconnaît** déjà (est-ce
parsable en maths + quel LaTeX). Le NER **localise** (où, dans de la prose qui coule,
commence et finit la formule). Une boucle vocab donne un sac de hits, pas des
frontières.

## L'expérience (banc jetable, non commité)

Donnée réelle : 3 .docx de cours/évaluation de maths (lycée FR). Les formules y sont
des **équations Word** (OMML) ; leur **prose** (1002 ¶) = jeu de PRÉCISION (le
détecteur ne doit pas y déclencher). Les **427 fixtures** du corpus = jeu de RAPPEL
(maths connues).

### Test 1 — le moteur seul comme détecteur (pas de porte)

Fenêtre glissante 1→5 mots sur chaque ligne de prose, `Analyze` sur chaque fenêtre :

> **92,5 %** des lignes de prose ont une fenêtre acceptée en « auto ».

Cause : **tout mot isolé est un atome math trivial** (« Note », « Classe » → candidat
unique = auto). **Le moteur est un *recognizer*, pas un *detector*.** Il faut une porte.

### Test 2 — porte structurelle (« signes évidents » + opérateurs)

Porte = la ligne contient un signal math : `^` `_`, fraction `a/b`, glue chiffre-lettre
(`x2`/`2x`), appel `f(`, raccourci `@x`, ou mot-clé évident (`lim sum cos … sqrt vec`).

> Précision : **21,3 %** des lignes de prose passent la porte (FP potentiels).
> Rappel : **50,4 % des maths ratées** (la porte laisse passer une formule sur deux).

**Pas de point de fonctionnement propre.** Les signes non ambigus (`lim/sum/cos/^/_`)
sont **absents de la moitié des vraies maths** (`2*x`, `2(x+1)`, `1+2+3`, `a mod b`,
`AB perp CD`, `u.v`, `f circ g`…). Et ajouter `+ ( * .` pour les rattraper effondre la
précision (parenthèses/points/`+` sont partout en prose). On ne peut pas avoir les deux :
c'est la **sensibilité au contexte** (`(` est math dans `2(x+1)`, prose dans `(k1 ∈ Z)`)
qu'un modèle de séquence capture et qu'une porte sans état ne distingue pas.

### Caveats honnêtes

- Le corpus de rappel (fixtures) est biaisé : sur-représente des formes rares (`mod`,
  `perp`, `circ`, `pm`) et de l'arithmétique triviale (`1+2+3`) qu'un élève tape rarement
  en ayant besoin d'auto-détection. Le rappel « utile » est meilleur que 50 %.
- La « prose » vient en partie d'un corrigé : une partie des 21,3 % est en fait des maths
  tapées en texte (`m=a x k2`), pas de la prose. Le vrai FP sur prose pure est < 21 %.

## Conclusion (provisoire)

Sur le pur axe précision/rappel de la détection passive, **le NER gagne** — la donnée
le justifie plus que l'hypothèse ne l'espérait. MAIS la bonne question n'est pas
« porte vs NER », c'est **as-tu besoin que la détection passive soit précise ?** Deux
échappatoires tiennent :

- la **popup est non-destructive** (jamais d'auto-commit ; Tab-commit opt-in OFF) → un
  faux positif = une popup qu'on ignore ;
- **Ctrl+Espace** est le chemin fiable, sans ML → les maths « ratées » par une porte sont
  rattrapées à la main.

Donc le choix est **philosophie produit**, pas accuracy :

1. **Détection passive best-effort** → porte lâche + popup ignorable, on **supprime le NER**
   (−129 Mo, −cold-start, −pipeline d'entraînement). Faux positifs = bruit visuel indolore.
2. **Détection passive silencieuse et précise** → garder le NER, au prix fort.

Non tranché. À décider si/quand le poids de l'installeur ou le cold-start redeviennent
prioritaires.
