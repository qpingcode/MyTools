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

export function isHeadingType(type?: string | null): boolean {
    const normalized = (type || "").trim().toLowerCase();
    return normalized === "h1" || normalized === "h2";
}

export function isTopLevelHeading(type?: string | null): boolean {
    return (type || "").trim().toLowerCase() === "h1";
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

export function settingKey(setting: { fullPath: string }): string {
    const path = setting.fullPath || "";
    const index = path.lastIndexOf(".");
    return index >= 0 ? path.slice(index + 1) : path;
}

/**
 * Evaluates a configuration `visibility` condition such as `${ChromeEnabled == true}`.
 * Empty or missing macros are visible. Sibling keys of the current plugin are resolved
 * through `lookup`.
 */
export function evaluateVisibility(
    visibility: string | undefined | null,
    lookup: (name: string) => unknown,
): boolean {
    if (!visibility || !visibility.trim()) return true;
    const expression = extractSingleMacro(visibility);
    if (expression == null) return true;
    if (!expression.trim()) return true;
    try {
        return isTruthy(new VisibilityParser(expression, lookup).parseExpression());
    } catch {
        return true;
    }
}

function extractSingleMacro(value: string): string | null {
    const trimmed = value.trim();
    if (trimmed.length < 3 || !trimmed.startsWith("${") || !trimmed.endsWith("}")) return null;
    const inner = trimmed.slice(2, -1);
    if (inner.includes("${")) return null;
    return inner.trim();
}

class VisibilityParser {
    private index = 0;

    constructor(
        private readonly source: string,
        private readonly lookup: (name: string) => unknown,
    ) {}

    parseExpression(): unknown {
        const value = this.parseOr();
        this.skipWhitespace();
        if (!this.isAtEnd()) throw new Error("Unexpected input after expression");
        return value;
    }

    private parseOr(): unknown {
        let left = this.parseAnd();
        while (this.match("||")) {
            left = isTruthy(left) || isTruthy(this.parseAnd());
        }
        return left;
    }

    private parseAnd(): unknown {
        let left = this.parseEquality();
        while (this.match("&&")) {
            left = isTruthy(left) && isTruthy(this.parseEquality());
        }
        return left;
    }

    private parseEquality(): unknown {
        const left = this.parsePrimary();
        if (this.match("==")) return valuesEqual(left, this.parsePrimary());
        if (this.match("!=")) return !valuesEqual(left, this.parsePrimary());
        return left;
    }

    private parsePrimary(): unknown {
        this.skipWhitespace();
        if (this.match("(")) {
            const inner = this.parseOr();
            if (!this.match(")")) throw new Error("Missing ')'");
            return inner;
        }
        if (this.matchKeyword("true")) return true;
        if (this.matchKeyword("false")) return false;
        const text = this.tryReadString();
        if (text !== undefined) return text;
        const number = this.tryReadNumber();
        if (number !== undefined) return number;
        const name = this.tryReadIdentifier();
        if (name !== undefined) return this.lookup(name);
        throw new Error(`Unexpected token at ${this.index}`);
    }

    private match(token: string): boolean {
        this.skipWhitespace();
        if (!this.source.startsWith(token, this.index)) return false;
        this.index += token.length;
        return true;
    }

    private matchKeyword(keyword: string): boolean {
        this.skipWhitespace();
        const slice = this.source.slice(this.index, this.index + keyword.length);
        if (slice.toLowerCase() !== keyword.toLowerCase()) return false;
        const next = this.index + keyword.length;
        const ch = this.source[next];
        if (ch && /[A-Za-z0-9_.]/.test(ch)) return false;
        this.index = next;
        return true;
    }

    private tryReadIdentifier(): string | undefined {
        this.skipWhitespace();
        const start = this.index;
        const first = this.source[this.index];
        if (!first || !/[A-Za-z_]/.test(first)) return undefined;
        this.index++;
        while (this.index < this.source.length && /[A-Za-z0-9_.]/.test(this.source[this.index]!)) {
            this.index++;
        }
        return this.source.slice(start, this.index);
    }

    private tryReadString(): string | undefined {
        this.skipWhitespace();
        const quote = this.source[this.index];
        if (quote !== '"' && quote !== "'") return undefined;
        this.index++;
        let text = "";
        while (this.index < this.source.length && this.source[this.index] !== quote) {
            text += this.source[this.index++];
        }
        if (this.isAtEnd()) throw new Error("Unterminated string");
        this.index++;
        return text;
    }

    private tryReadNumber(): number | undefined {
        this.skipWhitespace();
        const start = this.index;
        if (this.source[this.index] === "-") this.index++;
        let digits = 0;
        while (this.index < this.source.length && /\d/.test(this.source[this.index]!)) {
            digits++;
            this.index++;
        }
        if (this.source[this.index] === ".") {
            this.index++;
            while (this.index < this.source.length && /\d/.test(this.source[this.index]!)) {
                digits++;
                this.index++;
            }
        }
        if (digits === 0) {
            this.index = start;
            return undefined;
        }
        return Number.parseFloat(this.source.slice(start, this.index));
    }

    private skipWhitespace(): void {
        while (this.index < this.source.length && /\s/.test(this.source[this.index]!)) this.index++;
    }

    private isAtEnd(): boolean {
        return this.index >= this.source.length;
    }
}

function valuesEqual(left: unknown, right: unknown): boolean {
    const leftBool = coerceBool(left);
    const rightBool = coerceBool(right);
    if (leftBool !== undefined && rightBool !== undefined) return leftBool === rightBool;
    const leftNumber = coerceNumber(left);
    const rightNumber = coerceNumber(right);
    if (leftNumber !== undefined && rightNumber !== undefined) {
        return Math.abs(leftNumber - rightNumber) < 0.0000001;
    }
    return stringValue(left).toLowerCase() === stringValue(right).toLowerCase();
}

function isTruthy(value: unknown): boolean {
    const boolean = coerceBool(value);
    if (boolean !== undefined) return boolean;
    const number = coerceNumber(value);
    if (number !== undefined) return Math.abs(number) > 0.0000001;
    return stringValue(value).trim().length > 0;
}

function coerceBool(value: unknown): boolean | undefined {
    if (typeof value === "boolean") return value;
    if (typeof value === "string") {
        const text = value.trim().toLowerCase();
        if (text === "true") return true;
        if (text === "false") return false;
    }
    return undefined;
}

function coerceNumber(value: unknown): number | undefined {
    if (typeof value === "number" && Number.isFinite(value)) return value;
    if (typeof value === "string" && value.trim() && !/^(true|false)$/i.test(value.trim())) {
        const parsed = Number.parseFloat(value);
        if (Number.isFinite(parsed)) return parsed;
    }
    return undefined;
}

function stringValue(value: unknown): string {
    if (value == null) return "";
    if (typeof value === "boolean") return value ? "true" : "false";
    return String(value);
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
