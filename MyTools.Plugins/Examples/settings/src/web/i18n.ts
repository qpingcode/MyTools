import { ref } from "vue";
import { HostEvents } from "@qping/plugin-bus/web";
import { bus } from "./bus";

/** Bumped after host Initialize / LanguageChanged so Vue templates re-run t(). */
export const localeRevision = ref(0);

export function notifyLocaleChanged(): void {
    localeRevision.value += 1;
}

bus.on(HostEvents.Initialize, () => notifyLocaleChanged());
bus.on(HostEvents.LanguageChanged, () => notifyLocaleChanged());

export function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
    void localeRevision.value;
    return bus.i18n.t(key, { defaultValue, ...values });
}

export function escapeHtml(text: string): string {
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

export function highlight(text: string, query: string): string {
    if (!query) return escapeHtml(text);
    const lower = text.toLowerCase();
    const idx = lower.indexOf(query);
    if (idx < 0) return escapeHtml(text);
    return escapeHtml(text.slice(0, idx))
        + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
        + highlight(text.slice(idx + query.length), query);
}
