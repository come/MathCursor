// OMath XML generation helpers

export const SETS: Record<string, string> = {
  R: "\u211D", N: "\u2115", Z: "\u2124", Q: "\u211A", C: "\u2102",
};

const RFonts = `<w:rFonts w:ascii="Cambria Math" w:hAnsi="Cambria Math"/>`;
export const CTRL = `<m:ctrlPr><w:rPr>${RFonts}<w:i/></w:rPr></m:ctrlPr>`;

export function mr(t: string): string {
  return `<m:r><w:rPr>${RFonts}<w:i/></w:rPr><m:t>${t}</m:t></m:r>`;
}

// Le wrapper <w:p> est obligatoire (OOXML ne permet pas d'inline-only dans <w:body>),
// mais on retire le <w:r> trailing : soupçon qu'il pousse Word à matérialiser un
// paragraphe complet au lieu de fusionner inline avec le paragraphe hôte.
export function omathPkg(inner: string): string {
  return `<pkg:package xmlns:pkg="http://schemas.microsoft.com/office/2006/xmlPackage"><pkg:part pkg:name="/_rels/.rels" pkg:contentType="application/vnd.openxmlformats-package.relationships+xml"><pkg:xmlData><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships></pkg:xmlData></pkg:part><pkg:part pkg:name="/word/document.xml" pkg:contentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"><pkg:xmlData><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"><w:body><w:p><m:oMath>${inner}</m:oMath></w:p></w:body></w:document></pkg:xmlData></pkg:part></pkg:package>`;
}

export function oVec(text: string): string {
  return omathPkg(
    `<m:groupChr><m:groupChrPr><m:chr m:val="\u2192"/><m:pos m:val="top"/><m:vertJc m:val="bot"/></m:groupChrPr><m:e>${mr(text)}</m:e></m:groupChr>`
  );
}

export function oBar(text: string): string {
  return omathPkg(
    `<m:groupChr><m:groupChrPr><m:chr m:val="\u00AF"/><m:pos m:val="top"/><m:vertJc m:val="bot"/></m:groupChrPr><m:e>${mr(text)}</m:e></m:groupChr>`
  );
}
