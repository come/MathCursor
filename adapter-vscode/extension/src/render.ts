import { mathjax } from 'mathjax-full/js/mathjax.js';
import { TeX } from 'mathjax-full/js/input/tex.js';
import { SVG } from 'mathjax-full/js/output/svg.js';
import { liteAdaptor } from 'mathjax-full/js/adaptors/liteAdaptor.js';
import { RegisterHTMLHandler } from 'mathjax-full/js/handlers/html.js';
import { AllPackages } from 'mathjax-full/js/input/tex/AllPackages.js';

// Pré-rendu d'une formule LaTeX → SVG autonome (data-URI), pour l'aperçu dans le
// panneau de détails de la complétion native. MathJax (Apache-2.0) car KaTeX ne
// produit pas de SVG self-contained. fill=currentColor → suit le thème VSCode.

let doc: any;
let adaptor: any;

function ensure(): void {
  if (doc) return;
  adaptor = liteAdaptor();
  RegisterHTMLHandler(adaptor);
  const tex = new TeX({ packages: AllPackages });
  const svg = new SVG({ fontCache: 'local' });
  doc = mathjax.document('', { InputJax: tex, OutputJax: svg });
}

/** Retourne un data-URI image/svg+xml, ou undefined si le rendu échoue. */
export function renderSvgDataUri(latex: string): string | undefined {
  try {
    ensure();
    const node = doc.convert(latex, { display: false });
    const svg: string = adaptor.innerHTML(node).replace('<svg ', '<svg fill="currentColor" ');
    return 'data:image/svg+xml;utf8,' + encodeURIComponent(svg);
  } catch {
    return undefined;
  }
}
