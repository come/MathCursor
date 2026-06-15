# Feat — Raccourcis grecs au clavier : `@` + lettre → lettre grecque

**Date :** 2026-06-15
**Kind :** Feat
**Température :** molle
**Statut :** acté
**Lié à :** [2026-06-10-Feat-culture-scoped-aliases.md](2026-06-10-Feat-culture-scoped-aliases.md) (mécanisme d'alias réutilisé)

## Citation acté

> « peux t'on faire un truc pour raccourci j'ai vu passer ça je trouve ça cool : "@t" => theta "@a" => alpha etc en alias du coup ? » — utilisateur, 2026-06-15. Choix de désambiguïsation : « Popup quand ambigu » ; périmètre : « Grec seulement ».

## Décision

Introducteur `@` au clavier : `@` suivi d'un run de lettres produit une lettre grecque.

- **Lettre unique** = déterministe : `@a`→α, `@b`→β, `@g`→γ, `@d`→δ, `@z`→ζ, `@i`→ι, `@k`→κ, `@l`→λ, `@m`→μ, `@n`→ν, `@x`→ξ, `@o`→ω, `@r`→ρ, `@s`→σ, `@u`→υ, `@c`→χ.
- **Lettre qui collisionne** = popup avec les candidats, le plus courant en tête (mécanisme de lectures multiples existant, comme `R`→[R, ℝ]) : `@t`→[θ, τ], `@e`→[ε, η], `@p`→[π, φ, ψ].
- **Nom complet** gratuit (résolu comme un mot normal) : `@theta`→θ, `@tau`→τ, `@Delta`→Δ (la casse de la 1re lettre donne la majuscule grecque).

`@o` est **déterministe** (ω) : omicron est écarté car `\omicron` n'est pas une commande LaTeX standard et la lettre est quasi inutilisée.

## Pourquoi

- **Réutilise l'existant** : la résolution passe par le mécanisme d'alias (`EngineCulture.Canon`) et, pour les collisions, par les lectures multiples (`Alts` sur un atome) — déjà éprouvés (`R`/ℝ, `:`/÷). Aucun nouveau concept.
- **`@` est libre** : le caractère n'a aucun sens mathématique en entrée (il levait `caractère inattendu`), donc zéro collision avec une notation existante.
- **Popup quand ambigu** plutôt qu'une table déterministe arbitraire : pas de convention à mémoriser pour les « perdants » (τ, η, φ, ψ), et `@t=>theta` reste vrai (θ présélectionné, Entrée valide).
- **Première lettre du nom** comme principe : prévisible (`@<initiale>`), aligné sur l'exemple utilisateur.

Alternatives écartées : table 100 % déterministe (impose de mémoriser `@ta`=τ etc.) ; préfixe non ambigu (`@t` ne donnerait rien).

## Conséquences

- **Moteur** : `Lexer.cs` (branche `@` + run, ~6 lignes), `Vocabulary.cs` (3 entrées synthétiques `·greek-t`/`·greek-e`/`·greek-p` à `Alts` + 19 alias `@<lettre>` dans le set générique).
- **Données** : les mappings vivent en alias (axe C, générique car le grec est neutre en langue) — FR et US en héritent.
- **Tests** : nouvelles fixtures `@a`, `@t`, `@e`, `@p`, `@o`, `@theta`, composition (`@a+@b`), juxtaposition (`2@p`). Corpus mis à jour.
- **Hors scope v1** : raccourcis « ressemblance » hors initiale (`@f`→φ, `@w`→ω, `@h`→η, `@y`→ψ) et symboles non grecs (`@8`→∞…) — ajout trivial ultérieur si retour positif.

## Validation post-feat

Fixtures moteur vertes (corpus + nouvelles entrées), `@t` propose [θ, τ] avec θ présélectionné, `@a` passe en auto.
