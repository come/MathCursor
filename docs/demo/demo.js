/* MathCursor — démo web. Pont JS vers le moteur LatticeEngine compilé en WASM. */
(async () => {
  const ta = document.getElementById('ta');
  const preview = document.getElementById('preview');
  const latex = document.getElementById('latex');
  const alternatives = document.getElementById('alternatives');
  const loading = document.getElementById('loading');

  // Démarre Blazor (charge le runtime .NET en WASM + DLL core-csharp).
  try {
    await Blazor.start();
  } catch (e) {
    const t = (window.I18N_RUNTIME && window.I18N_RUNTIME.runtime_load_failed)
      || 'Échec chargement moteur. Recharge la page ?';
    loading.innerHTML = `<div class="loading-text">${t}</div>`;
    console.error(e);
    return;
  }
  loading.style.display = 'none';

  let timer = null;
  const ASSEMBLY = 'MathCursor.Demo.WebAssembly';

  // Étiquettes des règles d'ambiguïté — traduites en FR/EN.
  const RULE_LABELS = {
    fr: {
      'two-uppercase': '2 majuscules adjacentes',
      'three-uppercase': '3 majuscules adjacentes',
      'letter-sup-number': 'lettre + chiffre',
      'v-as-forall': 'V isolé',
      'e-as-exists': 'E isolé',
      'canonical-set': 'lettre canonique R/N/Z/Q/C',
    },
    en: {
      'two-uppercase': '2 adjacent uppercase letters',
      'three-uppercase': '3 adjacent uppercase letters',
      'letter-sup-number': 'letter + digit',
      'v-as-forall': 'isolated V',
      'e-as-exists': 'isolated E',
      'canonical-set': 'canonical letter R/N/Z/Q/C',
    },
  };

  const t = (key, fallback) => {
    const r = window.I18N_RUNTIME;
    return (r && r[key]) || fallback;
  };

  const ruleLabel = (rule) => {
    const lang = (document.documentElement.lang === 'en') ? 'en' : 'fr';
    const dict = RULE_LABELS[lang] || RULE_LABELS.fr;
    return dict[rule] || rule || (lang === 'en' ? 'other interpretation' : 'autre interprétation');
  };

  const renderEmpty = () => {
    preview.innerHTML = `<span class="empty">${t('demo_empty', 'Le rendu apparaîtra ici…')}</span>`;
    latex.textContent = '';
    alternatives.innerHTML = '';
  };

  const renderLatexInto = (target, tex) => {
    try {
      katex.render(tex, target, { throwOnError: false, displayMode: true });
    } catch (e) {
      target.textContent = tex; // fallback : LaTeX brut
    }
  };

  // Cache du dernier input pour permettre re-render quand la langue change.
  let lastResult = null;

  const renderResult = (result) => {
    lastResult = result;
    if (!result || !result.top) {
      preview.innerHTML = `<span class="empty">${t('runtime_no_conversion', '(aucune conversion proposée)')}</span>`;
      latex.textContent = '';
      alternatives.innerHTML = '';
      return;
    }

    latex.textContent = result.top;
    renderLatexInto(preview, result.top);

    alternatives.innerHTML = '';
    const alts = (result.alternatives || []).filter(a => a && a !== result.top);
    if (alts.length === 0) return;

    const word = alts.length > 1
      ? t('runtime_alts_plural', 'alternatives')
      : t('runtime_alts', 'alternative');
    const header = document.createElement('div');
    header.className = 'alt-header';
    header.innerHTML = `<span class="alt-count">${alts.length} ${word}</span> · <span class="alt-rule">${ruleLabel(result.rule)}</span>`;
    alternatives.appendChild(header);

    alts.forEach(tex => {
      const card = document.createElement('div');
      card.className = 'alt-card';

      const renderBox = document.createElement('div');
      renderBox.className = 'alt-render';
      renderLatexInto(renderBox, tex);
      card.appendChild(renderBox);

      const src = document.createElement('code');
      src.className = 'alt-source';
      src.textContent = tex;
      card.appendChild(src);

      alternatives.appendChild(card);
    });
  };

  const render = async () => {
    const input = ta.value;
    if (!input.trim()) {
      lastResult = null;
      renderEmpty();
      return;
    }
    let result;
    try {
      result = await DotNet.invokeMethodAsync(ASSEMBLY, 'ConvertRich', input);
    } catch (e) {
      preview.innerHTML = `<span class="error">${t('runtime_engine_error', 'Erreur moteur — voir console')}</span>`;
      latex.textContent = String(e);
      alternatives.innerHTML = '';
      console.error(e);
      return;
    }
    renderResult(result);
  };

  // Exposé au switcher de langue : re-render avec le dernier résultat
  // mais en lisant les strings runtime de la nouvelle langue.
  window.demoRerender = () => {
    if (lastResult) renderResult(lastResult);
    else renderEmpty();
  };

  ta.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(render, 200);
  });

  document.querySelectorAll('.examples button').forEach(b => {
    b.addEventListener('click', () => {
      ta.value = b.dataset.ex;
      ta.focus();
      render();
    });
  });

  render();
})();
