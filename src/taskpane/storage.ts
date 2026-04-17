// Stockage texte source dans document.settings (persiste avec le document)

export function storeSource(key: string, sourceText: string) {
  Office.context.document.settings.set(key, sourceText);
  Office.context.document.settings.saveAsync();
}

export function getSource(key: string): string | null {
  return Office.context.document.settings.get(key) as string | null;
}

export function deleteSource(key: string) {
  Office.context.document.settings.remove(key);
  Office.context.document.settings.saveAsync();
}
