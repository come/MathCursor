# Fix — Le hook clavier n'armait pas l'auto-détection sur les frappes AltGr (`[` `]` AZERTY)

**Date :** 2026-06-16
**Kind :** Fix
**Température :** molle
**Statut :** acté
**Supersedes :** [2026-06-16-Fix-zone-forward-delimiter-extension.md](2026-06-16-Fix-zone-forward-delimiter-extension.md)

## Citation acté

> « le bug est uniquement avec le [ et le ] tout le reste fonctionne nickel » puis (en voyant le hack de zone) « du coup attention ca c'etait degueu » « aussi ;) » puis « oui ! » — utilisateur, 2026-06-16.

## Contexte

En auto-détection, `[0;1[` rendait `[0;1]` (fermé) et `[a]` laissait un `]` résidu. Le log a tranché : le `[` final n'atteignait **jamais** le texte analysé (`endChar` toujours vide). Cause racine dans `KeyboardInterceptor` :

```csharp
if (!ctrlDown && IsTextKey(vkCode)) {
    bool altDown = ...;
    if (!altDown) OnTextKeyTyped?.Invoke();   // arme le debounce
}
```

Le debounce ne s'arme que si `!ctrlDown && !altDown`. Or sur **AZERTY**, `[` = **AltGr+5**, `]` = **AltGr+°**, et **AltGr = Ctrl+Alt** → `ctrlDown && altDown` → le debounce **ne s'arme pas**. Donc taper un crochet (ou `@`, `{`, `}`, `|`, `\`, `€`… tous AltGr) ne re-déclenchait pas la détection. Le `[` du début passait quand même (le caractère suivant armait), mais le `[` final non. `()` (Shift, touches normales) marchait → d'où « uniquement `[` et `]` ».

## Décision

Armer le debounce **aussi sur AltGr** (compositeur de caractères), tout en ignorant Ctrl-seul / Alt-seul (vrais raccourcis) :

```csharp
bool altDown = (GetKeyState(VK_MENU) & 0x8000) != 0;
bool altGr = ctrlDown && altDown;
if (IsTextKey(vkCode) && (altGr || (!ctrlDown && !altDown)))
    OnTextKeyTyped?.Invoke();
```

Sûr : `OnTextKeyTyped` ne fait que réarmer un timer one-shot → relance la détection NER (non destructif). `IsTextKey` couvre déjà la touche physique sous-jacente (`[` = AltGr+`5`, vk `0x35`).

## Conséquences

- **Code** : `KeyboardInterceptor.cs` (la garde modificateurs). C'est le seul changement de comportement.
- **Revert** : `TryExtendForwardDelimiters` (méthode `ZoneRefiner`, câblage `AutoDetectController`, 4 tests, logs de diag) retiré — rustine d'un symptôme. ADR `2026-06-16-Fix-zone-forward-delimiter-extension` retractée.
- **Périmètre** : corrige `[` `]` ET tous les compositeurs AltGr (`@ { } | \ €`…) qui ne ré-armaient pas l'auto-détection.

## Validation post-fix

Test Word (rebuild requis) : taper `[0;1[` → `[0;1[`, `[a]` → `[a]` sans résidu. Le NER restant potentiellement instable sur le crochet final, re-tester ; si flaky, stabilisation de zone PROPRE à rediscuter (≠ l'ancien hack).
