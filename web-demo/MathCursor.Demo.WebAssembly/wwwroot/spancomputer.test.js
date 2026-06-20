// Parite du port JS de SpanComputer vs le C# valide.
// Cas portes 1:1 de adapter-vsto/tests/MathCursor.Tests/Host/SpanComputerTests.cs.
// Lancement : node spancomputer.test.js  (sortie OK/FAIL, exit code 1 si echec).
'use strict';

var SpanComputer = require('./spancomputer.js');

// Reproduit le helper Span() des tests xUnit : zone brute + trim des blancs.
function span(text, caret) {
  var z = SpanComputer.computeZone(text, caret, []);
  return text.substring(z.start, z.end);
}

var cases = [
  // [nom, texte, caret, attendu]
  ['Factorielle_au_bout_est_captee', 'n!', 2, 'n!'],
  ['Factorielle_apres_un_stopword', 'soit n!', 'soit n!'.length, 'n!'],
  ['Factorielle_a_droite_d_un_egal', 'a=n!', 'a=n!'.length, 'n!'],
  ['Point_reste_un_delimiteur', 'fin. x+1', 'fin. x+1'.length, 'x+1'],
  ['Expression_simple_entiere', '1/x+1', 5, '1/x+1'],
  ['Matrice_non_fermee_virgules_captee_entiere', '(a,b,c,d ;e,f', '(a,b,c,d ;e,f'.length, '(a,b,c,d ;e,f'],
  ['Matrice_non_fermee_espaces_captee_entiere', '(a b c d; e f', '(a b c d; e f'.length, '(a b c d; e f'],
  ['Matrice_non_fermee_caret_au_milieu', '(a,b;c,d', 6, '(a,b;c,d'],
  ['Crochet_non_ferme_intervalle', '[0;1', '[0;1'.length, '[0;1'],
  ['Decimale_dans_parenthese_non_fermee', '(1.5 ;2.5', '(1.5 ;2.5'.length, '(1.5 ;2.5'],
  ['Matrice_fermee_inchangee', '(a,b,c;d,e,f)', '(a,b,c;d,e,f)'.length, '(a,b,c;d,e,f)'],
  ['Point_virgule_hors_parenthese_coupe_toujours', 'a ; b', 'a ; b'.length, 'b']
];

// ── Comportement DEMO « mode reel » : set reduit (sans = < >) -> l'equation
//    entiere est captee. Specifique a la demo (pas une parite C#). ──
function spanDemo(text, caret) {
  var z = SpanComputer.computeZone(text, caret, [], SpanComputer.DEMO_DELIMITERS);
  return text.substring(z.start, z.end);
}

var demoCases = [
  // [nom, texte, caret, attendu]
  ['demo_egal_ne_coupe_pas', 'f(x) = 1/x', 'f(x) = 1/x'.length, 'f(x) = 1/x'],
  ['demo_prose_bornee_par_stopword', 'On a donc f(x)=1/x', 'On a donc f(x)=1/x'.length, 'f(x)=1/x'],
  ['demo_inegalite_entiere', 'x < 1', 'x < 1'.length, 'x < 1'],
  ['demo_point_virgule_coupe_toujours', 'a=b ; c=d', 'a=b ; c=d'.length, 'c=d'],
  ['demo_point_phrase_coupe_toujours', 'fin. x=1', 'fin. x=1'.length, 'x=1']
];

var failed = 0;
function run(label, list, fn) {
  console.log(label);
  for (var i = 0; i < list.length; i++) {
    var name = list[i][0], text = list[i][1], caret = list[i][2], expected = list[i][3];
    var got = fn(text, caret);
    if (got === expected) {
      console.log('  OK   ' + name);
    } else {
      failed++;
      console.log('  FAIL ' + name + '  attendu="' + expected + '"  obtenu="' + got + '"');
    }
  }
}

run('Parite C# (set complet) :', cases, span);
run('\nComportement demo (set reduit) :', demoCases, spanDemo);

var total = cases.length + demoCases.length;
console.log('\n' + (total - failed) + '/' + total + ' cas verts');
if (failed > 0) process.exit(1);
