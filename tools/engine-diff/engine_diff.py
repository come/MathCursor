# MathCursor: capturing mathematical intent from linear keyboard input.
# Copyright (C) 2026  Côme de Percin
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

"""Non-régression LARGE du moteur, au-delà des fixtures.

Deux contrôles sur un corpus généré (~17 000 entrées : fixtures, combinaisons
fonctions/arguments/indices, formules et grandeurs de physique-chimie) :

1. RÉFÉRENCE : moteur Rust d'un commit git (défaut HEAD, construit dans un
   worktree temporaire) vs moteur de l'arbre de travail. Toute différence de
   décision, de candidat ou de coût est listée, ainsi que toute différence de
   composition multiligne (= / <=>) et de système { (~1 200 requêtes).
   Attendu : 0, sauf évolution voulue.
2. PARITÉ : moteur C# (Word) vs moteur Rust (VSCode, LibreOffice) de l'arbre de
   travail, sur le même corpus. Attendu : 0 toujours.

Usage (depuis la racine du dépôt) :
    python -I -X utf8 tools/engine-diff/engine_diff.py                 # HEAD, tous les modes
    python -I -X utf8 tools/engine-diff/engine_diff.py --ref 4ed2461 --modes fr,us
    python -I -X utf8 tools/engine-diff/engine_diff.py --expect-diff   # évolution voulue : liste sans échouer

Modes : fr, us, fr+physchem, us+physchem (mode physique-chimie, ADR
2026-10-16-Feat-physics-chemistry-mode). Un commit antérieur au mode
physique-chimie l'ignore : comparer alors avec --modes fr,us.
Code de sortie : 0 si tout est conforme, 1 sinon.
"""
import argparse, itertools, json, os, random, shutil, subprocess, sys, tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FIXTURES = os.path.join(REPO, 'engine', 'tests', 'MathCursor.Engine.Tests', 'fixtures.json')
WORK = os.path.join(tempfile.gettempdir(), 'mathcursor-engine-diff')


# ---------------------------------------------------------------- corpus
def corpus():
    out = set()
    for f in json.load(open(FIXTURES, encoding='utf-8')):
        out.add(f['in'])
    # fonctions × arguments × formes (imbrications, scripts, produits)
    heads = ['f', 'g', 'F', 'u', 'sin', 'cos', 'ln', 'exp', 'sqrt', 'log', 'f_n', 'f2', 'fn', 'u_n', 'sin2', 'f^2',
             'f_(n+1)', 'abs', 'P', 'f_1', 'g^(-1)', 'cos^2']
    args = ['x', 'x2', 'x^2', 'x_2', 'x_n', '2x', 'x+1', 'x2+1', '(x+1)2', 'x_n2', 'x^n', '1/x', 'x/2', 'a,b', 'n+1',
            '-x', 'e^x', 'x_(n+1)', 'x^(n+1)', 'u_n2', '2^x', 'x y']
    forms = ['{h}({a})', '{h} {a}', '{h}({a})2', '{h}({a})^2', '{h}({a})+1', '2{h}({a})', '{h}({a})={a}', '({h}({a}))',
             '{h}({h}({a}))', '{h}({a})/{a}', '1/{h}({a})', '{h}({a})_n', '{h}_n({a})']
    for h, a, fm in itertools.product(heads, args, forms):
        out.add(fm.format(h=h, a=a))
    atoms = ['x', 'u', 'a', 'f', '2', 'n', '(x+1)', 'e']
    scr = ['2', 'n', 'n+1', '(n+1)', '-x', '1/2', 'ij']
    for b, s1, s2 in itertools.product(atoms, scr, scr):
        for t in ['{b}_{s}', '{b}^{s}', '{b}_{s}^{t}', '{b}^{s}_{t}', '{b}_{s}+{b}^{t}', '{b}{s}', '{b}_({s})({b})',
                  '{b}^{s}({b})', '2{b}_{s}=1']:
            out.add(t.format(b=b, s=s1, t=s2))
    out.update(units_sci_corpus())
    out.update(physchem_corpus())
    return sorted(s for s in out if s.strip() and '\n' not in s and '\r' not in s)


def units_sci_corpus():
    """Nombres × opérateurs × unités, et notation scientifique (10-3, 103)
    (ADR 2026-10-14-Feat-scientific-notation-linear-minus)."""
    out = set()
    lefts = ['(1+2)', '1/2', '10^3', '2*10^-3', '2,5', '3', 'x', 'x2']
    ops = ['', ' ', '*', '+', '-', '/']
    rights = ['10', '2,5', '5', 'x']
    units = [' m', 'm', ' kg', ' m/s', 's', ' ms', ' km/h', '']
    tails = ['', '+1', '=x', '*2']
    for l, o, r, u, t in itertools.product(lefts, ops, rights, units, tails):
        out.add(l + o + r + u + t)
    heads = ['2', '(1+2)', 'x', '2,5', '10']
    times = ['*', ' * ', '×', '.', '+', '/']
    for h, m in itertools.product(heads, times):
        for sep, r, suf in itertools.product(['-', ' - ', '+'], ['3', '12', '(3)', 'x'], ['', ' m', 'm', '=x']):
            out.add(f'{h}{m}10{sep}{r}{suf}')
        for n, suf in itertools.product(['10', '100', '1000', '1012', '103', '105,5', '108', '120'],
                                        ['', ' m/s', '+1', '=x', 'm']):
            out.add(f'{h}{m}{n}{suf}')
    return out


def physchem_corpus():
    els = json.load(open(os.path.join(REPO, 'data', 'engine', 'physchem.json'), encoding='utf-8'))['elements']
    rnd = random.Random(42)
    real = ['H2O', 'CO2', 'O2', 'H2', 'N2', 'Cl2', 'NaCl', 'HCl', 'NaOH', 'H2SO4', 'HNO3', 'H3PO4', 'NH3', 'CH4',
            'C2H5OH', 'CH3COOH', 'C6H12O6', 'CaCO3', 'Ca(OH)2', 'Al2(SO4)3', 'Fe2O3', 'KMnO4', 'K2Cr2O7',
            '(NH4)2SO4', '(NH4)3PO4', 'Mg(OH)2', 'Fe(OH)3', 'Fe', 'Cu', 'Zn', 'Na', 'K', 'Ca', 'Al']
    ions = ['H+', 'OH-', 'Na+', 'Cl-', 'Ca2+', 'Fe2+', 'Fe3+', 'Cu2+', 'Al3+', 'SO42-', 'NO3-', 'CO32-', 'PO43-',
            'NH4+', 'H3O+', 'MnO4-', 'VO2+', 'Fe^2+', 'SO4^2-', 'Na+(aq)', 'NaCl(s)', 'H2O(l)', 'CO2(g)']
    phys = ['v0', 'x0', 'T1', 'v2', 'm1 + m2', 'T2 - T1', 'T=T2-T1', 'v2 - v1', 'Req = R1 + R2', 'X1=X0+x',
            'x = 1/2gt2 + v0t + x0', 'Ec = 1/2mv2', '(1/2)mv2', 'Em = Ec + Ep', 'vx = v0cos(alpha)', 'P = RI2',
            'U = RI', 'f(x2)', 'cos(x2)', 'pir2', '5 m2', '20 cm2', '3*10-3 ms', 'g = 9,81 m/s2', 'BC', '(BC)',
            '(BC)2', '(AB) // (CD)', 'vec AB', 'Re(z)', '2 Pa', 'C', 'V2', 'CO', 'Ceq', 'Vm', 'theta0',
            'pH = -log[H3O+]', 'n = m/M', 'PV = nRT', '2H2 + O2 -> 2H2O', 'Fe^3+ + 3OH- -> Fe(OH)3',
            '2Fe + 2e-', '2e-', 'Cu2+ + 2e- = Cu', '2H + 2e+', 'e-x', 'e-1', 'x = e-']
    gen = []
    for _ in range(400):
        f = ''.join(rnd.choice(els) + rnd.choice(['', '', str(rnd.randint(2, 12))]) for _ in range(rnd.randint(1, 4)))
        r = rnd.random()
        if r < 0.15: f += rnd.choice(['+', '-', '2+', '3-', '^2+'])
        elif r < 0.25: f = '(' + f + ')' + str(rnd.randint(2, 4))
        elif r < 0.3: f += rnd.choice(['(aq)', '(s)', '(g)', '(l)'])
        gen.append(f)
    for _ in range(200):
        gen.append(f'{rnd.choice(real)} + {rnd.choice(ions)} -> {rnd.choice(real)}')
    return real + ions + phys + gen


# ---------------------------------------------------------------- moteurs
def run(cmd, **kw):
    r = subprocess.run(cmd, capture_output=True, text=True, **kw)
    if r.returncode != 0:
        sys.exit(f'échec : {" ".join(cmd)}\n{r.stdout[-2000:]}\n{r.stderr[-2000:]}')
    return r


def build_ref(ref):
    sha = run(['git', '-C', REPO, 'rev-parse', '--short', ref]).stdout.strip()
    tree = os.path.join(WORK, 'tree-' + sha)
    target = os.path.join(WORK, 'target-' + sha)
    exe = os.path.join(target, 'release', 'analyze.exe' if os.name == 'nt' else 'analyze')
    if not os.path.exists(exe):
        if os.path.exists(tree):
            subprocess.run(['git', '-C', REPO, 'worktree', 'remove', '--force', tree], capture_output=True)
        run(['git', '-C', REPO, 'worktree', 'add', '--detach', tree, sha])
        try:
            run(['cargo', 'build', '--release', '--bin', 'analyze'], cwd=os.path.join(tree, 'rust'),
                env=dict(os.environ, CARGO_TARGET_DIR=target))
        finally:
            subprocess.run(['git', '-C', REPO, 'worktree', 'remove', '--force', tree], capture_output=True)
    return sha, exe


def build_work():
    run(['cargo', 'build', '--release', '--bin', 'analyze'], cwd=os.path.join(REPO, 'rust'))
    return os.path.join(REPO, 'rust', 'target', 'release', 'analyze.exe' if os.name == 'nt' else 'analyze')


def rust_out(exe, mode, inputs):
    data = ''.join(mode + '\t' + s + '\n' for s in inputs) + 'QUIT\n'
    out = subprocess.run([exe], input=data.encode('utf-8'), capture_output=True).stdout.decode('utf-8')
    lines = out.split('\n')[1:len(inputs) + 1]
    if len(lines) != len(inputs):
        sys.exit(f'{exe} : {len(lines)} réponses pour {len(inputs)} entrées')
    res = []
    for l in lines:
        try:
            j = json.loads(l)
            res.append((j['decision'], bool(j['hasNote']), [(c['latex'], c['cost']) for c in j['ranked']]))
        except Exception:
            res.append(('erreur', False, []))
    return res


def cs_out(inp, modes, n):
    env = dict(os.environ, MC_ENGINE_DUMP_IN=inp, MC_ENGINE_DUMP_MODES=','.join(modes))
    run(['dotnet', 'test', os.path.join(REPO, 'engine', 'tests', 'MathCursor.Engine.Tests'), '--nologo', '-v', 'q',
         '--filter', 'FullyQualifiedName~EngineDumpTests'], env=env)
    outs = {}
    for m in modes:
        rows = open(inp + '.' + m.replace('+', '_') + '.cs.out', encoding='utf-8').read().split('\n')[:n]
        res = []
        for row in rows:
            dec, note, cands = row.split('\t', 2)
            cl = [(x.split('\u0002')[0], float(x.split('\u0002')[1])) for x in cands.split('\u0001')] if cands else []
            res.append((dec, note == '1', cl))
        outs[m] = res
    return outs


def compose_corpus():
    """Requêtes de composition (protocole analyze) : chaînes multilignes = / <=>
    (COMPOSE) et systèmes { (COMPOSE_SYSTEM), avec et sans préfixe."""
    firsts = ['f(x) = 2x+2-2', '2x+1 = 5', 'x2 - 1 = 0', '(x+1)2 = x2+2x+1', 'u_n+1 = 2u_n', 'v0 = 2',
              'T2 - T1 = 5', 'Fe2+ + H2O', '1/x+1 = 2', 'x = 1/2gt2 + v0t + x0']
    nexts = ['= 2x', '= 4', '<=> x=2', '<=> x = 1/2', '= (x-1)(x+1)', '<=> x2 = 1', '= v0t + x0', '<=> 2x = 4']
    reqs = []
    for f in firsts:
        for n1 in nexts:
            reqs.append(('COMPOSE', json.dumps([{'steno': f, 'index': 0}, {'steno': n1, 'index': 1}])))
            for n2 in nexts[::3]:
                reqs.append(('COMPOSE', json.dumps([{'steno': f, 'index': 0}, {'steno': n1, 'index': 1},
                                                    {'steno': n2, 'index': 2}])))
    rows = ['2x+y=5', 'x-y=1', 'x=1', 'y = 2x+1', 'v0 = 2', 'x0 = v0t + 1', 'x2 = 1', 'T1 = T2 - 4', 'H2O', 'Fe2+',
            'u_n+1 = 2u_n', '1/x = 2', '2H1 + 4H+ = 2He + 2e-']
    for p in ['', 'f(x) = ', 'A ', 'S : ']:
        for a, b in itertools.product(rows, rows):
            reqs.append(('COMPOSE_SYSTEM', f'{p}{{ {a} ; {b}'))
    for a, b, c in itertools.islice(itertools.permutations(rows, 3), 0, None, 7):
        reqs.append(('COMPOSE_SYSTEM', f'{{ {a} ; {b} ; {c}'))
    return reqs


def rust_compose(exe, mode, reqs):
    data = ''.join(f'{verb}\t{mode}\t{body}\n' for verb, body in reqs) + 'QUIT\n'
    out = subprocess.run([exe], input=data.encode('utf-8'), capture_output=True).stdout.decode('utf-8')
    lines = out.split('\n')[1:len(reqs) + 1]
    if len(lines) != len(reqs):
        sys.exit(f'{exe} : {len(lines)} réponses pour {len(reqs)} compositions')
    return lines


def show_compose(label, reqs, a, b, la, lb, limit=6):
    diffs = [(r, x, y) for r, x, y in zip(reqs, a, b) if x != y]
    print(f'  {label} : {len(diffs)} écart(s)')
    for (verb, body), x, y in diffs[:limit]:
        print(f'    {verb} {body[:90]}\n      {la:5} {x[:140]}\n      {lb:5} {y[:140]}')
    return len(diffs)


def same(a, b):
    return a[0] == b[0] and a[1] == b[1] and [l for l, _ in a[2]] == [l for l, _ in b[2]] \
        and all(abs(x - y) < 1e-9 for (_, x), (_, y) in zip(a[2], b[2]))


def show(label, inputs, a, b, la, lb, limit=8):
    diffs = [(s, x, y) for s, x, y in zip(inputs, a, b) if not same(x, y)]
    print(f'  {label} : {len(diffs)} écart(s)')
    for s, x, y in diffs[:limit]:
        print(f'    {s!r}\n      {la:5} {x[0]} {[l for l, _ in x[2]][:3]}\n      {lb:5} {y[0]} {[l for l, _ in y[2]][:3]}')
    return len(diffs)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--ref', default='HEAD', help='commit de référence (défaut HEAD)')
    ap.add_argument('--modes', default='fr,us,fr+physchem,us+physchem')
    ap.add_argument('--expect-diff', action='store_true', help='écarts avec la référence attendus : lister sans échouer')
    ap.add_argument('--no-ref', action='store_true', help='parité C#/Rust seulement')
    a = ap.parse_args()
    modes = a.modes.split(',')
    os.makedirs(WORK, exist_ok=True)

    inputs = corpus()
    inp = os.path.join(WORK, 'corpus.txt')
    open(inp, 'w', encoding='utf-8', newline='\n').write('\n'.join(inputs) + '\n')
    print(f'corpus : {len(inputs)} entrées, modes : {", ".join(modes)}')

    work = build_work()
    cur = {m: rust_out(work, m, inputs) for m in modes}
    fail = 0

    if not a.no_ref:
        sha, ref = build_ref(a.ref)
        print(f'1. référence {sha} vs arbre de travail (moteur Rust)')
        n = sum(show(m, inputs, rust_out(ref, m, inputs), cur[m], sha[:5], 'work') for m in modes)
        reqs = compose_corpus()
        print(f'   composition multiligne et systèmes ({len(reqs)} requêtes)')
        n += sum(show_compose(m, reqs, rust_compose(ref, m, reqs), rust_compose(work, m, reqs), sha[:5], 'work')
                 for m in modes)
        if n and not a.expect_diff:
            fail = 1

    print('2. parité C# vs Rust (arbre de travail)')
    cs = cs_out(inp, modes, len(inputs))
    if sum(show(m, inputs, cs[m], cur[m], 'C#', 'Rust') for m in modes):
        fail = 1

    print('OK' if not fail else 'ÉCARTS : voir ci-dessus')
    sys.exit(fail)


if __name__ == '__main__':
    main()
