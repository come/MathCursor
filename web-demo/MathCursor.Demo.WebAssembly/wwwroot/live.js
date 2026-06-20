/* MathCursor — démo « mode réel ».
   Éditeur contenteditable au fil de l'eau : on tape du texte libre, la zone maths
   est détectée AUTOUR du curseur (port JS de SpanComputer, fidèle au Ctrl+Espace
   produit), une popup s'affiche au caret, et valider insère le rendu KaTeX figé.
   Le pont moteur (Bridge.Analyze) est réutilisé tel quel depuis la démo simple. */
(async () => {
  const editor = document.getElementById('editor');
  const popup = document.getElementById('caret-popup');
  const loading = document.getElementById('loading');
  const SC = window.SpanComputer;
  const SENTINEL = '￼'; // place-tient d'une équation figée (rôle OMath Word)

  const t = (key, fallback) => {
    const r = window.I18N_RUNTIME;
    return (r && r[key]) || fallback;
  };

  // Démarre Blazor (runtime .NET WASM + MathCursor.Engine).
  try {
    await Blazor.start();
  } catch (e) {
    loading.innerHTML = `<div class="loading-text">${t('runtime_load_failed', 'Échec du chargement du moteur. Recharge la page ?')}</div>`;
    console.error(e);
    return;
  }
  loading.style.display = 'none';

  const ASSEMBLY = 'MathCursor.Demo.WebAssembly';
  let mathCulture = 'fr';
  let timer = null;

  // État de la zone/popup courante (figé au moment de l'analyse, pour que les
  // clics/raccourcis valident la bonne zone même si la sélection bouge).
  let state = null; // { line, start, end, candidates, sel }

  // ── Structure éditeur : une ligne = un <div class="ln">. ──────────────────

  function ensureNonEmpty() {
    if (!editor.querySelector('.ln')) {
      const ln = document.createElement('div');
      ln.className = 'ln';
      ln.appendChild(document.createElement('br'));
      editor.appendChild(ln);
    }
  }

  function refreshEmptyState() {
    const text = editor.textContent.trim();
    editor.classList.toggle('is-empty', text === '' && !editor.querySelector('.eq'));
  }

  function currentLine() {
    const sel = window.getSelection();
    if (!sel || !sel.focusNode) return null;
    let n = sel.focusNode;
    if (n === editor) {
      const idx = Math.min(sel.focusOffset, editor.childNodes.length - 1);
      n = editor.childNodes[Math.max(0, idx)] || null;
    }
    while (n && n !== editor) {
      if (n.nodeType === Node.ELEMENT_NODE && n.classList && n.classList.contains('ln')) return n;
      n = n.parentNode;
    }
    return null;
  }

  function childIndex(parent, child) {
    return Array.prototype.indexOf.call(parent.childNodes, child);
  }

  // Lit la ligne courante → { text, caret, omath } pour SpanComputer.
  // Les équations figées (.eq, contenteditable=false) deviennent un caractère
  // sentinelle + une région (borne dure, comme les OMaths Word).
  function readBlock(line, sel) {
    let text = '';
    let caret = null;
    const omath = [];
    const kids = line.childNodes;

    for (let i = 0; i < kids.length; i++) {
      const node = kids[i];

      if (caret === null && sel && sel.focusNode === line && sel.focusOffset === i) {
        caret = text.length;
      }

      if (node.nodeType === Node.TEXT_NODE) {
        const tlen = node.textContent.length;
        if (caret === null && sel && sel.focusNode === node) {
          caret = text.length + Math.min(sel.focusOffset, tlen);
        }
        text += node.textContent;
      } else if (node.nodeType === Node.ELEMENT_NODE && node.classList.contains('eq')) {
        const start = text.length;
        text += SENTINEL;
        omath.push({ start, end: text.length });
        if (caret === null && sel && (node === sel.focusNode || node.contains(sel.focusNode))) {
          caret = text.length;
        }
      } else if (node.nodeName === 'BR') {
        // ligne vide : ignoré (pas de contribution texte)
      } else {
        const piece = node.textContent || '';
        if (caret === null && sel && (node === sel.focusNode || node.contains(sel.focusNode))) {
          caret = text.length + piece.length;
        }
        text += piece;
      }
    }

    if (caret === null && sel && sel.focusNode === line && sel.focusOffset === kids.length) {
      caret = text.length;
    }
    if (caret === null) caret = text.length;
    return { text, caret, omath };
  }

  // offset (dans le texte reconstruit) → point DOM { node, offset } dans la ligne.
  function domPointAt(line, offset) {
    let acc = 0;
    const kids = line.childNodes;
    for (let i = 0; i < kids.length; i++) {
      const node = kids[i];
      if (node.nodeType === Node.TEXT_NODE) {
        const len = node.textContent.length;
        if (offset <= acc + len) return { node, offset: offset - acc };
        acc += len;
      } else if (node.nodeType === Node.ELEMENT_NODE && node.classList.contains('eq')) {
        if (offset <= acc) return { node: line, offset: i };
        acc += 1; // sentinelle
        if (offset <= acc) return { node: line, offset: i + 1 };
      } else if (node.nodeName === 'BR') {
        if (offset <= acc) return { node: line, offset: i };
      } else {
        const len = (node.textContent || '').length;
        if (offset <= acc + len) return { node: line, offset: i };
        acc += len;
      }
    }
    return { node: line, offset: kids.length };
  }

  // ── Popup ─────────────────────────────────────────────────────────────────

  function hidePopup() {
    popup.classList.remove('visible');
    state = null;
  }

  function renderLatexInto(target, tex) {
    try {
      katex.render(tex, target, { throwOnError: false, displayMode: false });
    } catch (e) {
      target.textContent = tex;
    }
  }

  function buildPopup(result, navIndex) {
    popup.innerHTML = '';
    const cands = result.candidates || [];

    cands.forEach((c, i) => {
      const card = document.createElement('div');
      card.className = 'cand' + (i === 0 ? ' cand-sel' : '') + (i === navIndex ? ' nav-active' : '');

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

      // mousedown preventDefault : garder le focus éditeur, puis valider au clic.
      card.addEventListener('mousedown', (e) => e.preventDefault());
      card.addEventListener('click', () => commit(i));

      popup.appendChild(card);
    });

    if (result.hasNote) {
      const note = document.createElement('div');
      note.className = 'popup-note';
      note.textContent = 'ⓘ ' + t('runtime_note', 'Expression dense — ajoute des espaces ou des parenthèses pour préciser.');
      popup.appendChild(note);
    }
  }

  function positionPopupAt(line, offset) {
    const pt = domPointAt(line, offset);
    const r = document.createRange();
    try {
      r.setStart(pt.node, pt.offset);
      r.collapse(true);
    } catch (e) {
      return;
    }
    let rect = r.getBoundingClientRect();
    if ((!rect || (rect.left === 0 && rect.top === 0)) && window.getSelection().rangeCount) {
      rect = window.getSelection().getRangeAt(0).getBoundingClientRect();
    }
    popup.style.left = Math.round(rect.left + window.scrollX) + 'px';
    popup.style.top = Math.round(rect.bottom + window.scrollY + 6) + 'px';
  }

  // ── Pipeline live ───────────────────────────────────────────────────────

  async function analyze() {
    if (document.activeElement !== editor) return;
    const sel = window.getSelection();
    if (!sel || !sel.rangeCount) { hidePopup(); return; }

    const line = currentLine();
    if (!line) { hidePopup(); return; }

    const block = readBlock(line, sel);
    // Set réduit (sans = < >) : on veut l'équation entière, pas un côté du =.
    const zone = SC.computeZone(block.text, block.caret, block.omath, SC.DEMO_DELIMITERS);
    const zoneText = block.text.substring(zone.start, zone.end).replace(/￼/g, '').trim();

    if (!zoneText) { hidePopup(); return; }

    let result;
    try {
      result = await DotNet.invokeMethodAsync(ASSEMBLY, 'Analyze', zoneText, mathCulture);
    } catch (e) {
      console.error(e);
      hidePopup();
      return;
    }

    const cands = (result && result.candidates) || [];
    if (!result || result.decision === 'erreur' || cands.length === 0) { hidePopup(); return; }

    // Préservation du nav-index si la zone n'a pas changé (anti-flicker, produit).
    let navIndex = 0;
    if (state && state.zoneText === zoneText) {
      navIndex = Math.min(state.navIndex, cands.length - 1);
    }

    state = {
      line: line,
      start: zone.start,
      end: zone.end,
      zoneText: zoneText,
      candidates: cands,
      navIndex: navIndex
    };

    buildPopup(result, navIndex);
    popup.classList.add('visible');
    positionPopupAt(line, zone.start);
  }

  function scheduleAnalyze() {
    refreshEmptyState();
    clearTimeout(timer);
    timer = setTimeout(analyze, 150);
  }

  // ── Validation (commit) ───────────────────────────────────────────────────

  function commit(index) {
    if (!state) return;
    const cand = state.candidates[index != null ? index : state.navIndex];
    if (!cand) return;

    const line = state.line;
    const a = domPointAt(line, state.start);
    const b = domPointAt(line, state.end);

    const range = document.createRange();
    try {
      range.setStart(a.node, a.offset);
      range.setEnd(b.node, b.offset);
    } catch (e) {
      hidePopup();
      return;
    }
    range.deleteContents();

    const eq = document.createElement('span');
    eq.className = 'eq';
    eq.contentEditable = 'false';
    eq.dataset.latex = cand.latex;
    renderLatexInto(eq, cand.latex);
    range.insertNode(eq);

    // Espace après l'équation → point d'atterrissage du caret + séparateur.
    const space = document.createTextNode(' ');
    if (eq.nextSibling) eq.parentNode.insertBefore(space, eq.nextSibling);
    else eq.parentNode.appendChild(space);

    const sel = window.getSelection();
    const after = document.createRange();
    after.setStart(space, space.length);
    after.collapse(true);
    sel.removeAllRanges();
    sel.addRange(after);

    hidePopup();
    refreshEmptyState();
    editor.focus();
  }

  // ── Entrée = nouvelle ligne (jamais submit, jamais valider). ──────────────

  function insertNewline() {
    const sel = window.getSelection();
    if (!sel || !sel.rangeCount) return;
    const line = currentLine();
    if (!line) return;

    const caretRange = sel.getRangeAt(0);
    const tail = document.createRange();
    tail.setStart(caretRange.endContainer, caretRange.endOffset);
    tail.setEnd(line, line.childNodes.length);
    const frag = tail.extractContents();

    const newLine = document.createElement('div');
    newLine.className = 'ln';
    if (frag.childNodes.length) newLine.appendChild(frag);
    else newLine.appendChild(document.createElement('br'));
    line.after(newLine);

    if (!line.childNodes.length) line.appendChild(document.createElement('br'));

    const r = document.createRange();
    r.setStart(newLine, 0);
    r.collapse(true);
    sel.removeAllRanges();
    sel.addRange(r);

    hidePopup();
    refreshEmptyState();
  }

  // ── Clavier ────────────────────────────────────────────────────────────

  editor.addEventListener('keydown', (e) => {
    const open = popup.classList.contains('visible') && state;

    if (e.key === 'Enter') {
      e.preventDefault();
      // Popup ouverte → Entrée valide le choix courant. Sinon → nouvelle ligne.
      // (Échap d'abord si on veut un saut de ligne sans convertir.)
      if (open) commit(state.navIndex);
      else insertNewline();
      return;
    }
    if (e.key === 'Escape') {
      if (open) { e.preventDefault(); hidePopup(); }
      return;
    }
    if (!open) return;

    if (e.key === 'Tab') {
      e.preventDefault();
      commit(state.navIndex);
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      state.navIndex = Math.min(state.navIndex + 1, state.candidates.length - 1);
      Array.prototype.forEach.call(popup.querySelectorAll('.cand'), (c, i) =>
        c.classList.toggle('nav-active', i === state.navIndex));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      state.navIndex = Math.max(state.navIndex - 1, 0);
      Array.prototype.forEach.call(popup.querySelectorAll('.cand'), (c, i) =>
        c.classList.toggle('nav-active', i === state.navIndex));
    }
  });

  editor.addEventListener('input', scheduleAnalyze);
  // selectionchange : déplacements caret (clic, flèches hors popup).
  document.addEventListener('selectionchange', () => {
    if (document.activeElement === editor) scheduleAnalyze();
  });
  editor.addEventListener('blur', () => {
    // léger délai : laisser un éventuel clic popup se déclencher d'abord.
    setTimeout(() => { if (document.activeElement !== editor) hidePopup(); }, 120);
  });

  // ── Exemples : remplacent le contenu par une ligne, caret en fin. ─────────
  document.querySelectorAll('.examples button').forEach((b) => {
    b.addEventListener('click', () => {
      editor.innerHTML = '';
      const ln = document.createElement('div');
      ln.className = 'ln';
      ln.appendChild(document.createTextNode(b.dataset.ex));
      editor.appendChild(ln);
      editor.focus();
      const sel = window.getSelection();
      const r = document.createRange();
      r.selectNodeContents(ln);
      r.collapse(false);
      sel.removeAllRanges();
      sel.addRange(r);
      scheduleAnalyze();
    });
  });

  // ── Toggle FR/US math → re-analyse. ───────────────────────────────────────
  document.querySelectorAll('.math-culture button').forEach((btn) => {
    btn.addEventListener('click', () => {
      mathCulture = btn.dataset.mc;
      document.querySelectorAll('.math-culture button').forEach((x) =>
        x.classList.toggle('active', x === btn));
      editor.focus();
      scheduleAnalyze();
    });
  });

  ensureNonEmpty();
  refreshEmptyState();
})();
