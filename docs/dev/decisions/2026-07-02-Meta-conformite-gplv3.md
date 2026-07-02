# Meta — Mise en conformité GPLv3 du dépôt

**Date :** 2026-07-02
**Kind :** Meta
**Température :** forte
**Statut :** acté

## Décision

Rendre le dépôt **pleinement conforme GNU GPL v3** avant publication publique
(et dépôt sur la Forge des communs numériques éducatifs) :

1. `LICENSE` (GPLv3 intégral) à la racine **et** embarqué par l'installeur VSTO
   (`{app}\LICENSE`, + `THIRD-PARTY-NOTICES.md` et `licenses/Apache-2.0.txt`).
2. **En-tête GPLv3 « v3-or-later »** en tête de **tous** les fichiers source
   first-party (158 fichiers : 127 C#, 17 Rust, 8 TS, 6 Python), titulaire
   **Côme de Percin**. Générés (AssemblyInfo, Designer) et vendored exclus.
3. **Notice interactive** GPLv3 (copyright + « sans garantie » + lien dépôt) dans
   l'« À propos » de l'add-in Word **et** de l'extension LibreOffice.
4. `THIRD-PARTY-NOTICES.md` : entrée **NER ferme** (base DistilBERT Apache 2.0 +
   poids fine-tunés © Côme de Percin), plus aucun « à confirmer ».
5. `licenses/Apache-2.0.txt` + attribution DistilBERT (obligation Apache 2.0).
6. `README` : section **Licence** + section **Compiler depuis les sources**
   (= source correspondante GPL §6).
7. **Audit des dépendances** ([`docs/dev/licenses-audit-2026-07-02.md`](../licenses-audit-2026-07-02.md)) :
   NuGet / Cargo / npm / Python — **zéro incompatibilité**.

Portée de la licence : **tout le code first-party du monorepo** (moteur, tous les
adapters, démo web, outils), pas seulement l'add-in Word. Une seule `LICENSE` à
la racine couvre le dépôt.

## Pourquoi

- Le projet **déclarait** GPL v3 sans l'**embarquer** : ni `LICENSE`, ni en-têtes,
  ni source correspondante documentée → non conforme, et l'hygiène de licence est
  le premier point qu'un relecteur libriste vérifie.
- **Auteur unique préservé** : le copyright unique (personne physique, Côme
  Percin, pas le nom du logiciel) garde toutes les options ouvertes — y compris
  un éventuel volet propriétaire futur par-dessus la même base. Aucune décision
  commerciale n'est prise ici ; **aucune mention de dual-licensing dans le dépôt**
  (les options sont préservées par la titularité, pas par une clause écrite).
- **v3-or-later** (recommandation FSF) plutôt que v3-only : plus souple, sans
  effet sur la faculté de l'auteur unique à relicencier sa propre base.
- Base NER : DistilBERT est **Apache 2.0**, permissive et **compatible GPLv3** ;
  l'obligation Apache (attribution + texte de licence) est satisfaite. Les poids
  fine-tunés sont l'œuvre de l'auteur → GPL v3 avec le reste.

## Conséquences

- 158 fichiers source portent l'en-tête (comments purs → zéro impact
  fonctionnel/comportemental ; contrainte du chantier respectée).
- L'installeur dépose désormais `LICENSE`, `THIRD-PARTY-NOTICES.md`,
  `licenses/Apache-2.0.txt` dans le dossier d'install.
- L'« À propos » Word et LibreOffice affichent la notice GPLv3 + lien
  `https://github.com/come/MathCursor`.
- **Gouvernance** (note, pas une tâche de code) : ne fusionner aucun code externe
  sous GPL **sans CLA**, pour ne pas fragmenter le copyright unique. Réflexe à
  garder pour toute contribution via la Forge.
- Suivi recommandé (CI, non bloquant) : `cargo about`, `dotnet list package
  --include-transitive`, `npm ls --prod` pour figer l'inventaire transitif.

## Validé par l'utilisateur

Brief « Mise en conformité GPLv3 de MathCursor », puis validations explicites en
session :
> « peux tu faire ca ? »

Décisions tranchées (AskUserQuestion) :
- Titulaire du copyright : **Côme de Percin**.
- Portée : **v3-or-later**.
- URL dépôt : **https://github.com/come/MathCursor**.
- Périmètre : **tout le first-party en GPLv3** →
  > « oui »

## Statut

acté
