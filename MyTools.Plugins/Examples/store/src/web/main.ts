import { createApp } from "vue";
import { create, NButton, NCard, NConfigProvider, NEmpty, NInput, NSpin, NTabPane, NTabs } from "naive-ui";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";
import "./app.css";

const naive = create({
    components: [NConfigProvider, NButton, NInput, NSpin, NEmpty, NCard, NTabs, NTabPane],
});
createApp(App).use(naive).mount("#app");
