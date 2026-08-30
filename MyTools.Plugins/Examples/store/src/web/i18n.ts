import { ref } from "vue";
import { HostEvents } from "@qping/plugin-bus/web";
import { bus } from "./bus";

export const localeRevision = ref(0);
export const currentLocale = ref("en-US");

function applyLocale(payload: { locale?: string } | undefined): void {
    if (typeof payload?.locale === "string" && payload.locale) {
        currentLocale.value = payload.locale;
    }
    localeRevision.value += 1;
}

bus.on(HostEvents.Initialize, (payload: { locale?: string }) => applyLocale(payload));
bus.on(HostEvents.LanguageChanged, (payload: { locale?: string }) => applyLocale(payload));

export function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
    void localeRevision.value;
    return bus.i18n.t(key, { defaultValue, ...values });
}
