# -*- coding: utf-8 -*-
"""Composant UNO Job : à l'ouverture d'un document Writer, active
l'auto-détection MathCursor (zéro-clic). Le Job ne fait qu'invoquer le script
`autodetect_autostart` via le ScriptProvider du document (qui établit le
XSCRIPTCONTEXT attendu par mathcursor.py)."""
import uno
import unohelper
from com.sun.star.task import XJob

_IMPL = "fr.mathcursor.AutoStartJob"
_SCRIPT = ("vnd.sun.star.script:MathCursor.oxt|Scripts|python|mathcursor.py$"
           "autodetect_autostart?language=Python&location=user:uno_packages")


class AutoStartJob(unohelper.Base, XJob):
    def __init__(self, ctx):
        self.ctx = ctx

    def execute(self, args):
        model = None
        try:
            for nv in args:
                if nv.Name == "Environment":
                    for e in nv.Value:
                        if e.Name == "Model":
                            model = e.Value
        except Exception:
            return None
        if model is None:
            return None
        # uniquement les documents texte (Writer).
        try:
            if not model.supportsService("com.sun.star.text.TextDocument"):
                return None
        except Exception:
            return None
        try:
            spf = self.ctx.ServiceManager.createInstanceWithContext(
                "com.sun.star.script.provider.MasterScriptProviderFactory", self.ctx)
            sp = spf.createScriptProvider(model)
            sp.getScript(_SCRIPT).invoke((), (), ())
        except Exception:
            pass
        return None


g_ImplementationHelper = unohelper.ImplementationHelper()
g_ImplementationHelper.addImplementation(AutoStartJob, _IMPL, ("com.sun.star.task.Job",))
