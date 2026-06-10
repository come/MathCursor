# Feat — Auto-détection NER en cours de frappe (debounce clavier, pas de polling)

**Date :** 2026-06-10
**Kind :** Feat
**Température :** forte (mécanisme debounce-hook) / molle (délai 400 ms, seuil 0.85)
**Statut :** acté
**Supersedes :** —
**Lié à :** [2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md](2026-06-10-Refactor-phase2-adapter-orchestration-rewrite.md) (« NER différé Phase 4 » — cette phase) ; brief `docs/briefs/detection-ner.md` ; ADR DocMath `2026-04-28-Fix-ort-version-bay-trail-compat` (ONNX pinné 1.16.3)

## Citation acté

> « yes nickel » — utilisateur, 2026-06-10, en validation du plan
> (« debounce sur frappe, pas de polling … on garde ctrl+espace pour la
> forcer si jamais NER ne s'est pas reveillé »).

## Contexte

La beta convertit sur trigger manuel (Ctrl+Espace). L'expérience cible du
brief ergonomie est le « copilote silencieux » : la popup apparaît d'elle-même
quand on tape des maths. Le NER (DistilBERT multilingual quantizé, ONNX
Runtime, WordPiece C# pur) est porté et compilé depuis la Phase 2 mais
dormant. L'ancien DocMath le pilotait par un polling DispatcherTimer 200 ms —
contraire à la règle projet « triggers explicites + events natifs, pas de
polling » (CLAUDE.md), et coûteux à vide (lecture ¶ + COM 5×/s en continu).

## Décision

### 1. Déclenchement : debounce armé par le hook clavier

Le hook `WH_KEYBOARD` thread-local existant observe (sans les consommer) les
frappes « texte » (lettres, chiffres, opérateurs OEM, espace, Backspace,
Delete — hors Ctrl/Alt) via un nouveau callback `OnTextKeyTyped`. Chaque
frappe réarme un **timer one-shot de 400 ms** : la détection ne tourne qu'à
la **pause de frappe**, et rien ne tourne quand l'utilisateur n'écrit pas.
Réactivité ≈ polling, coût au repos nul, conforme à la règle projet.

### 2. Pipeline auto (`Host/AutoDetectController.cs`)

```
pause 400 ms → guards (réglage off / commit / nav mode / popup edit /
               caret dans OMath / signal de sortie tab|double-espace)
  → WordContextReader (¶, OMaths masquées)
  → NerInputWindow.Compute (fenêtre entre OMaths voisines du caret)
  → MathNerDetector.Detect (seuil 0.85) → coords retraduites ¶
  → ZoneRefiner : FilterOutOMathOverlap → PickNearestZone(caret)
    → TryExtendForwardWhitespace → exige zone.End == caret (frappe en cours)
    → ExtendBackwardWithKeyword (limite/racine/somme…)
  → ConversionController.TryProposeAuto(ZoneSpan)
```

`TryProposeAuto` = même moteur, même popup, mêmes touches que le manuel,
mais **silencieux** : pas de message StatusBar en échec, popup masquée si la
zone disparaît, **pas de re-show si la zone est identique** (anti-flicker).
La popup auto se met à jour au fil de la frappe ; elle n'est **jamais**
rafraîchie en nav mode (la sélection de l'utilisateur est sacrée).

### 3. Ctrl+Espace inchangé = forçage + extension

Le trigger manuel reste l'escape hatch quand le NER dort, et l'extension
itérative s'applique aussi à une zone venue du NER (la zone proposée est la
même `ZoneSpan` ; un Ctrl+Espace popup ouverte l'étend d'un cran).

### 4. Démarrage non bloquant, dégradation propre

`ThisAddIn` cherche le modèle (`MATHCURSOR_MODEL_DIR`,
`%LocalAppData%\MathCursor\models`, dossier add-in, fallback dev
`D:\Software\DocMath\models` — modèle ~129 Mo hors git) et le charge en
`Task.Run` + warm-up (1ʳᵉ inférence ~500 ms hors thread UI). **Modèle absent
ou échec de chargement = pas de crash** : auto-détection silencieusement
inactive, Ctrl+Espace intact, log.

### 5. Réglage utilisateur

`AppSettings.AutoDetect` (bool, défaut **true**), persisté `auto_detect`
dans settings.json, case à cocher dans la fenêtre Paramètres. Relu à chaque
tick → bascule sans redémarrage. (= mode « Manuel » du brief ergonomie ;
le mode fin Auto/Manuel/Silent viendra si le besoin se confirme.)

## Tradeoff & alternatives écartées

- **Polling 200 ms permanent** (DocMath) : même réactivité mais lecture ¶ +
  COM en continu même à l'arrêt ; contraire à la règle projet. Rejeté.
- **Events Word seuls** (`WindowSelectionChange`) : ne se déclenche pas à
  la frappe de caractères → impossible de détecter « en cours de frappe ».
- **NER sur thread de fond à chaque tick** : l'inférence fait ~25 ms et la
  lecture du ¶ doit rester sur le thread UI Word ; le découplage
  n'apporterait que de la complexité (seul le CHARGEMENT initial est async).

## Conséquences

- **Nouveau** : `Host/AutoDetectController.cs` (debounce + pipeline).
- **Modifié** : `KeyboardInterceptor` (+`OnTextKeyTyped`, observation
  non-consommante), `ConversionController` (+`TryProposeAuto`, dédup zone),
  `ThisAddIn` (chargement NER async + câblage), `AppSettings`/`SettingsStore`
  /`SettingsWindow` (+AutoDetect), csproj.
- **Tests** : couche pure inchangée ; pipeline auto = logique Word-couplée,
  validation manuelle (cf. ci-dessous).
- **Perf** : zéro travail sans frappe ; à la pause : lecture ¶ + NER ~25 ms
  + forest sur la zone (qq ms).

## Validation post-fix

1. Taper « on a 1/x+1 » puis pause → popup apparaît seule sous la zone ;
   continuer à taper → elle suit ; Tab commit.
2. Taper de la prose pure → pas de popup. Tab ou double espace → popup se ferme.
3. Flèche bas (nav mode) puis continuer à réfléchir → la sélection ne saute pas.
4. Ctrl+Espace force quand le NER n'a rien vu ; re-Ctrl+Espace étend.
5. Renommer le dossier modèle → l'add-in démarre, pas de popup auto,
   Ctrl+Espace fonctionne.
6. Paramètres → décocher « Détection automatique » → plus de popup auto sans
   redémarrage.
