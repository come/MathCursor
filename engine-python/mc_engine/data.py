"""Chargement de la data universelle du moteur (data/engine/*.json).

MÊME source que le moteur C# (qui l'embarque en EmbeddedResource) — ici on lit
les fichiers directement. Cf. ADR 2026-06-16-Feat-portable-engine-universal-vocab.
"""
import json
from pathlib import Path
from functools import lru_cache

# engine-python/mc_engine/data.py -> parents[2] = racine du repo
_DATA_DIR = Path(__file__).resolve().parents[2] / "data" / "engine"
_override = None


def set_data_dir(path):
    """Pointe le chargeur vers un autre dossier data/engine (ex. data embarquée
    dans un .oxt LibreOffice). À appeler AVANT le premier import de vocab."""
    global _override
    _override = Path(path)
    load.cache_clear()


@lru_cache(maxsize=None)
def load(name: str) -> dict:
    base = _override if _override is not None else _DATA_DIR
    with open(base / name, encoding="utf-8") as f:
        return json.load(f)
