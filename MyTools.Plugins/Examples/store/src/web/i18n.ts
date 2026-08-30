import { ref } from "vue";
import { HostEvents } from "@qping/plugin-bus/web";
import { bus } from "./bus";

export const localeRevision = ref(0);
bus.on(HostEvents.Initialize, () => { localeRevision.value += 1; });
bus.on(HostEvents.LanguageChanged, () => { localeRevision.value += 1; });

export function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
    void localeRevision.value;
    return bus.i18n.t(key, { defaultValue, ...values });
}
