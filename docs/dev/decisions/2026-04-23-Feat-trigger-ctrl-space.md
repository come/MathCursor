# Feat — Trigger explicite Ctrl+Espace

**Date :** 2026-04-23
**Kind :** Feat
**Température :** molle
**Statut :** acté

## Décision

Raccourci **Ctrl+Espace** qui force la popup sur le span `caret → dernier
stopword / délimiteur / fin d'OMath précédent`, en bypass complet du NER.

## Pourquoi

- Cas d'échec observé : "Soit f et g" → on convertit `f` → le NER ne re-détecte
  plus `g` tout seul (perte de contexte après masquage du `f` converti).
- Le CLAUDE.md cadrait déjà : *"Triggers explicites : conversion via raccourci
  (Ctrl+Espace) ou bouton, pas de polling"*. On n'avait juste jamais câblé le
  raccourci.
- Donne à l'utilisateur une issue de secours fiable quand le NER rate — sans
  avoir à relancer Word ou retaper l'expression.

## Conséquences

- `KeyboardInterceptor` : nouveau handler `OnCtrlSpacePressed`.
- `SuggestionService.TriggerManual()` calcule le span, envoie au pattern engine,
  entre direct en mode navigation (pas besoin de flèche bas).
- Liste de stopwords FR inline dans `SuggestionService` — si on multilingue,
  faudra tirer depuis `data/stopwords.json`.
- Ctrl+Espace native de Word ("supprimer mise en forme caractère") est shuntée
  tant que l'add-in tourne. Acceptable pour la beta ; à retraiter si retour
  négatif.

## Validé par l'utilisateur

> "j'ai un soucis si je fais Soit f et g si je transforme f, plus rien ne va
> m'etre proposé sur g. je pense que le probleme n'est pas trivial, mais du
> coup je m'interroge sur faire un ctrl+espace pour forcer la popup dans ce
> cas là. il envoie le span cursor->stopword et tente des sugestions"

## Statut

acté
