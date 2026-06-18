# -*- coding: utf-8 -*-
"""Detector NER — port de `MathNerDetector.cs`.

tokenize → ONNX (input_ids, attention_mask → logits [1,n,3]) → softmax/argmax
par token → décodage BIO (O=0, B-MATH=1, I-MATH=2) en spans à offsets
caractères → filtre seuil. numpy + onnxruntime requis (imports au niveau module :
n'importer ce fichier QUE via `mc_ner.load_detector`, qui gère l'absence)."""
import numpy as np
import onnxruntime as ort

from . import tokenizer as tok
from .zone import DetectedZone

_MAX_TOKENS = 128
_LABEL_O = 0
_LABEL_B = 1
_LABEL_I = 2
_SPECIAL = (tok.PAD_ID, tok.CLS_ID, tok.SEP_ID, tok.MASK_ID)


class Detector:
    def __init__(self, onnx_path, vocab_path, threshold=0.85):
        self._vocab = tok.load_vocab(vocab_path)
        opts = ort.SessionOptions()
        opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        opts.intra_op_num_threads = 2
        self._sess = ort.InferenceSession(
            onnx_path, sess_options=opts, providers=["CPUExecutionProvider"])
        self._threshold = threshold

    def detect(self, text):
        """text → list[DetectedZone] (confiance ≥ seuil)."""
        if not text:
            return []
        toks = tok.encode(text, self._vocab)
        if not toks:
            return []
        n = min(len(toks), _MAX_TOKENS)
        ids = np.array([[t.id for t in toks[:n]]], dtype=np.int64)
        mask = np.ones((1, n), dtype=np.int64)
        logits = self._sess.run(
            None, {"input_ids": ids, "attention_mask": mask})[0][0]  # [n,3]

        spans = []
        cur_start = None
        cur_end = 0
        conf_sum = 0.0
        conf_n = 0
        for i in range(n):
            t = toks[i]
            if t.id in _SPECIAL:
                continue
            row = logits[i]
            m = row.max()
            e = np.exp(row - m)
            p = e / e.sum()
            label = int(p.argmax())
            conf = float(p[label])
            if label == _LABEL_B:
                if cur_start is not None:
                    spans.append(self._build(text, cur_start, cur_end, conf_sum, conf_n))
                cur_start = t.char_start
                cur_end = t.char_end
                conf_sum = conf
                conf_n = 1
            elif label == _LABEL_I and cur_start is not None:
                cur_end = t.char_end
                conf_sum += conf
                conf_n += 1
            elif label == _LABEL_O and cur_start is not None:
                spans.append(self._build(text, cur_start, cur_end, conf_sum, conf_n))
                cur_start = None
                conf_sum = 0.0
                conf_n = 0
        if cur_start is not None:
            spans.append(self._build(text, cur_start, cur_end, conf_sum, conf_n))

        return [z for z in spans if z.confidence >= self._threshold]

    @staticmethod
    def _build(text, start, end, conf_sum, count):
        s = max(0, min(start, len(text)))
        e = max(s, min(end, len(text)))
        conf = conf_sum / count if count > 0 else 0.0
        return DetectedZone(s, e, text[s:e], conf)
