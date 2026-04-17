// Types partagés pour la conversion math

export interface DocChoice {
  label: string;
  display: string;
  replacement: string;
  ooxml?: string;
}

// AST Node types
export type N =
  | { k: "num"; v: string }
  | { k: "var"; v: string }
  | { k: "op"; op: string; left: N; right: N }
  | { k: "unary"; op: string; child: N }
  | { k: "frac"; num: N; den: N }
  | { k: "sup"; base: N; exp: N }
  | { k: "paren"; d: "(" | "["; inner: N }
  | { k: "juxt"; parts: N[] }
  | { k: "empty" };

// Token types
export type Tk = { t: "n" | "v" | "op" | "(" | ")" | "[" | "]"; v: string };
