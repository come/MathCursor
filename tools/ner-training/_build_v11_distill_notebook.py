"""
Génère `train_mathcursor_v11_distill.ipynb` — DISTILLATION vers un élève léger
(<30 Mo) qui imite le prof `distilmult-pruned` (46 Mo, le gagnant de l'arbitrage
v11). Réutilise verbatim les cellules TESTÉES du notebook v11 (chargement glob,
tokenisation BIO, métriques seqeval, sondes comportementales) puis ajoute :
  - chargement du prof (pt-distilmult-pruned), même tokenizer élagué partagé,
  - élève DistilBert (dim 384, 4 couches) entraîné par distillation
    (CE dure + KL douce sur les logits du prof, température T),
  - ONNX int8 + taille/latence,
  - ARBITRAGE prof vs élève (table + reco).

Prérequis : avoir lancé `train_mathcursor_v11.ipynb` (il produit
`pt-distilmult-pruned/` sur le Drive). Lancer : `python _build_v11_distill_notebook.py`.
"""

import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
V11 = HERE / "train_mathcursor_v11.ipynb"
OUT = HERE / "train_mathcursor_v11_distill.ipynb"

v11 = json.loads(V11.read_text(encoding="utf-8"))


def reuse(marker):
    for c in v11["cells"]:
        if marker in "".join(c.get("source", [])):
            return {"cell_type": "code", "metadata": {}, "execution_count": None,
                    "outputs": [], "source": list(c["source"])}
    raise RuntimeError(f"cellule v11 introuvable: {marker}")


def code(src):
    lines = src.split("\n")
    return {"cell_type": "code", "metadata": {}, "execution_count": None, "outputs": [],
            "source": [l + "\n" for l in lines[:-1]] + ([lines[-1]] if lines[-1] else [])}


def md(src):
    lines = src.split("\n")
    return {"cell_type": "markdown", "metadata": {},
            "source": [l + "\n" for l in lines[:-1]] + ([lines[-1]] if lines[-1] else [])}


cells = []

cells.append(md(r"""# MathCursor — NER v11 : DISTILLATION (<30 Mo)

Distille le prof **`distilmult-pruned`** (46 Mo, gagnant de l'arbitrage v11) dans
un **élève léger** (DistilBert dim 384, 4 couches, tokenizer élagué partagé).
Puis ONNX int8 + **arbitrage prof vs élève** sur les mêmes sondes.

**Prérequis** : avoir lancé `train_mathcursor_v11.ipynb` (il sauve
`pt-distilmult-pruned/` sur le Drive). Cellules de données / sondes réutilisées
verbatim depuis v11.
"""))

cells.append(code(r"""# 1. Dépendances
get_ipython().system('pip install -q "transformers>=4.40" datasets evaluate seqeval "optimum[onnxruntime]>=1.19"')
"""))

cells.append(code(r"""# 2. Drive + corpus (glob)
from google.colab import drive
import os, glob
drive.mount('/content/drive')
WORKDIR = '/content/drive/MyDrive/mathcursor'
assert os.path.isdir(WORKDIR), f'Dossier introuvable : {WORKDIR}'
for name in ['train.jsonl', 'val.jsonl', 'test.jsonl', 'regression_v1_gold.jsonl']:
    assert os.path.isfile(os.path.join(WORKDIR, name)), f'Manquant : {name}'
EXTENSIONS = sorted(os.path.basename(p) for p in glob.glob(os.path.join(WORKDIR, 'extension_*.jsonl')))
print(f'{len(EXTENSIONS)} extensions')
"""))

cells.append(code(r"""# 3. Config
LABELS = ['O', 'B-MATH', 'I-MATH']
LABEL2ID = {l: i for i, l in enumerate(LABELS)}
ID2LABEL = {i: l for i, l in enumerate(LABELS)}
MAX_LENGTH = 128
BATCH_SIZE = 16

TEACHER_DIR = os.path.join(WORKDIR, 'pt-distilmult-pruned')   # produit par train_mathcursor_v11.ipynb
assert os.path.isdir(TEACHER_DIR), (
    f"Prof introuvable : {TEACHER_DIR}. Lance d'abord train_mathcursor_v11.ipynb.")
TEACHER_QUANT = os.path.join(WORKDIR, 'onnx-int8-distilmult-pruned')  # 46 Mo (peut etre re-exporte)

# Élève (vise <30 Mo) — ajuste si trop petit/gros.
STUDENT_DIM = 384
STUDENT_LAYERS = 4
STUDENT_HEADS = 6
STUDENT_HIDDEN = 1536
DISTILL_EPOCHS = 8        # from scratch -> plus d'epochs qu'un fine-tune
LR = 5e-5
ALPHA = 0.5              # poids CE dure (1-ALPHA = KL douce)
TEMP = 3.0              # temperature distillation

GATE_F1_TEST = 0.98
GATE_F1_GOLD = 0.97
"""))

# Cellules réutilisées de v11 (testées)
cells.append(reuse("def load_jsonl"))       # FOLD + datasets (glob)
cells.append(reuse("def make_tokenize_fn"))  # alignement BIO
cells.append(reuse("def compute_metrics"))   # seqeval

cells.append(code(r"""# 7. PROF + ÉLÈVE + distillation
import torch
import torch.nn.functional as F
from transformers import (AutoTokenizer, AutoModelForTokenClassification, DistilBertConfig,
                          DistilBertForTokenClassification, TrainingArguments, Trainer,
                          DataCollatorForTokenClassification)

tok = AutoTokenizer.from_pretrained(TEACHER_DIR)               # tokenizer élagué partagé
tokd = datasets.map(make_tokenize_fn(tok), batched=True, remove_columns=['text', 'spans', 'lang'])

dev = 'cuda' if torch.cuda.is_available() else 'cpu'
teacher = AutoModelForTokenClassification.from_pretrained(TEACHER_DIR).to(dev).eval()

scfg = DistilBertConfig(
    vocab_size=tok.vocab_size, dim=STUDENT_DIM, hidden_dim=STUDENT_HIDDEN,
    n_layers=STUDENT_LAYERS, n_heads=STUDENT_HEADS, max_position_embeddings=512,
    num_labels=len(LABELS), id2label=ID2LABEL, label2id=LABEL2ID, pad_token_id=tok.pad_token_id)
student = DistilBertForTokenClassification(scfg)
print(f"Élève : {sum(p.numel() for p in student.parameters())/1e6:.0f}M params "
      f"(dim {STUDENT_DIM}, {STUDENT_LAYERS} couches) | Prof : "
      f"{sum(p.numel() for p in teacher.parameters())/1e6:.0f}M")

class DistilTrainer(Trainer):
    def compute_loss(self, model, inputs, return_outputs=False, **kw):
        out = model(**inputs)
        with torch.no_grad():
            t_logits = teacher(**{k: v for k, v in inputs.items() if k != 'labels'}).logits
        m = inputs['labels'].view(-1) != -100
        sl = out.logits.view(-1, out.logits.size(-1))[m]
        tl = t_logits.view(-1, t_logits.size(-1))[m]
        kd = F.kl_div(F.log_softmax(sl / TEMP, -1), F.softmax(tl / TEMP, -1),
                      reduction='batchmean') * (TEMP ** 2)
        loss = ALPHA * out.loss + (1 - ALPHA) * kd
        return (loss, out) if return_outputs else loss

OUT_PT = os.path.join(WORKDIR, 'pt-student')
args = TrainingArguments(
    output_dir=OUT_PT, learning_rate=LR, per_device_train_batch_size=BATCH_SIZE,
    per_device_eval_batch_size=BATCH_SIZE, num_train_epochs=DISTILL_EPOCHS, weight_decay=0.01,
    eval_strategy='epoch', save_strategy='epoch', load_best_model_at_end=True,
    metric_for_best_model='f1', logging_steps=50, report_to='none')
trainer = DistilTrainer(model=student, args=args, train_dataset=tokd['train'],
                        eval_dataset=tokd['validation'], tokenizer=tok,
                        data_collator=DataCollatorForTokenClassification(tok),
                        compute_metrics=compute_metrics)
trainer.train()
trainer.save_model(OUT_PT); tok.save_pretrained(OUT_PT)
"""))

cells.append(code(r"""# 8. Export ONNX int8 + métriques (élève ET prof)
import time
from transformers import Trainer as PlainTrainer
from optimum.onnxruntime import ORTModelForTokenClassification, ORTQuantizer
from optimum.onnxruntime.configuration import AutoQuantizationConfig

def export_quant(pt_dir, short):
    onnx_dir = os.path.join(WORKDIR, f'onnx-{short}')
    quant_dir = os.path.join(WORKDIR, f'onnx-int8-{short}')
    ORTModelForTokenClassification.from_pretrained(pt_dir, export=True).save_pretrained(onnx_dir)
    AutoTokenizer.from_pretrained(pt_dir).save_pretrained(onnx_dir)
    ORTQuantizer.from_pretrained(onnx_dir).quantize(
        save_dir=quant_dir, quantization_config=AutoQuantizationConfig.avx2(is_static=False, per_channel=False))
    AutoTokenizer.from_pretrained(pt_dir).save_pretrained(quant_dir)
    return quant_dir

def size_mb(d):
    return sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d) if f.endswith('.onnx')) / 1e6

def latency(quant_dir):
    from transformers import pipeline
    nlp = pipeline('token-classification', model=ORTModelForTokenClassification.from_pretrained(quant_dir),
                   tokenizer=AutoTokenizer.from_pretrained(quant_dir), aggregation_strategy='simple', device=-1)
    ss = ['On a f(x)=2x+1', '(1/2, x^2 ; sqrt(2), a_n)']
    for s in ss: nlp(s)
    t0 = time.time()
    for _ in range(20):
        for s in ss: nlp(s)
    return 1000 * (time.time() - t0) / (20 * len(ss))

def f1(model, name):
    tr = PlainTrainer(model=model.to(dev),
                      args=TrainingArguments(output_dir='/tmp/' + name, per_device_eval_batch_size=BATCH_SIZE, report_to='none'),
                      tokenizer=tok, data_collator=DataCollatorForTokenClassification(tok),
                      compute_metrics=compute_metrics)
    return tr.predict(tokd['test']).metrics['test_f1'], tr.predict(tokd['gold']).metrics['test_f1']

st_test, st_gold = f1(student, 'student')
st_quant = export_quant(OUT_PT, 'student')
te_test, te_gold = f1(teacher, 'teacher')
if not os.path.isdir(TEACHER_QUANT):
    TEACHER_QUANT = export_quant(TEACHER_DIR, 'distilmult-pruned')
print(f"\nÉlève : test {st_test:.4f} gold {st_gold:.4f} {size_mb(st_quant):.0f} Mo")
print(f"Prof  : test {te_test:.4f} gold {te_gold:.4f} {size_mb(TEACHER_QUANT):.0f} Mo")
"""))

cells.append(reuse("PROBES = ["))   # sondes comportementales (testées)

cells.append(code(r"""# 10. ARBITRAGE : prof (46 Mo) vs élève (<30 Mo vise)
THEMES = ['matrice', 'tuple', 'keyword', 'homonyme', 'intervalle', 'multi', 'prose']
CRITICAL = ['homonyme', 'matrice', 'keyword']

results = [
    {'short': 'teacher-pruned', 'f1_test': te_test, 'f1_gold': te_gold,
     'size_mb': size_mb(TEACHER_QUANT), 'latency_ms': latency(TEACHER_QUANT), 'quant_dir': TEACHER_QUANT},
    {'short': 'student-distill', 'f1_test': st_test, 'f1_gold': st_gold,
     'size_mb': size_mb(st_quant), 'latency_ms': latency(st_quant), 'quant_dir': st_quant},
]
for r in results:
    r['probes'], r['fails'] = score_probes(r['quant_dir'])

print('=' * 100)
print('  ARBITRAGE DISTILLATION')
print('=' * 100)
hdr = (f"{'modele':16} {'F1test':>7} {'F1gold':>7} {'Mo':>5} {'ms':>5} {'sondes':>7}  "
       + " ".join(f'{t[:5]:>5}' for t in THEMES))
print(hdr); print('-' * len(hdr))
for r in results:
    pr = r['probes']
    print(f"{r['short']:16} {r['f1_test']:>7.4f} {r['f1_gold']:>7.4f} {r['size_mb']:>5.0f} "
          f"{r['latency_ms']:>5.1f} {pr['_overall']*100:>6.0f}%  "
          + " ".join(f"{pr.get(t,0)*100:>4.0f}%" for t in THEMES))

def passes(r):
    return (r['f1_test'] >= GATE_F1_TEST and r['f1_gold'] >= GATE_F1_GOLD
            and all(r['probes'].get(t, 0) >= 0.90 for t in CRITICAL))

stu = results[1]
print('\n' + '-' * 100)
if passes(stu):
    print(f"  RECO : ADOPTER l'élève distillé ({stu['size_mb']:.0f} Mo, gold {stu['f1_gold']:.4f}, "
          f"sondes {stu['probes']['_overall']*100:.0f}%) — il passe la gate ET les thèmes critiques.")
else:
    print(f"  RECO : GARDER le prof (46 Mo). L'élève ne passe pas "
          f"(gold {stu['f1_gold']:.4f}, sondes {stu['probes']['_overall']*100:.0f}%).")
    print(f"  -> pour l'améliorer : STUDENT_LAYERS/DIM plus grands, DISTILL_EPOCHS plus, ou ALPHA/TEMP.")
print('\n  Sondes ratées de l\'élève :')
for theme, text, exp, got in stu['fails']:
    print(f"    [{theme}] {text!r}  attendu={exp}  prédit={got}")
"""))

nb = {"cells": cells,
      "metadata": {"kernelspec": {"display_name": "Python 3", "name": "python3"},
                   "language_info": {"name": "python"}},
      "nbformat": 4, "nbformat_minor": 5}
OUT.write_text(json.dumps(nb, ensure_ascii=False, indent=1), encoding="utf-8")
print(f"Écrit : {OUT.name}  ({len(cells)} cellules)")
