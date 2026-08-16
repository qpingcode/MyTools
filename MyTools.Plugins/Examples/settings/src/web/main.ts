import { createApp } from "vue";
import { createVuetify } from "vuetify";
import { aliases, mdi } from "vuetify/iconsets/mdi";
import "vuetify/styles";
import "@mdi/font/css/materialdesignicons.css";
import App from "./App.vue";
import { isDarkTheme, readThemeColors } from "./theme";
import "./app.css";

const dark = isDarkTheme();
const colors = readThemeColors();

const vuetify = createVuetify({
    icons: {
        defaultSet: "mdi",
        aliases,
        sets: { mdi },
    },
    theme: {
        defaultTheme: dark ? "dark" : "light",
        themes: {
            light: { dark: false, colors },
            dark: { dark: true, colors },
        },
    },
    defaults: {
        VTextField: { density: "compact", variant: "solo", hideDetails: "auto" },
        VSelect: { density: "compact", variant: "solo", hideDetails: "auto" },
        VTextarea: { density: "compact", variant: "solo", hideDetails: "auto" },
        VBtn: { variant: "flat", size: "small", rounded: "lg" },
        VCheckbox: { density: "compact", hideDetails: true, color: "primary" },
        VSwitch: { density: "compact", hideDetails: true, color: "primary", inset: true },
        VList: { density: "compact" },
    },
});

createApp(App).use(vuetify).mount("#app");
