"""Chargement de la data universelle du moteur (data/engine/*.json).

MÊME source que le moteur C# (qui l'embarque en EmbeddedResource) — ici on lit
les fichiers directement. Cf. ADR 2026-06-16-Feat-portable-engine-universal-vocab.
"""
import json
from pathlib import Path
from functools import lru_cache

# engine-python/mc_engine/data.py -> parents[2] = racine du repo
_DATA_DIR = Path(__file__).resolve().parents[2] / "data" / "engine"


@lru_cache(maxsize=None)
def load(name: str) -> dict:
    with open(_DATA_DIR / name, encoding="utf-8") as f:
        return json.load(f)
