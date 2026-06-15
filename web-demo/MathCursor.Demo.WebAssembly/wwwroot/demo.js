/* MathCursor — démo web. Pont JS vers le ForestEngine compilé en WASM.
   L'UI reproduit la popup produit : une ligne d'input, et dessous les cas
   reconnus (candidat 0 = présélectionné, suivants = alternatives). */
(async () => {
  const input = document.getElementById('input');
  const popup = document.getElementById('popup');
  const loading = document.getElementById('loading');

  // Démarre Blazor (runtime .NET WASM + MathCursor.Engine).
  try {
    await Blazor.start();
  } catch (e) {
    const t = (window.I18N_RUNTIME && window.I18N_RUNTIME.runtime_load_failed)
      || 'Échec du chargement du moteur. Recharge la page ?';
    loading.innerHTML = `<div class="loading-text">${t}</div>`;
    console.error(e);
    return;
  }
  loading.style.display = 'none';

  const ASSEMBLY = 'MathCursor.Demo.WebAssembly';
  let timer = null;
  let mathCulture = 'fr';
  let lastResult = null;

  const t = (key, fallback) => {
    const r = window.I18N_RUNTIME;
    return (r && r[key]) || fallback;
  };

  const renderLatexInto = (target, tex) => {
    try {
      katex.render(tex, target, { throwOnError: false, displayMode: true });
    } catch (e) {
      target.textContent = tex; // repli : LaTeX brut
    }
  };

  const renderEmpty = () => {
    popup.innerHTML = `<div class="popup-empty">${t('demo_empty', "Les conversions proposées s'afficheront ici…")}</div>`;
  };

  const renderResult = (result) => {
    lastResult = result;
    popup.innerHTML = '';

    if (!result) { renderEmpty(); return; }

    const cands = result.candidates || [];
    if (result.decision === 'erreur' || cands.length === 0) {
      popup.innerHTML = `<div class="popup-empty popup-err">${t('runtime_error', 'Pas reconnu comme une formule.')}</div>`;
      return;
    }

    // Bandeau de décision : auto (1 candidat validé d'office) vs popup (N choix).
    const head = document.createElement('div');
    head.className = 'popup-head';
    if (result.decision === 'auto') {
      head.innerHTML = `<span class="badge badge-auto">✓ ${t('runtime_auto', 'inséré automatiquement')}</span>`;
    } else {
      const word = cands.length > 1 ? t('runtime_popup_many', 'choix') : t('runtime_popup_one', 'choix');
      head.innerHTML = `<span class="badge badge-popup">${cands.length} ${word}</span>`;
    }
    popup.appendChild(head);

    cands.forEach((c, i) => {
      const card = document.createElement('div');
      card.className = 'cand' + (i === 0 ? ' cand-sel' : '');

      const main = document.createElement('div');
      main.className = 'cand-main';

      const render = document.createElement('div');
      render.className = 'cand-render';
      renderLatexInto(render, c.latex);
      main.appendChild(render);

      const src = document.createElement('code');
      src.className = 'cand-src';
      src.textContent = c.latex;
      main.appendChild(src);

      card.appendChild(main);

      const rank = document.createElement('div');
      rank.className = 'cand-rank' + (i === 0 ? ' cand-rank-sel' : '');
      rank.textContent = (i === 0)
        ? '★ ' + t('runtime_preselected', "sélectionné d'office")
        : t('runtime_alt', 'alternative') + ' ' + i;
      card.appendChild(rank);

      popup.appendChild(card);
    });

    if (result.hasNote) {
      const note = document.createElement('div');
      note.className = 'popup-note';
      note.textContent = 'ⓘ ' + t('runtime_note', 'Expression dense — ajoute des espaces ou des parenthèses pour préciser.');
      popup.appendChild(note);
    }
  };

  const analyze = async () => {
    const value = input.value;
    if (!value.trim()) { lastResult = null; renderEmpty(); return; }
    let result;
    try {
      result = await DotNet.invokeMethodAsync(ASSEMBLY, 'Analyze', value, mathCulture);
    } catch (e) {
      popup.innerHTML = `<div class="popup-empty popup-err">${t('runtime_engine_error', 'Erreur moteur — voir console.')}</div>`;
      console.error(e);
      return;
    }
    renderResult(result);
  };

  // Re-render avec le dernier résultat (changement de langue UI).
  window.demoRerender = () => { if (lastResult) renderResult(lastResult); else renderEmpty(); };

  input.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(analyze, 180);
  });

  // Toggle FR/US math → re-analyse (la culture change le résultat moteur).
  document.querySelectorAll('.math-culture button').forEach(btn => {
    btn.addEventListener('click', () => {
      mathCulture = btn.dataset.mc;
      document.querySelectorAll('.math-culture button').forEach(b => b.classList.toggle('active', b === btn));
      analyze();
    });
  });

  document.querySelectorAll('.examples button').forEach(b => {
    b.addEventListener('click', () => {
      input.value = b.dataset.ex;
      input.focus();
      analyze();
    });
  });

  analyze();
})();
