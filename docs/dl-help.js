// Aide au téléchargement de l'installeur Word (ADR 2026-11-02-Feat-code-signing-certum).
// Tant que l'installeur n'est pas signé, SmartScreen avertit « n'est pas fréquemment
// téléchargé ». Le clic sur un lien .exe lance le téléchargement ET ouvre cette popup.
(function () {
  const TEXT = {
    fr: {
      title: "Le téléchargement a commencé",
      lead: "Si Windows ou Edge affiche « n'est pas fréquemment téléchargé » :",
      edge: "<strong>Dans Edge</strong> : cliquez sur « … » à côté du fichier, puis « Conserver », puis « Afficher plus » et « Conserver quand même ».",
      run: "<strong>À l'installation</strong> : cliquez sur « Informations complémentaires », puis « Exécuter quand même ».",
      note: "MathCursor est open source et sans danger. Cet avertissement disparaîtra quand l'installeur sera signé.",
      ok: "Compris",
      retry: "Le téléchargement ne démarre pas ?"
    },
    en: {
      title: "Your download has started",
      lead: "If Windows or Edge says the file “isn't commonly downloaded”:",
      edge: "<strong>In Edge</strong>: click “…” next to the file, then “Keep”, then “Show more” and “Keep anyway”.",
      run: "<strong>When installing</strong>: click “More info”, then “Run anyway”.",
      note: "MathCursor is open source and safe. This warning will go away once the installer is signed.",
      ok: "Got it",
      retry: "Download not starting?"
    }
  };

  let dialog = null;

  function build() {
    dialog = document.createElement('dialog');
    dialog.className = 'dl-help';
    dialog.setAttribute('aria-labelledby', 'dl-help-title');
    dialog.innerHTML =
      '<h3 id="dl-help-title"></h3><p class="dl-help-lead"></p>' +
      '<ol><li class="dl-help-edge"></li><li class="dl-help-run"></li></ol>' +
      '<p class="dl-help-note"></p>' +
      '<div class="dl-help-actions"><a class="dl-help-retry" download></a>' +
      '<button type="button" class="button button--primary" autofocus></button></div>';
    dialog.querySelector('button').addEventListener('click', () => dialog.close());
    // clic sur le fond : ferme
    dialog.addEventListener('click', e => { if (e.target === dialog) dialog.close(); });
    document.body.appendChild(dialog);
  }

  function open(href) {
    if (!dialog) build();
    const t = TEXT[document.documentElement.lang === 'en' ? 'en' : 'fr'];
    dialog.querySelector('h3').textContent = t.title;
    dialog.querySelector('.dl-help-lead').textContent = t.lead;
    dialog.querySelector('.dl-help-edge').innerHTML = t.edge;
    dialog.querySelector('.dl-help-run').innerHTML = t.run;
    dialog.querySelector('.dl-help-note').textContent = t.note;
    dialog.querySelector('button').textContent = t.ok;
    const retry = dialog.querySelector('.dl-help-retry');
    retry.textContent = t.retry;
    retry.href = href;
    if (typeof dialog.showModal === 'function') dialog.showModal();
  }

  document.addEventListener('click', e => {
    const a = e.target.closest('a[href$=".exe"]');
    if (!a || a.classList.contains('dl-help-retry')) return;
    // pas de preventDefault : le téléchargement part normalement
    setTimeout(() => open(a.getAttribute('href')), 0);
  });
})();
