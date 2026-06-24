import * as vscode from 'vscode';
import { analyze, disposeEngine } from './engine';
import { PopupController, isHelperAvailable } from './popup';
import { resolveSource, lineMasked, containsMath, Source } from './detect';
import { NerController } from './ner';
import { ensurePackages } from './packages';

// Host VSCode (= L6). Modèle « façon Word » :
//  - popup native PERSISTANTE au caret (helper WPF), qui VIT tant qu'on est dans
//    une zone math et se RAFRAÎCHIT sur frappe / clic (re-détection SpanComputer
//    + re-analyse + re-position) ;
//  - manuel : Ctrl+Alt+Espace force l'affichage.
// Hors Windows : repli sur la complétion native. Cf. ADR 2026-06-23-Feat-vscode-host.

const LANGS = ['latex', 'tex', 'markdown'];
const REFRESH_DEBOUNCE_MS = 120;

interface Current { range: vscode.Range; candidates: string[]; src: string; }

export function activate(context: vscode.ExtensionContext): void {
  let current: Current | undefined;
  let dismissedSrc: string | undefined;
  let lastAnchorKey: string | undefined;   // identité de zone (ligne:colDébut) → fige l'ancrage
  let timer: ReturnType<typeof setTimeout> | undefined;
  let refreshGen = 0;                       // jeton anti-réentrance (refresh async concurrent)
  let manualLevel = 0;                      // Ctrl+Espace répété → agrandit la zone
  let manualKey: string | undefined;        // position du dernier déclenchement manuel

  const popup = new PopupController(
    context,
    index => commit(index),
    () => { dismissedSrc = current?.src; }
  );
  const ner = new NerController(context);
  ner.warmup(); // charge le modèle en avance (≈1,1 s) → prêt quand l'élève tape
  context.subscriptions.push(
    { dispose: () => popup.dispose() },
    { dispose: () => ner.dispose() },
    { dispose: () => disposeEngine() }
  );

  async function commit(index: number): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !current || !Number.isInteger(index)
      || index < 0 || index >= current.candidates.length) { return; }
    const cur = current;
    current = undefined;
    // Revalide que la zone n'a pas bougé depuis l'affichage (frappe/undo/édition
    // ailleurs via le hook clavier async) → sinon on remplacerait la mauvaise plage.
    if (editor.document.getText(cur.range).trim() !== cur.src) { return; }
    const cfg = vscode.workspace.getConfiguration('mathcursor');
    const inserted = wrapFor(cur.candidates[index], editor.document, cur.range,
      cfg.get<string>('delimiters', 'auto'), cfg.get<string>('inlineDisplaystyle', 'auto'));
    await editor.edit(b => b.replace(cur.range, inserted));
    // Préambule : ajoute amsmath/amssymb si nécessaire (\mathbb, matrices…).
    if (cfg.get<boolean>('autoPackages', true)) { await ensurePackages(editor); }
  }

  // Ctrl+Espace (forçage) : appuis répétés au MÊME endroit → on agrandit la zone
  // d'un mot vers la gauche à chaque fois (comme l'expansion de zone de Word).
  function runManual(): void {
    const editor = vscode.window.activeTextEditor;
    if (!editor) { return; }
    const p = editor.selection.active;
    const key = `${editor.document.uri.toString()}:${p.line}:${p.character}`;
    if (key === manualKey) { manualLevel++; } else { manualKey = key; manualLevel = 0; }
    dismissedSrc = undefined;
    refresh(true, manualLevel);
  }

  async function refresh(force: boolean, level = 0): Promise<void> {
    const editor = vscode.window.activeTextEditor;
    if (!editor || !LANGS.includes(editor.document.languageId)) { popup.hide(); current = undefined; return; }
    // Multi-curseur non géré (un seul remplacement) → on s'abstient proprement.
    if (editor.selections.length > 1) { popup.hide(); current = undefined; return; }

    if (!isHelperAvailable()) {
      if (force) { await vscode.commands.executeCommand('editor.action.triggerSuggest'); }
      return;
    }

    const cfg = vscode.workspace.getConfiguration('mathcursor');
    if (!force && !cfg.get<boolean>('autoDetect', true)) { popup.hide(); return; }

    // Anti-réentrance : si un refresh plus récent démarre pendant nos await, on
    // abandonne (sinon candidats/ancrage/current d'une zone écrasés par une autre).
    const myGen = ++refreshGen;

    const found = await resolveSrc(ner, editor, force, level);
    if (myGen !== refreshGen) { return; }
    if (!found) { popup.hide(); current = undefined; return; }

    if (found.src !== dismissedSrc) { dismissedSrc = undefined; }
    if (!force && dismissedSrc === found.src) { return; } // dismissé → reste fermé

    let result;
    // Espace final = signal moteur « signe postfixe » (R* → R^{\ast}, lim x 0+ →
    // 0⁺), comme la détection live de Word. La zone remplacée, elle, reste trimmée.
    try { result = await analyze(found.src + ' ', cfg.get<string>('culture', 'fr')); }
    catch { return; }
    if (myGen !== refreshGen) { return; }

    const candidates = (result.ranked ?? []).slice(0, cfg.get<number>('maxCandidates', 3)).map(c => c.latex);
    if (result.decision === 'erreur' || candidates.length === 0) {
      popup.hide(); current = undefined;
      if (force) { vscode.window.setStatusBarMessage('MathCursor : rien à convertir', 1500); }
      return;
    }

    current = { range: found.range, candidates, src: found.src };

    // Ancrage au DÉBUT du texte reconnu : la coquille lit le caret (MSAA) et
    // recule de colDelta caractères. Figé tant que la zone ne change pas.
    const caret = editor.selection.active;
    const colDelta = found.range.start.line === caret.line
      ? Math.max(0, caret.character - found.range.start.character) : 0;
    const fontSize = vscode.workspace.getConfiguration('editor').get<number>('fontSize', 14);
    const startKey = `${found.range.start.line}:${found.range.start.character}`;
    const reposition = startKey !== lastAnchorKey || !popup.isShown;
    lastAnchorKey = startKey;
    popup.show({ candidates, colDelta, fontSize, reposition });
  }

  function scheduleRefresh(): void {
    if (timer) { clearTimeout(timer); }
    timer = setTimeout(() => refresh(false), REFRESH_DEBOUNCE_MS);
  }

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider(
      LANGS.map(language => ({ language })),
      new MathCursorCompletionProvider()
    ),
    vscode.commands.registerCommand('mathcursor.convert', () =>
      vscode.commands.executeCommand('editor.action.triggerSuggest')
    ),
    vscode.commands.registerCommand('mathcursor.popup', () => runManual()),
    vscode.workspace.onDidChangeTextDocument(e => {
      const ed = vscode.window.activeTextEditor;
      if (!ed || e.document !== ed.document || e.contentChanges.length === 0) { return; }
      // Toute édition à/avant la fin de la zone décale les offsets → on invalide
      // `current` pour qu'un commit (hook clavier async) ne remplace pas la
      // mauvaise plage. Le refresh debouncé reconstruira la zone.
      if (current && e.contentChanges.some(c => c.range.start.isBeforeOrEqual(current!.range.end))) {
        current = undefined;
      }
      scheduleRefresh();
    }),
    vscode.window.onDidChangeTextEditorSelection(() => scheduleRefresh()),
    vscode.window.onDidChangeActiveTextEditor(() => { popup.hide(); current = undefined; })
  );
}

// Source à convertir : sélection (manuel) > NER (détecteur primaire) >
// SpanComputer (repli uniquement si le NER est indisponible : non-Windows /
// modèle absent). Pas de double-détection.
async function resolveSrc(
  ner: NerController,
  editor: vscode.TextEditor,
  force: boolean,
  level = 0
): Promise<Source | undefined> {
  const document = editor.document;
  const pos = editor.selection.active;

  if (force && !editor.selection.isEmpty) {
    // Sélection mono-ligne seulement (le positionnement/expansion sont mono-ligne).
    if (editor.selection.start.line !== editor.selection.end.line) { return undefined; }
    const range = new vscode.Range(editor.selection.start, editor.selection.end);
    return { src: document.getText(range).trim(), range };
  }

  let range: vscode.Range | undefined;
  if (ner.isAvailable) {
    // Texte MASQUÉ ($…$ déjà convertis → espaces) → le NER ne les re-propose pas.
    const z = await ner.detect(lineMasked(document, pos.line), pos.character);
    if (z && z.end > z.start) {
      range = new vscode.Range(pos.line, z.start, pos.line, z.end);
    } else {
      // NER muet (formule SEULE sur sa ligne : le modèle est entraîné pour la
      // prose, pas les formules isolées ; aussi en cas de timeout) → repli
      // heuristique SpanComputer. La prose reste filtrée par le moteur (erreur).
      range = resolveSource(document, pos, force)?.range;
    }
  } else {
    range = resolveSource(document, pos, force)?.range; // repli SpanComputer
  }
  if (!range) { return undefined; }

  if (level > 0) { range = expandLeft(document, range, level); }
  const src = document.getText(range).trim();
  if (!src || containsMath(src)) { return undefined; } // ne pas reconvertir du LaTeX
  return { src, range };
}

// Étend le DÉBUT de la zone de `level` mots vers la gauche (sur la même ligne).
// Sert au cycle Ctrl+Espace « agrandir la zone ».
function expandLeft(document: vscode.TextDocument, range: vscode.Range, level: number): vscode.Range {
  const text = document.lineAt(range.start.line).text;
  let col = range.start.character;
  for (let k = 0; k < level; k++) {
    while (col > 0 && /\s/.test(text[col - 1])) { col--; }
    while (col > 0 && !/\s/.test(text[col - 1])) { col--; }
  }
  return new vscode.Range(range.start.line, col, range.end.line, range.end.character);
}

// Complétion native = REPLI hors Windows (sur Windows c'est la popup WPF).
class MathCursorCompletionProvider implements vscode.CompletionItemProvider {
  async provideCompletionItems(
    document: vscode.TextDocument,
    position: vscode.Position,
    token: vscode.CancellationToken,
    ctx: vscode.CompletionContext
  ): Promise<vscode.CompletionItem[] | undefined> {
    if (isHelperAvailable()) { return undefined; }

    const cfg = vscode.workspace.getConfiguration('mathcursor');
    const manual = ctx.triggerKind === vscode.CompletionTriggerKind.Invoke;
    if (!manual && !cfg.get<boolean>('autoDetect', true)) { return undefined; }

    const found = resolveSource(document, position, manual);
    if (!found) { return undefined; }

    let result;
    try { result = await analyze(found.src + ' ', cfg.get<string>('culture', 'fr')); }
    catch { return undefined; }
    if (token.isCancellationRequested || result.decision === 'erreur' || !result.ranked?.length) {
      return undefined;
    }

    const delimiters = cfg.get<string>('delimiters', 'auto');
    const inlineMode = cfg.get<string>('inlineDisplaystyle', 'auto');
    const max = cfg.get<number>('maxCandidates', 3);
    const rangeText = document.getText(found.range);
    const items: vscode.CompletionItem[] = [];
    const n = Math.min(max, result.ranked.length);
    for (let i = 0; i < n; i++) {
      const latex = result.ranked[i].latex;
      const inserted = wrapFor(latex, document, found.range, delimiters, inlineMode);
      const item = new vscode.CompletionItem(
        { label: latex, description: i === 0 ? 'MathCursor' : `MathCursor #${i + 1}` },
        vscode.CompletionItemKind.Snippet
      );
      item.insertText = inserted;
      item.range = found.range;
      item.filterText = rangeText;
      item.sortText = String(i).padStart(3, '0');
      item.preselect = i === 0;
      item.detail = inserted;
      item.documentation = buildDocs(latex, inserted);
      items.push(item);
    }
    return items;
  }
}

function buildDocs(_latex: string, inserted: string): vscode.MarkdownString {
  // Repli complétion native (hors Windows) : LaTeX en bloc, sans aperçu image
  // (le rendu SVG MathJax a été retiré — la popup WPF→Rust est l'UI principale).
  const md = new vscode.MarkdownString();
  md.appendCodeblock(inserted, 'latex');
  return md;
}

// Délimiteurs : 'auto' = contextuel (formule seule sur sa ligne → display centré,
// sinon inline) + bon délimiteur selon le langage (LaTeX \[ \] / Markdown $$ $$).
// Sinon mode forcé.
// Constructions qui rendent NETTEMENT différemment en displaystyle (et méritent
// donc \displaystyle inline) : fractions, grands opérateurs, lim, binom. Pour le
// reste (x^2+3x, x_i…) le textstyle suffit → pas de pollution.
function needsDisplay(latex: string): boolean {
  return /\\(d?frac|tfrac|sum|prod|coprod|o?int|iint|iiint|bigcup|bigcap|bigsqcup|bigoplus|bigotimes|bigodot|lim|binom)\b/.test(latex);
}

function wrapFor(
  latex: string,
  document: vscode.TextDocument,
  range: vscode.Range,
  setting: string,
  inlineMode: string
): string {
  // En inline, \displaystyle rend fractions/sommes à pleine taille. En 'auto' on
  // ne l'applique QUE si la formule en a besoin (heuristique needsDisplay).
  const big = inlineMode === 'always' || (inlineMode === 'auto' && needsDisplay(latex));
  const body = big ? `\\displaystyle ${latex}` : latex;
  if (setting === 'none') { return latex; }
  if (setting === 'paren') { return `\\(${body}\\)`; }
  if (setting === 'inline') { return `$${body}$`; }

  let display: boolean;
  if (setting === 'display') {
    display = true;
  } else { // auto
    const line = document.lineAt(range.start.line);
    display = range.start.line === range.end.line
      && line.text.trim() === document.getText(range).trim();
  }
  if (!display) { return `$${body}$`; }
  return document.languageId === 'markdown' ? `$$${latex}$$` : `\\[${latex}\\]`;
}

export function deactivate(): void { /* no-op */ }
