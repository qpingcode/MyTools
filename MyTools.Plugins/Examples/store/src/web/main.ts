import { createApp } from "vue";
import { create, NButton, NCard, NConfigProvider, NEmpty, NInput, NSpin } from "naive-ui";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";
import "./app.css";

const naive = create({
    components: [NConfigProvider, NButton, NInput, NSpin, NEmpty, NCard],
});
createApp(App).use(naive).mount("#app");
