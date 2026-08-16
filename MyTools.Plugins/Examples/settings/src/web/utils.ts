import { mytoolsI18n } from "@qping/plugin-bus/i18n";

export function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
    return mytoolsI18n.t(key, { defaultValue: defaultValue, ...values });
}

export function escapeHtml(text: string): string {
    var div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
}

export function highlight(text: string, query: string): string {
    if (!query) return escapeHtml(text);
    var lower = text.toLowerCase();
    var idx = lower.indexOf(query);
    if (idx < 0) return escapeHtml(text);
    return escapeHtml(text.slice(0, idx))
        + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
        + highlightRemaining(text.slice(idx + query.length), query);
}

function highlightRemaining(text: string, query: string): string {
    var lower = text.toLowerCase();
    var idx = lower.indexOf(query);
    if (idx < 0) return escapeHtml(text);
    return escapeHtml(text.slice(0, idx))
        + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
        + highlightRemaining(text.slice(idx + query.length), query);
}
