# -*- coding: utf-8 -*-
"""DetectedZone : une zone math localisée dans un texte (offsets caractères)."""


class DetectedZone:
    """Zone math : [start, end) dans le texte, le sous-texte, la confiance moyenne."""

    __slots__ = ("start", "end", "text", "confidence")

    def __init__(self, start, end, text, confidence):
        self.start = start
        self.end = end
        self.text = text
        self.confidence = confidence

    def __repr__(self):
        return "DetectedZone(%d,%d,%r,%.2f)" % (self.start, self.end, self.text, self.confidence)
