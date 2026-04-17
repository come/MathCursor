// Types partagés pour la conversion math

// ============================================================
// TOKENS
// ============================================================

export type UnicodeCategory =
  | "letter"         // a-z, A-Z, lettres accentuées
  | "greekLetter"    // α β γ δ ... Ω
  | "digit"          // 0-9
  | "mathSymbol"     // ∫ ∑ ∏ ∂ ∇ ∞ ≤ ≥ ≠ ∈ ⊂ ∪ ∩ ±
  | "operator"       // + - * / ^ = < >
  | "paren"          // ( ) [ ]
  | "comma"          // ,
  | "dot"            // .
  | "whitespace"     // espaces, tabs
  | "unknown";

export interface Token {
  text: string;                       // texte original du token
  normalized: string;                 // texte normalisé (math italic → ASCII)
  start: number;                      // position dans le texte original
  end: number;                        // position fin (exclusive)
  categories: Set<UnicodeCategory>;   // catégories Unicode détectées
  mathiness: number;                  // score 0.0 → 1.0 (rempli par le scorer)
}

// ============================================================
// AST NODES
// ============================================================

export interface NumNode    { k: "num"; v: string }
export interface VarNode    { k: "var"; v: string }
export interface OpNode     { k: "op"; op: string; left: N; right: N }
export interface FracNode   { k: "frac"; num: N; den: N }
export interface SupNode    { k: "sup"; base: N; exp: N }
export interface ParenNode  { k: "paren"; d: "(" | "["; inner: N }
export interface JuxtNode   { k: "juxt"; parts: N[] }
export interface UnaryNode  { k: "unary"; op: string; child: N }
export interface EmptyNode  { k: "empty" }

// Futurs (ajoutés sans toucher aux existants)
export interface LimitNode    { k: "limit"; variable: string; target: N; body: N }
export interface SumNode      { k: "sum"; body: N; indexVar: string; lower: N; upper: N }
export interface ProductNode  { k: "product"; body: N; indexVar: string; lower: N; upper: N }
export interface IntegralNode { k: "integral"; body: N; variable: string; lower?: N; upper?: N }
export interface DerivNode    { k: "derivative"; body: N; variable: string; order: number }
export interface MatrixNode   { k: "matrix"; rows: N[][] }
export interface VecColNode   { k: "vecCol"; name: string; components: N[] }
export interface VecRowNode   { k: "vecRow"; name: string; components: N[] }

export type N =
  | NumNode | VarNode | OpNode | FracNode | SupNode
  | ParenNode | JuxtNode | UnaryNode | EmptyNode
  | LimitNode | SumNode | ProductNode | IntegralNode | DerivNode
  | MatrixNode | VecColNode | VecRowNode;

// ============================================================
// PARSER INTERFACE
// ============================================================

export interface RankedCandidate {
  ast: N;
  confidence: number;    // 0-1
  label: string;         // ex: "vecteur colonne", "fraction"
}

export interface IParser {
  parse(tokens: Token[]): N;
  parseCandidates(tokens: Token[]): RankedCandidate[];
}

// ============================================================
// MATH ZONE (détection frontière prose/math)
// ============================================================

// ============================================================
// COMPAT (ancien format, utilisé par le parser v1 et les symboles)
// ============================================================

export interface DocChoice {
  label: string;
  display: string;
  replacement: string;
  ooxml?: string;
}

// Ancien format token simplifié (pour le parser v1)
export type Tk = { t: "n" | "v" | "op" | "(" | ")" | "[" | "]"; v: string };

// ============================================================
// MATH ZONE
// ============================================================

export interface MathZone {
  startToken: number;    // index du premier token math
  endToken: number;      // index du dernier token math (inclusive)
  confidence: number;    // confiance globale de la zone
  tokens: Token[];       // les tokens de la zone
  raw: string;           // texte original de la zone
  normalized: string;    // texte normalisé
}
