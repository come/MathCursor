"""Culture moteur — port d'EngineCulture.cs (réglages + alias).

Construite depuis data/engine/cultures.json. MergeAliases valide que chaque
cible d'alias est une clé canonique de Vocab.
"""
from . import data
from .vocab import VOCAB


def _merge(generic: dict, lang: dict) -> dict:
    merged = dict(generic)
    merged.update(lang)
    for k, v in merged.items():
        if v not in VOCAB:
            raise ValueError(f"alias '{k}' -> cible inconnue '{v}'")
    return merged


class Culture:
    def __init__(self, decimals_in, decimal_tex, interval_sep, matrix_env, aliases):
        self.decimals_in = decimals_in          # list[str] (chars)
        self.decimal_tex = decimal_tex
        self.interval_sep = interval_sep
        self.matrix_env = matrix_env
        self.aliases = aliases

    def canon(self, w: str) -> str:
        return self.aliases.get(w, w)


_C = data.load("cultures.json")
_AL = _C["aliases"]


def _make(key: str, lang_key: str) -> Culture:
    c = _C["cultures"][key]
    return Culture(list(c["decimalsIn"]), c["decimalTex"], c["intervalSep"], c["matrixEnv"],
                   _merge(_AL["generic"], _AL[lang_key]))


FR = _make("fr", "fr")
US = _make("us", "en")
