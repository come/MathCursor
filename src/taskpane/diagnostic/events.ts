// Diagnostic : enregistre des listeners sur les events Word et logge tout.
// Permet d'observer en direct ce qui fire sur undo, click, typing, etc.
// Active via startEventDiagnostic() dans main.ts.

import { ref } from "vue";

export interface LoggedEvent {
  t: number;              // timestamp relatif (ms depuis démarrage)
  type: string;           // "selectionChanged", "paraChanged", ...
  source?: string;        // "Local" / "Remote" si disponible
  detail?: string;        // métadonnées formatées
}

const startedAt = Date.now();
export const eventLog = ref<LoggedEvent[]>([]);
const MAX_LOG_SIZE = 50;

function push(ev: LoggedEvent): void {
  eventLog.value.push(ev);
  if (eventLog.value.length > MAX_LOG_SIZE) {
    eventLog.value.splice(0, eventLog.value.length - MAX_LOG_SIZE);
  }
  // Double logging : console pour DevTools + UI pour task pane
  console.log(`[EVT] +${ev.t}ms ${ev.type}${ev.source ? ` [${ev.source}]` : ""}${ev.detail ? ` ${ev.detail}` : ""}`);
}

function rel(): number { return Date.now() - startedAt; }

export function clearEventLog(): void {
  eventLog.value = [];
}

export async function startEventDiagnostic(): Promise<void> {
  if (typeof Word === "undefined" || !Word.run) {
    console.warn("[EVT] Word API non disponible, diagnostic inactif");
    return;
  }

  // --- DocumentSelectionChanged : Office-level (seul event selection dispo en Word) ---
  // ⚠️ addHandlerAsync NÉCESSITE un callback — sans lui, l'enregistrement silencieux échoue.
  const selHandler = async () => {
    try {
      await Word.run(async (c) => {
        const sel = c.document.getSelection();
        sel.load("text");
        sel.font.load("name");
        await c.sync();
        const font = sel.font.name ?? "?";
        const selText = (sel.text ?? "").slice(0, 30).replace(/\n/g, "\\n");
        push({
          t: rel(),
          type: "selectionChanged",
          detail: `font="${font}" sel="${selText}"`,
        });
      });
    } catch (e) {
      push({ t: rel(), type: "selectionChanged", detail: `ERR ${(e as Error).message}` });
    }
  };

  Office.context.document.addHandlerAsync(
    Office.EventType.DocumentSelectionChanged,
    selHandler,
    (result) => {
      if (result.status === Office.AsyncResultStatus.Succeeded) {
        push({ t: rel(), type: "diagnostic", detail: "selectionChanged handler OK" });
      } else {
        push({ t: rel(), type: "diagnostic", detail: `selectionChanged handler FAIL: ${result.error?.message}` });
      }
    },
  );

  try {
    await Word.run(async (ctx) => {
      const doc = ctx.document;

      // --- onParagraphAdded ---
      doc.onParagraphAdded.add(async (args) => {
        push({
          t: rel(),
          type: "paraAdded",
          source: args.source,
          detail: `count=${args.uniqueLocalIds?.length ?? 0}`,
        });
      });

      // --- onParagraphChanged ---
      doc.onParagraphChanged.add(async (args) => {
        try {
          await Word.run(async (c) => {
            // Tenter de lire le texte du paragraphe modifié
            const ids = args.uniqueLocalIds ?? [];
            push({
              t: rel(),
              type: "paraChanged",
              source: args.source,
              detail: `ids=[${ids.slice(0, 2).join(",")}${ids.length > 2 ? "..." : ""}] n=${ids.length}`,
            });
            await c.sync();
          });
        } catch (e) {
          push({ t: rel(), type: "paraChanged", detail: `ERR ${(e as Error).message}` });
        }
      });

      // --- onParagraphDeleted ---
      doc.onParagraphDeleted.add(async (args) => {
        push({
          t: rel(),
          type: "paraDeleted",
          source: args.source,
          detail: `count=${args.uniqueLocalIds?.length ?? 0}`,
        });
      });

      // --- onContentControlAdded ---
      doc.onContentControlAdded.add(async (args) => {
        push({
          t: rel(),
          type: "contentControlAdded",
          source: args.source,
          detail: `ids=[${(args.ids ?? []).slice(0, 2).join(",")}]`,
        });
      });

      await ctx.sync();
      push({ t: rel(), type: "diagnostic", detail: "listeners enregistrés" });
    });
  } catch (e) {
    console.error("[EVT] Échec enregistrement listeners:", e);
    push({ t: rel(), type: "diagnostic", detail: `ERR init ${(e as Error).message}` });
  }
}
