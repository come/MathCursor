// Port JS STRICT de adapter-vsto/src/MathCursor/Host/SpanComputer.cs
// Calcul pur de la span maths autour du caret (chemin Ctrl+Espace du produit) :
// bornes = delimiteur de phrase / stopword / OMath (ici : zone deja rendue) /
// debut-fin de ¶. Parite verrouillee par spancomputer.test.js (cas portes de
// SpanComputerTests.cs). Toute modif ici DOIT rester synchro avec le C#.
//
// NB : ce chemin ne sert qu'au declenchement explicite. L'auto-detection produit
// passe par le NER (non embarque en WASM) ; la demo s'en tient au caret-span.
(function (root) {
  'use strict';

  // Stopwords FR (table portee telle quelle du C#). Comparaison insensible a la
  // casse -> on stocke en minuscules et on lowercase le mot teste.
  var STOPWORDS = new Set([
    'soit', 'soient', 'et', 'ou', 'donc', 'alors', 'avec', 'si', 'on',
    'car', 'mais', 'ainsi', 'puis', 'comme', 'tout', 'un', 'une',
    'le', 'la', 'les', 'des', 'du', 'de', 'pour', 'par', 'sur',
    'dans', 'au', 'aux'
  ]);

  // '!' VOLONTAIREMENT ABSENT : postfixe factoriel, pas une fin de phrase
  // (ADR 2026-06-18-Fix-input-autocorrect-fraction-factorial). ',' n'est pas un
  // delimiteur (seul ';' coupe hors groupe) — le code garde le test ';'/',' du C#.
  var SPAN_DELIMITERS = new Set(['.', ';', '?', '=', '<', '>', '\n', '\r']);

  // Set REDUIT pour la demo « mode reel » : on retire les relations = < > pour
  // que l'equation entiere (ex. f(x)=1/x) soit captee d'un coup, faute de NER
  // pour la detecter. Les bornes de phrase/structure (. ; ? saut de ligne) et
  // les stopwords restent actifs. Cf. ADR 2026-06-19-Feat-web-demo-real-mode-editor.
  var DEMO_DELIMITERS = new Set(['.', ';', '?', '\n', '\r']);

  // char.IsLetter (unicode) || apostrophe || tiret.
  function isWordChar(c) {
    return /\p{L}/u.test(c) || c === "'" || c === '-';
  }

  function isWhitespace(c) {
    return /\s/.test(c);
  }

  // Position de l'ouvrante ( ou [ NON fermee qui englobe le caret (groupe en
  // cours de frappe), ou -1. Ne traverse pas un saut de ligne. Le '.' n'arrete
  // PAS le scan (separateur decimal). Cf. EnclosingOpenBracket (C#).
  function enclosingOpenBracket(text, caret) {
    var depth = 0;
    for (var k = caret - 1; k >= 0; k--) {
      var c = text[k];
      if (c === '\n' || c === '\r') return -1;
      if (c === ')' || c === ']') { depth++; continue; }
      if (c === '(' || c === '[') {
        if (depth > 0) { depth--; continue; }
        return k;
      }
    }
    return -1;
  }

  // Nombre d'ouvrantes ( / [ NON fermees avant le caret (remis a zero a chaque
  // saut de ligne). Init de la marche avant de computeSpanEnd. Cf. OpenDepthBehind.
  function openDepthBehind(text, caret) {
    var parenOpen = 0, bracketOpen = 0;
    var n = Math.min(caret, text.length);
    for (var k = 0; k < n; k++) {
      var c = text[k];
      if (c === '\n' || c === '\r') { parenOpen = 0; bracketOpen = 0; continue; }
      if (c === '(') parenOpen++;
      else if (c === ')') { if (parenOpen > 0) parenOpen--; }
      else if (c === '[') bracketOpen++;
      else if (c === ']') { if (bracketOpen > 0) bracketOpen--; }
    }
    return { parenOpen: parenOpen, bracketOpen: bracketOpen };
  }

  // omathRegions : tableau de { start, end } (zones deja rendues = bornes dures,
  // role des OMaths Word). Peut etre null/[].
  // delims : Set de delimiteurs (defaut = SPAN_DELIMITERS, = parite C#). La demo
  // « mode reel » passe un set REDUIT (sans = < >) : sans NER pour detecter
  // l'equation entiere, on ne veut pas que les relations coupent la zone.
  function computeSpanStart(text, caret, omathRegions, delims) {
    delims = delims || SPAN_DELIMITERS;
    var start = 0;

    var openBracket = enclosingOpenBracket(text, caret);
    if (openBracket >= 0) {
      start = openBracket;
    } else {
      var bracketDepth = 0, parenDepth = 0;
      for (var k = caret - 1; k >= 0; k--) {
        var c = text[k];
        if (c === ']') { bracketDepth++; continue; }
        if (c === '[') { if (bracketDepth > 0) bracketDepth--; continue; }
        if (c === ')') { parenDepth++; continue; }
        if (c === '(') { if (parenDepth > 0) parenDepth--; continue; }

        if (!delims.has(c)) continue;
        if ((c === ';' || c === ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
        start = Math.max(start, k + 1);
        break;
      }
    }

    if (omathRegions) {
      for (var r = 0; r < omathRegions.length; r++) {
        if (omathRegions[r].end <= caret) start = Math.max(start, omathRegions[r].end);
      }
    }

    var i = caret - 1;
    while (i >= start) {
      while (i >= start && isWhitespace(text[i])) i--;
      if (i < start) break;
      var wordEnd = i + 1;
      while (i >= start && isWordChar(text[i])) i--;
      var wordStart = i + 1;
      if (wordEnd <= wordStart) { i--; continue; }
      var w = text.substring(wordStart, wordEnd);
      if (STOPWORDS.has(w.toLowerCase())) { start = wordEnd; break; }
    }

    return start;
  }

  function computeSpanEnd(text, caret, omathRegions, delims) {
    delims = delims || SPAN_DELIMITERS;
    var end = text.length;
    while (end > caret && (text[end - 1] === '\r' || text[end - 1] === '\n')) end--;

    var depth = openDepthBehind(text, caret);
    var parenDepth = depth.parenOpen, bracketDepth = depth.bracketOpen;
    for (var k = caret; k < end; k++) {
      var c = text[k];
      if (c === '[') { bracketDepth++; continue; }
      if (c === ']') { if (bracketDepth > 0) bracketDepth--; continue; }
      if (c === '(') { parenDepth++; continue; }
      if (c === ')') { if (parenDepth > 0) parenDepth--; continue; }

      if (!delims.has(c)) continue;
      if ((c === ';' || c === ',') && (bracketDepth > 0 || parenDepth > 0)) continue;
      end = k;
      break;
    }

    if (omathRegions) {
      for (var r = 0; r < omathRegions.length; r++) {
        var s = omathRegions[r].start;
        if (s >= caret && s < end) end = s;
      }
    }

    var i = caret;
    while (i < end) {
      while (i < end && isWhitespace(text[i])) i++;
      if (i >= end) break;
      var wordStart = i;
      while (i < end && isWordChar(text[i])) i++;
      var wordEnd = i;
      if (wordEnd <= wordStart) { i++; continue; }
      var w = text.substring(wordStart, wordEnd);
      if (STOPWORDS.has(w.toLowerCase())) { end = wordStart; break; }
    }

    return end;
  }

  // Reproduit le flux de ConversionController.Trigger : span brute puis trim des
  // blancs aux deux bouts. Retourne { start, end } (offsets dans text).
  function computeZone(text, caret, omathRegions, delims) {
    var s = computeSpanStart(text, caret, omathRegions, delims);
    var e = computeSpanEnd(text, caret, omathRegions, delims);
    while (s < e && isWhitespace(text[s])) s++;
    while (e > s && isWhitespace(text[e - 1])) e--;
    return { start: s, end: e };
  }

  var api = {
    computeSpanStart: computeSpanStart,
    computeSpanEnd: computeSpanEnd,
    computeZone: computeZone,
    enclosingOpenBracket: enclosingOpenBracket,
    openDepthBehind: openDepthBehind,
    isWordChar: isWordChar,
    STOPWORDS: STOPWORDS,
    SPAN_DELIMITERS: SPAN_DELIMITERS,
    DEMO_DELIMITERS: DEMO_DELIMITERS
  };

  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  if (root) root.SpanComputer = api;
})(typeof window !== 'undefined' ? window : null);
