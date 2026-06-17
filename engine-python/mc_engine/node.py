"""Node de la forêt (AST) — port de Node.cs.

Types : atom | infix | prefix | nary | postfix | matrix | interval | list | set
| tuple | paren.
"""
from dataclasses import dataclass, field
from typing import Optional


@dataclass
class Node:
    type: str = ""
    sym: Optional[str] = None
    parts: Optional[list] = None
    rows: Optional[list] = None          # matrix : lignes de cellules
    spaced: bool = False
    implicit: bool = False
    grouped: bool = False
    hole: bool = False
    choice: bool = False
    sig: Optional[str] = None
    coh: Optional[str] = None
    ai: int = 0
    delim: Optional[str] = None
    alt: bool = False
    lb: Optional[str] = None             # interval/paren : délimiteur gauche
    rb: Optional[str] = None             # interval/paren : délimiteur droit


@dataclass
class Token:
    """Sortie du lexer — port de Token.cs."""
    kind: str = ""
    sym: Optional[str] = None
    syms: Optional[list] = None          # lectures multiples (x2 → ^/_ ; R → [R, ℝ])
    spaced: bool = False
    space_before: bool = False
    virtual: bool = False                # ) ou ] ajouté en fin de frappe
    num: bool = False
    sticky: bool = False
    implicit: bool = False
    rank: int = 0                        # sep : 0 = "," , 2 = ";"
    unit_word: bool = False
    coh: Optional[str] = None
