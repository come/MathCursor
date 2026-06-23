// mathjax-full n'embarque pas de déclarations TS exploitables ici ; on rend ses
// sous-modules « any » (esbuild ne type-check pas — c'est pour l'éditeur/tsc).
declare module 'mathjax-full/js/*';
