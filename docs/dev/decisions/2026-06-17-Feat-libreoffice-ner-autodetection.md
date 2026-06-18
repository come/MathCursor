# Feat — Auto-détection NER dans l'extension LibreOffice (parité Word)

**Date :** 2026-06-17
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** `libreoffice-ext/` (`mathcursor.py`, nouveau `mc_ner/`, `build_oxt.py`,
`oxt/`), modèle `adapter-vsto/installer/payload/models/distilmult-v6/`
**Dépend de :** ADR `2026-06-16-Feat-libreoffice-uno-python-extension`

## Citation acté

> « non juste faire que la popup se declenche toute seule sur l'ensemble du paragraphe
> donc passage par l'inference ner » / « ner à partir du curseur (comme dans word) » /
> « ok puis fleche du bas et entrée pour selectionner comme dans word » — utilisateur,
> 2026-06-17.

Décisions plan mode : **cross-OS d'emblée** (deps natives Win/Mac/Linux bundlées) ;
**auto au démarrage du document** (Job UNO) ; popup **non-modale** pilotée au clavier.

## Contexte

L'extension (P4) convertit une **sélection** via Ctrl+Espace (popup modale de candidats
rendus). L'utilisateur veut le comportement Word : la popup se **déclenche seule pendant
la frappe** en faisant passer le **paragraphe courant au curseur** dans l'**inférence
NER** (même modèle ONNX `distilmult-v6` que Word, 3 labels BIO, seuil 0.85), puis se
navigue au clavier (↓/↑ + Entrée, Échap pour fermer), sans voler le focus.

**Dérisquage concluant** : LO embarque **Python 3.12.13 + pip**. Spike
(`libreoffice-ext/_spike_ner.py` + vendor onnxruntime 1.27/numpy) → le vrai modèle infère
dans le Python de LO et localise les zones correctement. Le port WordPiece + inférence est
donc validé ; reste à industrialiser et brancher l'auto-détection.

## Décision

Porter en Python/UNO le pipeline `AutoDetectController` de Word, **single-thread** :

- **Module `mc_ner/`** (pur Python, testable hors LO) : `tokenizer.py` (WordPiece avec
  offsets), `detector.py` (`Detector.detect` ONNX → spans BIO), `refiner.py` (port pur de
  `ZoneRefiner`/`NerInputWindow`), `__init__.load_detector` à **imports natifs paresseux**
  → renvoie `None` si deps/modèle absents (dégradation gracieuse = Ctrl+Espace marche,
  comme `AttachDetector` côté Word).
- **`mathcursor.py`** : lecture ¶+caret UNO (OLE masqués en 1 caractère pour aligner
  offsets ↔ `goRight`) ; contrôleur auto-détect (gardes → fenêtre → `detect` → refiner →
  caret ∈ zone → `analyze` → popup) ; **popup flottante non-modale** (`createPeer` +
  `setVisible`, pas de focus, réutilise `_previews`/`_place_at_caret`) ; `XKeyHandler`
  (throttle ≥100 ms ; si popup visible : ↓/↑ naviguent, Entrée insère la sous-plage, Échap
  ferme, autre touche ferme + laisse passer) ; `XCloseListener` pour nettoyer.
- **Job UNO** (`Jobs.xcu` + composant Python `XJob` enregistré au manifest) : sur OnLoad/
  OnNew d'un TextDocument, enregistre le key handler (zéro-clic).
- **Packaging** : `build_oxt.py` bundle `mc_ner/`, `models/distilmult-v6/`
  (`ZIP_STORED`), `vendor/<tag>/` par OS ; `mathcursor.py` choisit `vendor/<tag>` sur
  `sys.path` selon plateforme. **.oxt par OS** + build allégé sans NER conservé.

## Tradeoff & alternatives écartées

- **`threading.Timer` pour le debounce** : écarté — callback sur thread de fond, et créer
  un dialogue VCL hors thread principal gèle/crashe. Throttle par horodatage dans
  `keyReleased` (thread UI). Échappatoire si besoin d'un vrai timer : `AsyncCallback` pour
  marshaller vers le thread principal.
- **Popup modale en auto** : écarté — volerait le clavier en pleine frappe. Non-modale
  obligatoire. La modale reste pour le Ctrl+Espace manuel.
- **Porte heuristique au lieu du NER** : écarté — l'utilisateur veut l'inférence NER (cf.
  `docs/dev/architecture/ner-vs-vocab-detection.md` : le NER gagne en précision/rappel).
- **.oxt cross-OS unique** : écarté (>400 Mo) au profit d'un .oxt par OS (modèle 135 Mo
  une fois + vendor de l'OS).
- **Alpha/verre dépoli WPF** : non atteignable en UNO (VCL/thème système) → popup
  propre/flottante/padded, pas translucide.

## Conséquences

- **Nouveau** : `libreoffice-ext/mc_ner/`, composant Job + `oxt/Jobs.xcu`, `vendor/` +
  `models/` dans l'.oxt.
- **Modifié** : `mathcursor.py` (bootstrap plateforme, contrôleur, popup non-modale,
  key handler, close listener), `build_oxt.py`, `oxt/{description.xml,Addons.xcu,
  manifest.xml}`.
- **Validation** : parties pures `mc_ner` testables hors LO ; le reste (UNO, Job, popup,
  packaging) se confirme **dans LibreOffice installé** côté utilisateur, puis on itère.
- **Spikes d'abord** (cf. plan) : (1) thread + popup non-modale + latence, (2) offsets
  sous-plage avec OLE, (3) packaging Job + natives via `unopkg`.

## Validation (côté utilisateur)

Taper `Soit f(x)=x^2+1` → popup auto sous le curseur ; ↓ change de candidat, Entrée
insère à la bonne place ; prose → pas de popup ; Échap ferme ; Ctrl+Espace (modal) marche
toujours. Build/install .oxt par OS (`unopkg`), fumée Windows d'abord.
