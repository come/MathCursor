import { createApp } from "vue";
import App from "./App.vue";
import { startWatcher } from "./watcher";

Office.onReady(() => {
  createApp(App).mount("#app");
  startWatcher();
});
