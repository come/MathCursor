"""
Génère `train_mathcursor_v11.ipynb` — notebook AUTONOME qui :
  1. charge tout le corpus (glob auto-découverte),
  2. entraîne plusieurs CANDIDATS (distilmult plein, distilmult élagué, MiniLM
     élagué) — chacun fine-tune → ONNX → int8,
  3. mesure F1 test / F1 gold / taille Mo / latence,
  4. les passe sur un jeu de SONDES COMPORTEMENTALES (matrices, mots-clés en
     tête, homonymes, intervalles, « deux formules ») → score par thème,
  5. ARBITRE : table récap + reco du meilleur compromis taille/qualité.

Lancer : `python _build_v11_notebook.py` → écrit le .ipynb à côté.
La table `FOLD` (accents) est extraite verbatim de train_mathcursor_benchmark.ipynb.
"""

import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC_NB = HERE / "train_mathcursor_benchmark.ipynb"
OUT_NB = HERE / "train_mathcursor_v11.ipynb"


def extract_fold():
    nb = json.loads(SRC_NB.read_text(encoding="utf-8"))
    for c in nb["cells"]:
        lines = c.get("source", [])
        grab, out = False, []
        for ln in lines:
            if "FOLD = str.maketrans(" in ln:
                grab = True
            if grab:
                out.append(ln)
                if ln.rstrip().endswith("')"):
                    return "".join(out)
    raise RuntimeError("FOLD introuvable dans le notebook source")


FOLD_BLOCK = extract_fold()


def code(src):
    lines = src.split("\n")
    src_list = [l + "\n" for l in lines[:-1]] + ([lines[-1]] if lines[-1] else [])
    return {"cell_type": "code", "metadata": {}, "execution_count": None,
            "outputs": [], "source": src_list}


def md(src):
    lines = src.split("\n")
    return {"cell_type": "markdown", "metadata": {},
            "source": [l + "\n" for l in lines[:-1]] + ([lines[-1]] if lines[-1] else [])}


cells = []

cells.append(md(r"""# MathCursor — NER v11 : entraînement + arbitrage (taille vs qualité)

Notebook **autonome**. Charge tout le corpus (glob), entraîne plusieurs candidats,
les passe sur des **sondes comportementales** (matrices, mots-clés en tête,
homonymes, intervalles, « deux formules ») et **tranche** automatiquement.

Candidats par défaut :
- `distilmult-full` — référence (~129 Mo)
- `distilmult-pruned` — vocab élagué FR+EN (~55-65 Mo attendu)
- `minilm-pruned` — base plus petite + élagage (~25-35 Mo) — ⚠️ MiniLM avait un
  bug d'alignement tokenizer historiquement ; les sondes le révéleront si présent.

Drive attendu `MyDrive/mathcursor/` : `train/val/test.jsonl`,
`regression_v1_gold.jsonl`, et tous les `extension_*.jsonl`.
"""))

cells.append(code(r"""# 1. Dépendances
get_ipython().system('pip install -q "transformers>=4.40" datasets evaluate seqeval "optimum[onnxruntime]>=1.19" textpruner')
"""))

cells.append(code(r"""# 2. Drive + corpus (glob auto-découverte : aucune extension oubliée)
from google.colab import drive
import os, glob

drive.mount('/content/drive')
WORKDIR = '/content/drive/MyDrive/mathcursor'
assert os.path.isdir(WORKDIR), f'Dossier introuvable : {WORKDIR}'

for name in ['train.jsonl', 'val.jsonl', 'test.jsonl', 'regression_v1_gold.jsonl']:
    p = os.path.join(WORKDIR, name)
    assert os.path.isfile(p), f'Manquant : {p}'
    print(f'OK {name}: {os.path.getsize(p)/1024:.1f} Ko')

EXTENSIONS = sorted(os.path.basename(p)
                    for p in glob.glob(os.path.join(WORKDIR, 'extension_*.jsonl')))
for name in EXTENSIONS:
    print(f'OK {name}: {os.path.getsize(os.path.join(WORKDIR, name))/1024:.1f} Ko (extension)')
print(f'\n{len(EXTENSIONS)} extensions auto-découvertes')
"""))

cells.append(code(r"""# 3. Config
LABELS = ['O', 'B-MATH', 'I-MATH']
LABEL2ID = {l: i for i, l in enumerate(LABELS)}
ID2LABEL = {i: l for i, l in enumerate(LABELS)}

MAX_LENGTH = 128
BATCH_SIZE = 16
NUM_EPOCHS = 4
WEIGHT_DECAY = 0.01

RESULTS_DIR = os.path.join(WORKDIR, 'v11-results')
os.makedirs(RESULTS_DIR, exist_ok=True)

# Candidats à arbitrer (commente une ligne pour l'exclure).
CANDIDATES = [
    {'short': 'distilmult-full',   'base': 'distilbert-base-multilingual-cased',     'lr': 5e-5, 'prune': False},
    {'short': 'distilmult-pruned', 'base': 'distilbert-base-multilingual-cased',     'lr': 5e-5, 'prune': True},
    {'short': 'minilm-pruned',     'base': 'microsoft/Multilingual-MiniLM-L12-H384', 'lr': 5e-5, 'prune': True},
]

# Critère d'adoption (ajustable).
GATE_F1_TEST = 0.98
GATE_F1_GOLD = 0.98   # 0.99 sur 77 cas gold = "parfait ou rien" ; 0.98 plus realiste
"""))

cells.append(code('# 4. Chargement corpus (accents foldés comme en prod)\n'
                  'import json as _json\nfrom datasets import Dataset, DatasetDict\n\n'
                  + FOLD_BLOCK +
                  r"""

def load_jsonl(path):
    with open(path, encoding='utf-8') as f:
        rows = [_json.loads(l) for l in f]
    for r in rows:
        r['text'] = r['text'].translate(FOLD)
    return rows

def fold(s):
    return s.translate(FOLD)

def to_dict(rows):
    return {'text': [r['text'] for r in rows],
            'spans': [r['spans'] for r in rows],
            'lang': [r.get('lang', '') for r in rows]}

train = load_jsonl(os.path.join(WORKDIR, 'train.jsonl'))
val   = load_jsonl(os.path.join(WORKDIR, 'val.jsonl'))
test  = load_jsonl(os.path.join(WORKDIR, 'test.jsonl'))
gold  = load_jsonl(os.path.join(WORKDIR, 'regression_v1_gold.jsonl'))  # hold-out, jamais train
n0 = len(train)
for name in EXTENSIONS:
    train += load_jsonl(os.path.join(WORKDIR, name))

datasets = DatasetDict({
    'train':      Dataset.from_dict(to_dict(train)),
    'validation': Dataset.from_dict(to_dict(val)),
    'test':       Dataset.from_dict(to_dict(test)),
    'gold':       Dataset.from_dict(to_dict(gold)),
})
print(f'Train {len(train)} (base {n0} + {len(train)-n0} ext) | Val {len(val)} | Test {len(test)} | Gold {len(gold)}')
"""))

cells.append(code(r"""# 5. Élagage de vocabulaire (FR+EN) — réduit l'embedding multilingue
from transformers import AutoTokenizer, AutoModelForTokenClassification

def prune_vocab(base, out_dir):
    from textpruner import VocabularyPruner
    texts = (list(datasets['train']['text']) + list(datasets['validation']['text'])
             + list(datasets['test']['text']) + list(datasets['gold']['text']))
    tok = AutoTokenizer.from_pretrained(base, add_prefix_space=False)
    mdl = AutoModelForTokenClassification.from_pretrained(
        base, num_labels=len(LABELS), id2label=ID2LABEL, label2id=LABEL2ID)
    v0 = tok.vocab_size
    VocabularyPruner(mdl, tok).prune(dataiter=texts, save_model=False)
    os.makedirs(out_dir, exist_ok=True)
    mdl.save_pretrained(out_dir); tok.save_pretrained(out_dir)
    print(f'  élagage {base}: vocab {v0} -> {len(tok)} ({100*(1-len(tok)/v0):.0f}% en moins)')
    return out_dir
"""))

cells.append(code(r"""# 6. Tokenisation + alignement BIO (offsets caractère -> tokens)
def make_tokenize_fn(tokenizer):
    def fn(batch):
        enc = tokenizer(batch['text'], truncation=True, max_length=MAX_LENGTH,
                        return_offsets_mapping=True)
        all_labels = []
        for i, spans in enumerate(batch['spans']):
            text = batch['text'][i]
            ischar = [False] * len(text)
            for s in spans:
                for c in range(s['start'], min(s['end'], len(text))):
                    ischar[c] = True
            labels, prev = [], False
            for (a, b) in enc['offset_mapping'][i]:
                if a == b:                       # token spécial
                    labels.append(-100); prev = False; continue
                math = any(ischar[a:b])
                if math:
                    labels.append(LABEL2ID['B-MATH'] if not prev else LABEL2ID['I-MATH']); prev = True
                else:
                    labels.append(LABEL2ID['O']); prev = False
            all_labels.append(labels)
        enc['labels'] = all_labels
        enc.pop('offset_mapping')
        return enc
    return fn
"""))

cells.append(code(r"""# 7. Métriques (seqeval entité = niveau span)
import numpy as np, evaluate
_seqeval = evaluate.load('seqeval')

def compute_metrics(p):
    preds = np.argmax(p.predictions, axis=2)
    refs = [[ID2LABEL[l] for (pr, l) in zip(pred, lab) if l != -100]
            for pred, lab in zip(preds, p.label_ids)]
    hyps = [[ID2LABEL[pr] for (pr, l) in zip(pred, lab) if l != -100]
            for pred, lab in zip(preds, p.label_ids)]
    r = _seqeval.compute(predictions=hyps, references=refs, zero_division=0)
    return {'f1': r['overall_f1'], 'precision': r['overall_precision'], 'recall': r['overall_recall']}
"""))

cells.append(code(r"""# 8. Entraînement d'un candidat : fine-tune -> ONNX -> int8 -> métriques
import time
from transformers import (TrainingArguments, Trainer, DataCollatorForTokenClassification, pipeline)
from optimum.onnxruntime import ORTModelForTokenClassification, ORTQuantizer
from optimum.onnxruntime.configuration import AutoQuantizationConfig

def dir_size_mb(d):
    return sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d)
               if f.endswith('.onnx')) / 1e6

def train_one(cfg):
    short, base, lr, prune = cfg['short'], cfg['base'], cfg['lr'], cfg['prune']
    print(f'\n{"="*70}\n  {short.upper()}  ({base})\n{"="*70}')
    src = prune_vocab(base, os.path.join(WORKDIR, f'base-{short}')) if prune else base

    tok = AutoTokenizer.from_pretrained(src, add_prefix_space=False)
    tokd = datasets.map(make_tokenize_fn(tok), batched=True,
                        remove_columns=['text', 'spans', 'lang'])
    model = AutoModelForTokenClassification.from_pretrained(
        src, num_labels=len(LABELS), id2label=ID2LABEL, label2id=LABEL2ID)

    out_pt = os.path.join(WORKDIR, f'pt-{short}')
    args = TrainingArguments(
        output_dir=out_pt, learning_rate=lr, per_device_train_batch_size=BATCH_SIZE,
        per_device_eval_batch_size=BATCH_SIZE, num_train_epochs=NUM_EPOCHS,
        weight_decay=WEIGHT_DECAY, eval_strategy='epoch', save_strategy='epoch',
        load_best_model_at_end=True, metric_for_best_model='f1', logging_steps=50,
        report_to='none')
    trainer = Trainer(model=model, args=args, train_dataset=tokd['train'],
                      eval_dataset=tokd['validation'], tokenizer=tok,
                      data_collator=DataCollatorForTokenClassification(tok),
                      compute_metrics=compute_metrics)
    trainer.train()
    f1_test = trainer.predict(tokd['test']).metrics['test_f1']
    f1_gold = trainer.predict(tokd['gold']).metrics['test_f1']
    trainer.save_model(out_pt); tok.save_pretrained(out_pt)

    # ONNX + quantization int8
    onnx_dir = os.path.join(WORKDIR, f'onnx-{short}')
    quant_dir = os.path.join(WORKDIR, f'onnx-int8-{short}')
    ort = ORTModelForTokenClassification.from_pretrained(out_pt, export=True)
    ort.save_pretrained(onnx_dir); tok.save_pretrained(onnx_dir)
    q = ORTQuantizer.from_pretrained(onnx_dir)
    q.quantize(save_dir=quant_dir,
               quantization_config=AutoQuantizationConfig.avx2(is_static=False, per_channel=False))
    tok.save_pretrained(quant_dir)

    # latence (pipeline int8, CPU)
    nlp = pipeline('token-classification', model=ORTModelForTokenClassification.from_pretrained(quant_dir),
                   tokenizer=AutoTokenizer.from_pretrained(quant_dir),
                   aggregation_strategy='simple', device=-1)
    samples = ['On a f(x) = 2x + 1 et cest tout.', '(1/2, x^2 ; sqrt(2), a_n)']
    for s in samples: nlp(s)  # warmup
    t0 = time.time()
    for _ in range(20):
        for s in samples: nlp(s)
    lat = 1000 * (time.time() - t0) / (20 * len(samples))

    return {'short': short, 'f1_test': f1_test, 'f1_gold': f1_gold,
            'size_mb': dir_size_mb(quant_dir), 'latency_ms': lat, 'quant_dir': quant_dir}
"""))

cells.append(code(r"""# 9. SONDES COMPORTEMENTALES — c'est ce qui ARBITRE sur les thèmes v11.
# (texte, fragments math attendus, thème). [] = aucune math attendue (distracteur).
PROBES = [
    # Matrices à contenu complexe
    ('(1/2, x^2, -3 ; sqrt(2), a_n, cos(x))',              ['(1/2, x^2, -3 ; sqrt(2), a_n, cos(x))'], 'matrice'),
    ('(a b c; d e f)',                                     ['(a b c; d e f)'],                        'matrice'),
    ('[1,2,3;4,5,6;7,8,9]',                                ['[1,2,3;4,5,6;7,8,9]'],                   'matrice'),
    ('On considere (x+1, 1/n ; 2^n, -x)',                  ['(x+1, 1/n ; 2^n, -x)'],                  'matrice'),
    # Tuples / points
    ('(1, 2, 3)',                                          ['(1, 2, 3)'],                             'tuple'),
    ('(x, y)',                                             ['(x, y)'],                                'tuple'),
    # Mots-cles en tete de ligne
    ('bar z',                                              ['bar z'],                                 'keyword'),
    ('module de z',                                        ['module de z'],                           'keyword'),
    ('racine de x+1',                                      ['racine de x+1'],                         'keyword'),
    ('pgcd(12,18)',                                        ['pgcd(12,18)'],                           'keyword'),
    ('3 parmi 10',                                         ['3 parmi 10'],                            'keyword'),
    ('pourtout x dans R',                                  ['pourtout x dans R'],                     'keyword'),
    ('det A',                                              ['det A'],                                 'keyword'),
    # Distracteurs HOMONYMES (aucune math)
    ('On se retrouve au bar a 18h',                        [],                                        'homonyme'),
    ('Le module 3 du cours porte sur les fonctions',       [],                                        'homonyme'),
    ('Parmi les eleves, trois etaient absents',            [],                                        'homonyme'),
    ('La racine du probleme est ailleurs',                 [],                                        'homonyme'),
    ('On sest assis en rond autour du feu',                [],                                        'homonyme'),
    # Intervalles
    ('[0;1]',                                              ['[0;1]'],                                 'intervalle'),
    (']-inf;2,5[',                                         [']-inf;2,5['],                            'intervalle'),
    ('x dans [0;1[',                                       ['x dans [0;1['],                          'intervalle'),
    # Cas difficile connu : deux formules sur une ligne
    ('If a + b = c then a = c - b',                        ['a + b = c', 'a = c - b'],                'multi'),
    ('On a f(x) = 2x + 1',                                 ['f(x) = 2x + 1'],                         'prose'),
]

def _spans_of(text, frags):
    out = set()
    for fr in frags:
        i = text.find(fr)
        if i >= 0:
            out.add((i, i + len(fr)))
    return out

def score_probes(quant_dir):
    nlp = pipeline('token-classification',
                   model=ORTModelForTokenClassification.from_pretrained(quant_dir),
                   tokenizer=AutoTokenizer.from_pretrained(quant_dir),
                   aggregation_strategy='simple', device=-1)
    by_theme, fails = {}, []
    for text, frags, theme in PROBES:
        t = fold(text)
        exp = _spans_of(t, frags)
        got = {(int(r['start']), int(r['end'])) for r in (nlp(t) if t else [])}
        ok = (got == exp)
        by_theme.setdefault(theme, []).append(ok)
        if not ok:
            fails.append((theme, text, [t[a:b] for a, b in sorted(exp)],
                          [t[a:b] for a, b in sorted(got)]))
    rates = {th: sum(v) / len(v) for th, v in by_theme.items()}
    rates['_overall'] = sum(sum(v) for v in by_theme.values()) / len(PROBES)
    return rates, fails
"""))

cells.append(code(r"""# 10. RUN : entraine + sonde chaque candidat
results = []
for cfg in CANDIDATES:
    r = train_one(cfg)
    rates, fails = score_probes(r['quant_dir'])
    r['probes'] = rates
    r['fails'] = fails
    results.append(r)
    print(f"  {r['short']}: F1 test {r['f1_test']:.4f} | gold {r['f1_gold']:.4f} | "
          f"{r['size_mb']:.0f} Mo | {r['latency_ms']:.1f} ms | sondes {rates['_overall']*100:.0f}%")
"""))

cells.append(code(r"""# 11. ARBITRAGE : table + reco
THEMES = ['matrice', 'tuple', 'keyword', 'homonyme', 'intervalle', 'multi', 'prose']
CRITICAL = ['homonyme', 'matrice', 'keyword']   # ce qu'on refuse de sacrifier

print('='*100)
print('  ARBITRAGE v11')
print('='*100)
hdr = f"{'modele':18} {'F1test':>7} {'F1gold':>7} {'Mo':>5} {'ms':>5} {'sondes':>7}  " + " ".join(f'{t[:5]:>5}' for t in THEMES)
print(hdr); print('-'*len(hdr))
for r in results:
    pr = r['probes']
    row = (f"{r['short']:18} {r['f1_test']:>7.4f} {r['f1_gold']:>7.4f} {r['size_mb']:>5.0f} "
           f"{r['latency_ms']:>5.1f} {pr['_overall']*100:>6.0f}%  "
           + " ".join(f"{pr.get(t,0)*100:>4.0f}%" for t in THEMES))
    print(row)

def passes(r):
    gate = r['f1_test'] >= GATE_F1_TEST and r['f1_gold'] >= GATE_F1_GOLD
    crit = all(r['probes'].get(t, 0) >= 0.90 for t in CRITICAL)   # >=90% sur les themes critiques
    return gate and crit

ok = [r for r in results if passes(r)]
print('\n' + '-'*100)
if ok:
    best = min(ok, key=lambda r: r['size_mb'])   # le plus PETIT qui passe la barre
    print(f"  RECO : {best['short']}  ({best['size_mb']:.0f} Mo, F1 gold {best['f1_gold']:.4f}, "
          f"sondes {best['probes']['_overall']*100:.0f}%)")
    print(f"  -> le plus leger qui passe gate (F1 test>={GATE_F1_TEST}, gold>={GATE_F1_GOLD}) ET themes critiques >=90%")
else:
    best = max(results, key=lambda r: (r['f1_gold'], -r['size_mb']))
    print(f"  AUCUN candidat ne passe la barre. Meilleur compromis : {best['short']} "
          f"(gold {best['f1_gold']:.4f}, {best['size_mb']:.0f} Mo)")

print('\n  Echecs de sondes du recommande :')
for theme, text, exp, got in best['fails']:
    print(f"    [{theme}] {text!r}  attendu={exp}  predit={got}")
"""))

nb = {"cells": cells,
      "metadata": {"kernelspec": {"display_name": "Python 3", "name": "python3"},
                   "language_info": {"name": "python"}},
      "nbformat": 4, "nbformat_minor": 5}
OUT_NB.write_text(json.dumps(nb, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"Ecrit : {OUT_NB.name}  ({len(cells)} cellules)")
