# Brief — Formulaire "Signaler une erreur" avec backend Cloudflare

**Auteur de la demande :** come (utilisateur principal)
**Date :** 2026-04-30
**Branche :** `lattice-engine`
**Public cible :** agent C#/VSTO + agent Cloudflare Workers/Pages
autonomes qui ne connaissent pas le projet, intervient sur
`adapter-vsto/` (UI, capture) et `docs/functions/` (endpoint).

---

## 1. Le besoin

Le bouton "Signaler un souci" actuel **génère un zip** (log + screenshot
+ contexte texte) et le **copie dans le presse-papier** comme
fichier-droppable. L'utilisateur doit ensuite **manuellement** :

1. Lire un dialog avec instructions
2. Ouvrir WhatsApp Web ou Outlook
3. Coller le zip
4. Écrire un message expliquant le bug
5. Envoyer

C'est **trop d'étapes** pour de jeunes utilisateurs (élèves PAP) ou des
profs pressés. **Résultat observé** : très peu de retours, et ceux qui
arrivent sont souvent juste le zip sans contexte ("ça marche pas").

**Doctrine cible** : un **formulaire pré-rempli en 1 fenêtre**, avec
champ libre pour l'explication, et **un seul clic "Envoyer"** qui pousse
le rapport directement à un endpoint Cloudflare. Le user voit la
dernière action effectuée, peut la corriger/commenter, et envoie.

## 2. UX visée

### 2.1. Trigger

Bouton `Signaler un souci` dans le ribbon — **inchangé** (cf. brief
`2026-04-30-ribbon-dedicated-tab-with-examples.md` qui le déplace dans
l'onglet MathCursor mais ne change pas son `onAction`).

### 2.2. Maquette de la fenêtre WPF

Au lieu du dialog d'instructions actuel (`OnReportIssueClicked`), on
ouvre une fenêtre WPF modale `FeedbackWindow.xaml` :

```
┌──────────────────────────────────────────────────────────────┐
│ MathCursor — Signaler une erreur                       [X]   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│ Tu as rencontré un souci ? Vérifie les infos ci-dessous     │
│ et explique ce qui ne va pas. On lit tout, ça nous aide.    │
│                                                              │
│ ── Dernière action ──────────────────────────────────────    │
│                                                              │
│ Ce que tu as tapé :                                          │
│ ┌──────────────────────────────────────────────────────┐    │
│ │ f(k) = 1/x^2 + tan2(x)                                │    │
│ └──────────────────────────────────────────────────────┘    │
│                                                              │
│ Ce que MathCursor a proposé :                                │
│ ┌──────────────────────────────────────────────────────┐    │
│ │ f(k) = 1/x² + tan²(x)                                 │    │
│ └──────────────────────────────────────────────────────┘    │
│                                                              │
│ Ce qui a été inséré dans Word :                              │
│ ┌──────────────────────────────────────────────────────┐    │
│ │ f(k)=∈t²(x)+1/x²                                      │    │
│ └──────────────────────────────────────────────────────┘    │
│                                                              │
│ ── Décris le souci ─────────────────────────────────────     │
│                                                              │
│ ┌──────────────────────────────────────────────────────┐    │
│ │ Le tan² s'est transformé en ∈t² au moment du commit. │    │
│ │ Je voulais une tangente carrée.                      │    │
│ │                                                      │    │
│ └──────────────────────────────────────────────────────┘    │
│                                                              │
│ ☑ Joindre une capture d'écran (recommandé)                   │
│ ☐ Joindre les 64 derniers Ko de log technique                │
│                                                              │
│ Ces données partent vers notre serveur (Cloudflare).         │
│ Pas de doc entier, pas d'identifiant. Voir détails [?]       │
│                                                              │
│  [ Annuler ]   [ Copier dans un mail ]   [ Envoyer ]         │
└──────────────────────────────────────────────────────────────┘
```

**3 actions possibles** :
- **Envoyer** (action principale, à droite, bouton accent) : POST direct
  vers le backend Cloudflare. Action en 1 clic.
- **Copier dans un mail** (action secondaire) : compose le texte du
  rapport dans le presse-papier + ouvre le client mail par défaut
  (`mailto:come2percin@wanadev.fr?subject=MathCursor%20-%20rapport`).
  Plan B pour les machines derrière un proxy d'entreprise, ou les users
  qui préfèrent garder la traçabilité dans leur boîte mail.
- **Annuler** : ferme sans rien faire.

### 2.3. Comportements

**Pré-remplissage** : à l'ouverture, les 3 champs (saisie/popup/insertion)
sont remplis depuis la **dernière action enregistrée** par le
`SuggestionService` (cf. §3.1). Si pas d'action récente (vient de
démarrer Word), les champs sont vides avec placeholder "(pas d'action
récente)".

**Édition** : les 3 champs sont **éditables** par l'utilisateur (au cas
où le pré-remplissage n'est pas pertinent — ex: bug rencontré il y a 5
min, popup déjà fermée). Permet aussi de retirer du contenu sensible
avant envoi.

**Champ libre** : focus auto au chargement. Multilignes, ~5 lignes
visibles, scroll si plus long.

**Toggles** :
- Capture d'écran : coché par défaut, débrayable. La capture inclut TOUTE
  la fenêtre Word — donc potentiellement contenu sensible. L'user doit
  pouvoir refuser.
- Log : décoché par défaut. Les logs peuvent contenir des bouts de texte
  tapé. Opt-in.

**Bouton Envoyer** :
- Désactivé si le champ libre est vide (`Length < 5` chars). On force un
  minimum d'explication, sinon on aura des envois vides.
- Au clic : POST async vers le backend (cf. §4). Pendant l'envoi :
  bouton grisé + spinner. Au retour OK : message de succès + ferme la
  fenêtre. Au retour KO : message d'erreur + propose de basculer sur
  "Copier dans un mail" (sans perdre ce que l'user a tapé).

**Bouton Copier dans un mail** :
- Même condition d'activation que Envoyer (champ libre rempli).
- Au clic :
  1. Construit un payload texte lisible (markdown-ish) avec les 3
     champs + commentaire + métadonnées versions. Pas de screenshot/log
     ici (un email texte, pas d'attachement programmatique fiable
     cross-client).
  2. Copie ce texte dans le presse-papier.
  3. Lance `Process.Start("mailto:come2percin@wanadev.fr?subject=
     MathCursor%20-%20rapport")` qui ouvre le client mail par défaut
     (Outlook, Thunderbird, webmail si configuré).
  4. L'user n'a qu'à Ctrl+V dans le corps du mail.
- Au retour : message info "Texte copié, colle-le (Ctrl+V) dans le
  mail qui vient de s'ouvrir." + ferme la fenêtre.
- Si screenshot/log activés : ils sont écrits en zip dans %Temp% (comme
  le comportement actuel) et le presse-papier contient en plus le
  chemin du zip à attacher manuellement.

**Bouton Annuler** : ferme sans rien envoyer.

## 3. Données capturées

### 3.1. Pré-remplissage : où récupérer "saisie / popup / insertion"

Aujourd'hui, ces 3 informations sont **présentes dans les logs** mais
non exposées en API. Il faut les **mémoriser explicitement** dans
`SuggestionService` :

```csharp
// Nouveau, dans SuggestionService.cs
internal sealed class LastActionSnapshot
{
    public DateTime At { get; set; }
    public string SourceText { get; set; }       // "f(k) = 1/x^2 + tan2(x)"
    public string ProposedLatex { get; set; }    // "f(k) = \frac{1}{x^2} + \tan^2(x)"
    public string ProposedUnicodeMath { get; set; } // après LatexToUnicodeMath
    public string CommittedLatex { get; set; }   // ce qui a été passé à InsertOMath
    public string ParagraphContext { get; set; } // le paragraphe où ça s'est passé
}

private LastActionSnapshot _lastAction;
public LastActionSnapshot GetLastAction() => _lastAction;
```

**Quand mettre à jour** :
- `_lastAction.SourceText + ProposedLatex` : à chaque `ShowPopup` (juste
  avant l'appel)
- `_lastAction.CommittedLatex + ProposedUnicodeMath` : juste avant
  l'appel à `InsertOMathAt`

**Pas de queue, juste 1 snapshot** : le bug est *quasi toujours* sur la
dernière action. Plus simple à coder, plus simple à comprendre côté
user. Si vraiment l'user veut signaler un truc plus ancien, il édite les
champs à la main.

### 3.2. Champs envoyés au backend

JSON POST :

```json
{
  "version": "0.5.3",
  "ts": "2026-04-30T14:30:00Z",
  "source_text": "f(k) = 1/x^2 + tan2(x)",
  "proposed_latex": "f(k) = \\frac{1}{x^2} + \\tan^2(x)",
  "committed_latex": "f(k)=\\in t^{2}(x)+\\frac{1}{x^{2}}",
  "user_comment": "Le tan² s'est transformé en ∈t² au moment du commit.",
  "include_screenshot": true,
  "include_log": false,
  "screenshot_b64": "iVBORw0KGgo...",  // si include_screenshot
  "log_tail": null,                     // si include_log : string text
  "metadata": {
    "word_version": "16.0.18526.20144",
    "os_version": "Microsoft Windows NT 10.0.26200.0",
    "dotnet_version": "4.0.30319.42000"
  }
}
```

**Ne PAS envoyer** :
- Le document entier (déjà respecté avec le bundle actuel)
- Le presse-papier
- Aucun identifiant utilisateur (pas de email, pas de UUID device,
  rien). Si on veut une déduplication, le backend pourra hasher
  `version + ts + source_text` côté serveur.

### 3.3. Taille payload

Estimations max raisonnables :
- Champs texte : ~2-5 KB max
- Screenshot PNG (1920×1080 quality auto) : ~200-500 KB après gzip
- Log tail : 64 KB max (limite déjà existante)

→ payload ~600 KB max. Cloudflare Workers acceptent jusqu'à 100 MB
(plus que large). Pas d'inquiétude.

## 4. Backend Cloudflare

### 4.1. Choix : Pages Function plutôt que Worker dédié

Le projet a déjà une stack Cloudflare Pages avec Functions
(`docs/functions/download/[[filename]].js`). On reste dans cet
environnement plutôt que créer un Worker séparé : 1 seul déploiement, 1
seul domaine, partage des secrets.

**Endpoint** : `POST https://mathcursor.pages.dev/api/report`
→ implémenté dans `docs/functions/api/report.js`.

### 4.2. Implémentation Pages Function

```js
// docs/functions/api/report.js
export async function onRequestPost({ request, env }) {
  // Rate limit basique : 5 reports / IP / heure (KV namespace)
  const ip = request.headers.get('CF-Connecting-IP') || 'unknown';
  const key = `rl:${ip}:${new Date().toISOString().slice(0, 13)}`;
  const count = parseInt(await env.RATE_LIMIT_KV.get(key) || '0', 10);
  if (count >= 5) {
    return new Response('Too many reports, try in an hour', { status: 429 });
  }
  await env.RATE_LIMIT_KV.put(key, String(count + 1), { expirationTtl: 7200 });

  // Validation taille
  const body = await request.text();
  if (body.length > 5 * 1024 * 1024) {
    return new Response('Payload too large', { status: 413 });
  }

  // Parse + validation minimale
  let report;
  try { report = JSON.parse(body); }
  catch { return new Response('Invalid JSON', { status: 400 }); }
  if (!report.source_text && !report.user_comment) {
    return new Response('Empty report', { status: 400 });
  }

  // Stockage R2 : 1 fichier JSON par report
  const id = crypto.randomUUID();
  const date = new Date().toISOString().slice(0, 10);
  const key2 = `reports/${date}/${id}.json`;

  // Si screenshot : extraire en fichier séparé pour économiser le JSON
  if (report.screenshot_b64) {
    const png = Uint8Array.from(atob(report.screenshot_b64), c => c.charCodeAt(0));
    await env.REPORTS_BUCKET.put(`reports/${date}/${id}.png`, png, {
      httpMetadata: { contentType: 'image/png' },
    });
    delete report.screenshot_b64;
    report.screenshot_url = `${date}/${id}.png`;
  }

  await env.REPORTS_BUCKET.put(key2, JSON.stringify(report, null, 2), {
    httpMetadata: { contentType: 'application/json' },
  });

  return new Response(JSON.stringify({ ok: true, id }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
```

### 4.3. Setup R2 + KV

À ajouter dans `tools/cloudflare/deploy.sh` ou en one-shot :

```bash
# R2 bucket pour les rapports
npx wrangler r2 bucket create mathcursor-reports

# KV namespace pour rate limiting
npx wrangler kv:namespace create RATE_LIMIT_KV
```

Puis bindings dans `wrangler.toml` (ou Pages Functions equivalent — à
vérifier avec la doc actuelle Cloudflare Pages) :

```toml
[[r2_buckets]]
binding = "REPORTS_BUCKET"
bucket_name = "mathcursor-reports"

[[kv_namespaces]]
binding = "RATE_LIMIT_KV"
id = "<id-du-namespace>"
```

### 4.4. Consultation des reports (côté admin)

Pas d'UI au MVP : on liste les reports via wrangler.

```bash
npx wrangler r2 object list mathcursor-reports --prefix=reports/2026-04-30/
npx wrangler r2 object get mathcursor-reports reports/2026-04-30/abc-uuid.json
```

Ajouter une commande dans `tools/cloudflare/` :

```bash
tools/cloudflare/list-reports.sh [DATE]   # liste les reports d'une date
tools/cloudflare/get-report.sh ID         # télécharge un report (json + png si présent)
```

Phase 2 (plus tard) : page admin protégée pour browser les reports en
HTML.

### 4.5. Coût Cloudflare

- R2 storage : ~$0.015/GB/mois. À 600 KB/report et 100 reports/jour, on
  est à ~1.8 GB/mois après 1 an. ~$0.03/mois. Négligeable.
- KV : gratuit jusqu'à 100k reads/jour. On est très loin.
- Pages Functions : gratuit jusqu'à 100k invocations/jour. Très loin.

Ordre de grandeur : **gratuit en pratique**, < $1/mois si succès viral
inattendu.

## 5. Confidentialité — point dur

C'est le **changement de doctrine** important : avant, le user envoyait
manuellement (= consentement explicite à chaque envoi). Maintenant on
push automatiquement à un serveur. Doit être assumé clairement.

**Engagements à respecter** (à afficher dans la fenêtre, lien `[?]`) :

1. **Aucun identifiant** envoyé. Pas d'email, pas de nom, pas de UUID
   machine, pas d'IP stockée (CF la log mais on ne l'enregistre pas dans
   le report).
2. **Jamais le document entier**. Seulement : la dernière action
   (saisie/popup/commit) + paragraphe courant + screenshot si opt-in +
   log si opt-in.
3. **Screenshot opt-in** par défaut coché mais clairement visible. Un
   user qui travaille sur un doc confidentiel peut décocher.
4. **Log opt-in** par défaut décoché. Le log peut contenir des bouts de
   texte tapé.
5. **Pas de retention infinie** : politique de purge à 6 mois (à
   implémenter via R2 lifecycle rules ou cron Worker).
6. **Pas de partage tiers**. Les reports restent dans le R2 bucket
   privé du compte CF.

À ajouter au site : page `/privacy.html` (ou section dans existant) qui
liste ces engagements de manière lisible.

## 6. Fichiers à toucher

### 6.1. Côté add-in (`adapter-vsto/`)

| Fichier | Modification |
|---|---|
| `Host/SuggestionService.cs` | Ajouter `LastActionSnapshot` + 2 hooks de mise à jour (au `ShowPopup` + au `InsertOMath`). Méthode publique `GetLastAction()`. |
| `Host/FeedbackBundle.cs` | Refactor : extraire la capture screenshot + log tail en méthodes publiques `CaptureScreenshotPng()` et `ReadLogTail()`. Le zip reste dispo en fallback (cf. §7). |
| `Host/FeedbackSender.cs` | NOUVEAU : POST async vers `/api/report`, gère timeout, retry, retour. |
| `UI/FeedbackWindow.xaml` | NOUVEAU : maquette WPF (cf. §2.2). |
| `UI/FeedbackWindow.xaml.cs` | NOUVEAU : code-behind, lit `_lastAction`, gère le bouton Envoyer (appel `FeedbackSender`). |
| `RibbonCallback.cs:106` | Remplacer `OnReportIssueClicked` : au lieu d'ouvrir le dialog d'instructions, ouvrir `FeedbackWindow` modale. |
| `Strings.cs` | Ajouter labels FR/EN pour les nouveaux strings (titre fenêtre, libellés champs, messages succès/erreur). |
| `MathCursor.csproj` | Ajouter `<Compile Include="UI\FeedbackWindow.xaml.cs" />` + page WPF. |

### 6.2. Côté backend (`docs/functions/`)

| Fichier | Modification |
|---|---|
| `docs/functions/api/report.js` | NOUVEAU : Pages Function POST endpoint. |
| `tools/cloudflare/README.md` | Documenter setup R2 + KV. |
| `tools/cloudflare/list-reports.sh` | NOUVEAU : utility CLI. |
| `tools/cloudflare/get-report.sh` | NOUVEAU : utility CLI. |
| `docs/privacy.html` (ou section) | NOUVEAU : page privacy (engagements §5). |

## 7. Phasage proposé

**Phase A — Backend Cloudflare seul** (~2-3h)
- Pages Function `report.js` + setup R2/KV
- Test manuel via `curl` (POST JSON → vérifier R2)
- Pas de UI add-in encore

**Phase B — `LastActionSnapshot` + capture méthodes publiques** (~2h)
- Modifier `SuggestionService` + `FeedbackBundle`
- Tests unitaires sur le snapshot

**Phase C — `FeedbackWindow` WPF avec les 2 boutons d'action** (~4-5h)
- XAML + code-behind avec les 3 actions (Annuler / Copier mail /
  Envoyer)
- Bouton **Envoyer** : `FeedbackSender` POST → endpoint
- Bouton **Copier dans un mail** : génère payload texte + clipboard +
  `mailto:` (réutilise une partie de `FeedbackBundle` actuel pour
  screenshot/log si toggles activés)
- Remplacer `OnReportIssueClicked` pour ouvrir cette fenêtre
- Sur erreur du POST (timeout, proxy bloque) : bascule auto sur "Copier
  dans un mail" sans perdre la saisie

**Phase D — Page privacy** (~30min)
- Publier `/privacy.html` avec les engagements §5

**Phase E — Outils admin** (~1h)
- Scripts `list-reports.sh` + `get-report.sh`
- Doc dans `tools/cloudflare/README.md`

Total : ~10-12h. À phaser sur 2-3 sessions.

## 8. Risques / points d'attention

### 8.1. Add-in derrière un proxy d'entreprise

Certains lycées ont des proxies HTTP qui bloquent ou interceptent
HTTPS. L'envoi via "Envoyer" peut échouer. → Le bouton **Copier dans
un mail** est la solution offerte de manière first-class (pas un
fallback honteux). Bascule auto en cas d'échec POST pour ne pas faire
deviner à l'user qu'il faut changer de bouton.

### 8.2. Screenshot — fuite confidentielle

Si l'user travaille sur un doc avec des notes perso et oublie de
décocher, on reçoit son écran. **Mitigation** : (a) toggle bien visible
et libellé clair ; (b) preview thumbnail dans la fenêtre avant envoi ?
(à ajouter en phase ergo si retour user).

### 8.3. Pré-remplissage des 3 champs — quand "vide" ?

Si l'utilisateur démarre Word et clique direct sur "Signaler" sans
avoir tapé : `_lastAction == null`. La fenêtre s'ouvre avec les champs
vides + placeholder "(pas d'action récente)". Le user devra écrire son
souci à la main. Acceptable.

### 8.4. CORS

`docs/functions/api/report.js` doit gérer CORS si jamais on veut tester
depuis localhost ou la web demo. Au minimum :

```js
const corsHeaders = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};
// + handler OPTIONS preflight
```

L'add-in VSTO n'est pas dans un browser donc pas de CORS, mais utile
pour debug + future web demo "report bug from demo".

### 8.5. Anti-abus

Rate limit par IP est un minimum. Si un attaquant veut DoS :
- 5 reports/h/IP via KV
- Limite payload 5 MB par request
- R2 max ~1 GB/mois en pratique → coût plafonné

Si abus avéré : ajouter Turnstile (CAPTCHA Cloudflare) côté formulaire.
Pas au MVP.

### 8.6. Versioning de l'endpoint

L'add-in v0.5.x parle au `/api/report`. Si on change le contrat dans le
futur, il faut soit : versionner (`/api/v2/report`), soit garder
backward compat dans le handler.
**Recommandation** : versionner dès le départ → `/api/v1/report`. Coût
zéro maintenant, évite la dette.

## 9. Effort estimé global

| Phase | Effort |
|---|---|
| A (Backend Cloudflare seul) | ~2-3h |
| B (LastActionSnapshot + capture publique) | ~2h |
| C (FeedbackWindow WPF + envoi) | ~3-4h |
| D (Fallback offline + page privacy) | ~1-2h |
| E (Outils admin CLI) | ~1h |
| **Total** | **~9-12h** |

## 10. Hors scope (à NE PAS faire dans ce brief)

- Authentification user (pas d'identifiant = anonymat assumé).
- Réponse asynchrone à l'utilisateur (pas de "on te répondra par
  email" — pas d'email collecté).
- Browser des reports en HTML — phase 2.
- Notification Slack/Discord à chaque report — phase 2 si besoin.
- Stats de bug (top patterns d'erreur) — phase 2.
- Auto-detection du screenshot pour flouter le contenu sensible —
  hors scope, trop complexe.
