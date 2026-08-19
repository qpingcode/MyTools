import { createApp } from "vue";
import { create, NButton, NCard, NCheckbox, NConfigProvider, NInput, NModal, NSelect, NSpin, NSwitch } from "naive-ui";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";
import "./app.css";

const naive = create({
    components: [
        NConfigProvider,
        NButton,
        NInput,
        NSelect,
        NSwitch,
        NCheckbox,
        NModal,
        NCard,
        NSpin,
    ],
});

createApp(App).use(naive).mount("#app");
