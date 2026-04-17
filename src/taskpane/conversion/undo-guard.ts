// Anti-boucle Ctrl+Z : quand on convertit "f(x)=1/x\t" → OMath, puis l'utilisateur
// fait Ctrl+Z, Word restaure "f(x)=1/x\t" dans le paragraphe. Sans protection, le
// fastTick suivant verrait à nouveau le tab et relancerait la conversion → loop.
//
// Stratégie : on marque le texte converti. Le guard reste actif TANT QUE le texte
// pré-tab observé est égal à celui marqué. Dès que l'utilisateur édite (textBeforeTab
// différent), le guard est libéré et une nouvelle conversion devient possible.
// Pas de TTL : seule l'édition signale l'intention de re-convertir.

export class UndoGuard {
  private guardedText: string | null = null;

  mark(text: string): void {
    this.guardedText = text;
  }

  clear(): void {
    this.guardedText = null;
  }

  // À appeler quand un tab est détecté, avec le texte avant le tab.
  // Retourne true si la conversion doit être skipée.
  // Side-effect : libère le guard si le texte a changé (= utilisateur a édité).
  shouldSkipAndUpdate(textBeforeTab: string): boolean {
    if (this.guardedText === null) return false;
    if (this.guardedText === textBeforeTab) return true; // undo artifact
    // Le texte avant tab a changé → utilisateur a édité → libérer
    this.guardedText = null;
    return false;
  }
}
