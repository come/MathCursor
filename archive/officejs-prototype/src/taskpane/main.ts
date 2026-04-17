import { createApp } from "vue";
import App from "./App.vue";
import { startWatcher } from "./watcher";
import { startEventDiagnostic } from "./diagnostic/events";

Office.onReady(() => {
  createApp(App).mount("#app");
  startWatcher();
  startEventDiagnostic();
});
