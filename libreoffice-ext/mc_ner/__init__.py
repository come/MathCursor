# -*- coding: utf-8 -*-
"""mc_ner — détection de zones math (NER) pour l'extension LibreOffice.

Port Python du pipeline Word (`MathNerDetector` + `WordPieceTokenizer` +
`ZoneRefiner` + `NerInputWindow`). Modèle ONNX `distilmult-v6` (DistilBERT
multilingue, 3 labels BIO).

`load_detector` importe numpy/onnxruntime **paresseusement** : si les deps
natives (vendor) ou le modèle sont absents, renvoie None — l'extension charge
quand même et Ctrl+Espace continue de marcher (dégradation gracieuse, parité
avec `AutoDetectController.AttachDetector` côté Word).
"""
from .zone import DetectedZone


def load_detector(model_dir, threshold=0.85):
    """Construit le Detector ONNX, ou None si indisponible.

    `model_dir` doit contenir `model_quantized.onnx` + `vocab.txt`. numpy et
    onnxruntime doivent être importables (vendor dir déjà sur sys.path)."""
    import os

    onnx_path = os.path.join(model_dir, "model_quantized.onnx")
    vocab_path = os.path.join(model_dir, "vocab.txt")
    if not (os.path.exists(onnx_path) and os.path.exists(vocab_path)):
        return None
    try:
        from .detector import Detector
        return Detector(onnx_path, vocab_path, threshold=threshold)
    except Exception:
        return None


__all__ = ["DetectedZone", "load_detector"]
