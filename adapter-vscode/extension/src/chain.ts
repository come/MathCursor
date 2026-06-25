// Couche « chaînes d'équations » côté VSCode — détection de ligne-relation
// (port de rust/mc-engine chain.rs / RelationMarkers.cs). La composition (le
// bloc aligné) vit dans le cœur Rust (verbe COMPOSE) ; ici on ne fait que
// reconnaître qu'une ligne commence par un marqueur (=, <=>, ≤…) et isoler le
// RESTE (le moteur renvoie « erreur » sur une relation en tête).

export interface RelMatch {
  typed: string;
  markerLatex: string;   // LaTeX d'affichage (préfixé dans la popup)
  isConnector: boolean;  // ⟺/⟹ vs relation
  rest: string;          // l'expression à analyser
}

// (forme tapée, latex, connecteur?, marqueur-MOT?). Ordre = longueur décroissante.
const MARKERS: [string, string, boolean, boolean][] = [
  ['approx', '\\approx ', false, true],
  ['environ', '\\approx ', false, true],
  ['env', '\\approx ', false, true],
  ['<=>', '\\Leftrightarrow ', true, false],
  ['=>', '\\Rightarrow ', true, false],
  ['<=', '\\leq ', false, false],
  ['>=', '\\geq ', false, false],
  ['!=', '\\neq ', false, false],
  ['⟺', '\\Leftrightarrow ', true, false], // ⟺
  ['⇔', '\\Leftrightarrow ', true, false], // ⇔
  ['⟹', '\\Rightarrow ', true, false],     // ⟹
  ['⇒', '\\Rightarrow ', true, false],     // ⇒
  ['≤', '\\leq ', false, false],           // ≤
  ['≥', '\\geq ', false, false],           // ≥
  ['≠', '\\neq ', false, false],           // ≠
  ['≈', '\\approx ', false, false],        // ≈
  ['=', '=', false, false],
  ['<', '<', false, false],
  ['>', '>', false, false],
];

const isAlpha = (c: string): boolean => /[^\W\d_]/u.test(c);

/** `undefined` si la ligne ne commence pas par un marqueur de chaîne. */
export function detectRelationLine(line: string): RelMatch | undefined {
  const s = line.replace(/^\s+/, '');
  if (!s) { return undefined; }
  for (const [typed, markerLatex, isConnector, isWord] of MARKERS) {
    const head = s.slice(0, typed.length);
    if (isWord) {
      if (head.toLowerCase() !== typed.toLowerCase()) { continue; }
      const next = s.charAt(typed.length);
      if (next && isAlpha(next)) { continue; } // « approximation » ≠ « approx »
    } else if (head !== typed) {
      continue;
    }
    const rest = s.slice(typed.length).replace(/^ +/, '').replace(/[\r\n]+$/, '');
    return { typed, markerLatex, isConnector, rest };
  }
  return undefined;
}
