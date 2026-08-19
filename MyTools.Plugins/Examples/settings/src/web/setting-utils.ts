export type PathKind = "file" | "directory" | "fileOrDirectory";

const IlSpyPathFullPath = "dllinterfacereader.ilspypathsetting";

export function defaultUiHint(type: string, uiHint?: string | null): string {
    if (uiHint && uiHint.trim()) return uiHint.trim().toLowerCase();
    const normalized = (type || "").trim().toLowerCase();
    if (normalized === "bool") return "checkbox";
    if (normalized === "int" || normalized === "integer" || normalized === "double") return "input-number";
    if (normalized === "array") return "table";
    if (normalized === "path") return "fileordirectory";
    if (normalized === "hidden") return "";
    return "input";
}

export function isPathType(type?: string | null, fullPath?: string | null): boolean {
    if ((type || "").trim().toLowerCase() === "path") return true;
    return (fullPath || "").toLowerCase() === IlSpyPathFullPath;
}

export function normalizePathKind(uiHint?: string | null): PathKind {
    const hint = (uiHint || "").trim().toLowerCase();
    if (hint === "file") return "file";
    if (hint === "directory") return "directory";
    return "fileOrDirectory";
}

export function resolvePathKind(
    type?: string | null,
    uiHint?: string | null,
    fullPath?: string | null,
): PathKind {
    if ((fullPath || "").toLowerCase() === IlSpyPathFullPath) return "file";
    return normalizePathKind(uiHint);
}

export function resolveMacros(value: string | undefined | null): string {
    if (!value) return "";
    return value.replaceAll("${DateTime.Now}", new Date().toISOString());
}

export function parseArrayValue(raw: string | undefined | null): Record<string, unknown>[] {
    if (!raw) return [];
    try {
        const parsed = JSON.parse(raw);
        return Array.isArray(parsed) ? parsed.map((item) => (item && typeof item === "object" ? item : {})) : [];
    } catch {
        return [];
    }
}

export function coercePropertyValue(type: string, raw: unknown): unknown {
    const normalized = (type || "string").toLowerCase();
    if (normalized === "bool") {
        if (typeof raw === "boolean") return raw;
        return raw === "True" || raw === "true" || raw === true;
    }
    if (normalized === "int" || normalized === "integer") {
        const value = typeof raw === "number" ? raw : Number.parseInt(String(raw ?? "0"), 10);
        return Number.isFinite(value) ? value : 0;
    }
    if (normalized === "double") {
        const value = typeof raw === "number" ? raw : Number.parseFloat(String(raw ?? "0"));
        return Number.isFinite(value) ? value : 0;
    }
    if (Array.isArray(raw)) {
        return raw.map((item) => String(item ?? "")).join("\n");
    }
    return raw == null ? "" : String(raw);
}

export function defaultPropertyValue(type: string, defaultValue?: string): unknown {
    const normalized = (type || "string").toLowerCase();
    const resolved = resolveMacros(defaultValue);
    if (normalized === "bool") return coercePropertyValue(type, resolved || "False");
    if (normalized === "int" || normalized === "integer" || normalized === "double") {
        return coercePropertyValue(type, resolved || "0");
    }
    return resolved;
}

export function formatCellText(value: unknown): string {
    if (value == null) return "";
    if (typeof value === "boolean") return value ? "true" : "false";
    return String(value);
}

export function isTruthyBool(value: unknown): boolean {
    return value === true || value === "True" || value === "true";
}
