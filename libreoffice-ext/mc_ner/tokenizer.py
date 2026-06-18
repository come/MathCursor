# -*- coding: utf-8 -*-
"""Tokenizer WordPiece (BERT/DistilBERT) — port pur Python de
`WordPieceTokenizer.cs`. Modèle : distilbert-base-multilingual-cased (CASED).

- pré-tokenization sur whitespace + ponctuation (chaque ponct = son token),
- greedy longest-match avec sous-mots `##`, [UNK] si aucun préfixe,
- [CLS] en tête, [SEP] en fin, offsets caractères conservés.

Stdlib uniquement (aucune dépendance native)."""
import unicodedata

PAD_ID = 0
UNK_ID = 100
CLS_ID = 101
SEP_ID = 102
MASK_ID = 103

_SUBWORD = "##"
_MAX_CHARS_PER_WORD = 100


def load_vocab(path):
    """vocab.txt HuggingFace : un token par ligne, id = numéro de ligne."""
    vocab = {}
    with open(path, "r", encoding="utf-8") as f:
        for i, line in enumerate(f):
            tok = line.rstrip("\n")
            if tok not in vocab:
                vocab[tok] = i
    return vocab


def _is_punctuation(c):
    """Convention BERT : tout ce qui n'est ni lettre, ni chiffre, ni underscore
    est ponctuation (et devient son propre token)."""
    cp = ord(c)
    if (33 <= cp <= 47) or (58 <= cp <= 64) or (91 <= cp <= 96) or (123 <= cp <= 126):
        return True
    return unicodedata.category(c).startswith("P")


def _pre_tokenize(text):
    """Split sur whitespace + ponctuation, garde les offsets (start, end)."""
    out = []
    buf = []
    start = -1
    for i, c in enumerate(text):
        if c.isspace():
            if buf:
                out.append(("".join(buf), start, i))
                buf, start = [], -1
        elif _is_punctuation(c):
            if buf:
                out.append(("".join(buf), start, i))
                buf, start = [], -1
            out.append((c, i, i + 1))
        else:
            if not buf:
                start = i
            buf.append(c)
    if buf:
        out.append(("".join(buf), start, len(text)))
    return out


class Token:
    __slots__ = ("id", "char_start", "char_end")

    def __init__(self, tid, cs, ce):
        self.id = tid
        self.char_start = cs
        self.char_end = ce


def encode(text, vocab):
    """Tokenize `text` → liste de Token (avec [CLS]/[SEP] et offsets)."""
    if text is None:
        text = ""
    toks = [Token(CLS_ID, 0, 0)]
    for word, ws, we in _pre_tokenize(text):
        if len(word) > _MAX_CHARS_PER_WORD:
            toks.append(Token(UNK_ID, ws, we))
            continue
        sub = []
        start = 0
        ok = True
        while start < len(word):
            end = len(word)
            matched = None
            mid = -1
            while start < end:
                piece = word[start:end] if start == 0 else _SUBWORD + word[start:end]
                if piece in vocab:
                    matched = piece
                    mid = vocab[piece]
                    break
                end -= 1
            if matched is None:
                toks.append(Token(UNK_ID, ws, we))
                ok = False
                break
            sublen = len(matched) if start == 0 else len(matched) - len(_SUBWORD)
            sub.append(Token(mid, ws + start, ws + start + sublen))
            start += sublen
        if ok:
            toks.extend(sub)
    toks.append(Token(SEP_ID, len(text), len(text)))
    return toks
