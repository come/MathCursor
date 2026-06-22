// Parite du port JS de SpanComputer vs le C#.
// Cas = SOURCE UNIQUE partagee avec le C# :
//   adapter-vsto/tests/MathCursor.Tests/Host/spancomputer-fixtures.txt
// (plus de liste recopiee a la main → plus de derive silencieuse, ADR 2026-06-23).
// Lancement : node spancomputer.test.js  (sortie OK/FAIL, exit code 1 si echec).
'use strict';

var SpanComputer = require('./spancomputer.js');
var fs = require('fs');
var path = require('path');

// Reproduit le helper Span() des tests xUnit : zone brute + trim des blancs.
function span(text, caret) {
  var z = SpanComputer.computeZone(text, caret, []);
  return text.substring(z.start, z.end);
}

// Fixture partagee (meme fichier que le C#). [name, text, caret, expected].
var fixturePath = path.join(__dirname, '../../../adapter-vsto/tests/MathCursor.Tests/Host/spancomputer-fixtures.txt');
var cases = fs.readFileSync(fixturePath, 'utf8').split('\n')
  .map(function (l) { return l.replace(/\r$/, ''); })
  .filter(function (l) { return l.trim().length > 0 && l.trim()[0] !== '#'; })
  .map(function (l) { var p = l.split('|'); return [p[0], p[1], parseInt(p[2], 10), p[3]]; });

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
