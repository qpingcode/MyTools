import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

(function () {
    const bus = createWebBusClient();
    var inputElement = document.getElementById("input") as HTMLTextAreaElement;
    var outputElement = document.getElementById("output") as HTMLElement;
    var indentSelect = document.getElementById("indent") as HTMLSelectElement;
    var formatButton = document.getElementById("formatButton") as HTMLButtonElement;
    var minifyButton = document.getElementById("minifyButton") as HTMLButtonElement;
    var copyButton = document.getElementById("copyButton") as HTMLButtonElement;
    var clearButton = document.getElementById("clearButton") as HTMLButtonElement;
    var collapseAllButton = document.getElementById("collapseAllButton") as HTMLButtonElement;
    var expandAllButton = document.getElementById("expandAllButton") as HTMLButtonElement;
    var messageElement = document.getElementById("message") as HTMLElement;

    type JsonValue =
        | { kind: "object"; entries: { key: string; value: JsonValue }[] }
        | { kind: "array"; items: JsonValue[] }
        | { kind: "string"; value: string }
        | { kind: "number"; value: string }
        | { kind: "boolean"; value: boolean }
        | { kind: "null" };

    var lastParsed: JsonValue | null = null;

    // ---------- lenient JSON parser ----------
    // Accepts: unquoted keys ({test:1}), single-quoted strings ('test':2),
    // line/block comments, trailing commas. Throws on truly invalid input.
    function parseLenient(input: string): JsonValue {
        var src = input;
        var pos = 0;
        var len = src.length;

        function skipWhitespaceAndComments(): void {
            while (pos < len) {
                var ch = src[pos];
                if (ch === " " || ch === "\t" || ch === "\n" || ch === "\r") {
                    pos++;
                } else if (ch === "/" && src[pos + 1] === "/") {
                    pos += 2;
                    while (pos < len && src[pos] !== "\n") pos++;
                } else if (ch === "/" && src[pos + 1] === "*") {
                    pos += 2;
                    while (pos < len && !(src[pos] === "*" && src[pos + 1] === "/")) pos++;
                    pos += 2;
                } else {
                    break;
                }
            }
        }

        function parseValue(): JsonValue {
            skipWhitespaceAndComments();
            if (pos >= len) throw new Error("Unexpected end of input");
            var ch = src[pos];
            if (ch === "{") return parseObject();
            if (ch === "[") return parseArray();
            if (ch === "\"" || ch === "'") return { kind: "string", value: parseString() };
            if (ch === "-" || (ch >= "0" && ch <= "9") || ch === "." || ch === "+" || ch === "I" || ch === "N") {
                return parseNumberOrLiteral();
            }
            // bare word: true, false, null, or unquoted string token (lenient)
            return parseBareWord();
        }

        function parseObject(): JsonValue {
            pos++; // {
            var entries: { key: string; value: JsonValue }[] = [];
            skipWhitespaceAndComments();
            if (src[pos] === "}") { pos++; return { kind: "object", entries }; }
            while (pos < len) {
                skipWhitespaceAndComments();
                if (pos >= len) throw new Error("Unterminated object");
                // key
                var key: string;
                var k = src[pos];
                if (k === "\"" || k === "'") {
                    key = parseString();
                } else {
                    // unquoted key: read identifier-ish chars
                    var start = pos;
                    while (pos < len && /[\w$\-./]/.test(src[pos])) pos++;
                    key = src.slice(start, pos);
                    if (!key) throw new Error("Expected object key at position " + pos);
                }
                skipWhitespaceAndComments();
                if (src[pos] !== ":") throw new Error("Expected ':' after key '" + key + "'");
                pos++; // :
                var value = parseValue();
                entries.push({ key, value });
                skipWhitespaceAndComments();
                var sep = src[pos];
                if (sep === ",") {
                    pos++;
                    skipWhitespaceAndComments();
                    if (src[pos] === "}") { pos++; break; } // trailing comma
                    continue;
                }
                if (sep === "}") { pos++; break; }
                throw new Error("Expected ',' or '}' at position " + pos);
            }
            return { kind: "object", entries };
        }

        function parseArray(): JsonValue {
            pos++; // [
            var items: JsonValue[] = [];
            skipWhitespaceAndComments();
            if (src[pos] === "]") { pos++; return { kind: "array", items }; }
            while (pos < len) {
                var item = parseValue();
                items.push(item);
                skipWhitespaceAndComments();
                var sep = src[pos];
                if (sep === ",") {
                    pos++;
                    skipWhitespaceAndComments();
                    if (src[pos] === "]") { pos++; break; } // trailing comma
                    continue;
                }
                if (sep === "]") { pos++; break; }
                throw new Error("Expected ',' or ']' at position " + pos);
            }
            return { kind: "array", items };
        }

        function parseString(): string {
            var quote = src[pos];
            pos++; // opening quote
            var result = "";
            while (pos < len) {
                var ch = src[pos];
                if (ch === "\\") {
                    var next = src[pos + 1];
                    if (next === "u") {
                        var hex = src.slice(pos + 2, pos + 6);
                        result += String.fromCharCode(parseInt(hex, 16));
                        pos += 6;
                    } else if (next === "n") { result += "\n"; pos += 2; }
                    else if (next === "t") { result += "\t"; pos += 2; }
                    else if (next === "r") { result += "\r"; pos += 2; }
                    else if (next === "b") { result += "\b"; pos += 2; }
                    else if (next === "f") { result += "\f"; pos += 2; }
                    else { result += next; pos += 2; }
                } else if (ch === quote) {
                    pos++;
                    return result;
                } else {
                    result += ch;
                    pos++;
                }
            }
            throw new Error("Unterminated string");
        }

        function parseNumberOrLiteral(): JsonValue {
            var start = pos;
            // Infinity / NaN
            if (src.slice(pos, pos + 8) === "Infinity") { pos += 8; return { kind: "number", value: "Infinity" }; }
            if (src.slice(pos, pos + 3) === "NaN") { pos += 3; return { kind: "number", value: "NaN" }; }
            while (pos < len && /[-+0-9.eExXa-fA-F]/.test(src[pos])) pos++;
            var raw = src.slice(start, pos);
            return { kind: "number", value: raw };
        }

        function parseBareWord(): JsonValue {
            var start = pos;
            while (pos < len && /[\w$]/.test(src[pos])) pos++;
            var word = src.slice(start, pos);
            if (word === "true") return { kind: "boolean", value: true };
            if (word === "false") return { kind: "boolean", value: false };
            if (word === "null" || word === "undefined") return { kind: "null" };
            // lenient: treat unquoted bare word as a string
            return { kind: "string", value: word };
        }

        var result = parseValue();
        skipWhitespaceAndComments();
        if (pos < len) throw new Error("Unexpected trailing content at position " + pos);
        return result;
    }

    // ---------- serializers (produce canonical JSON for copy/minify) ----------
    function indentUnit(): string {
        var value = indentSelect.value;
        return value === "tab" ? "\t" : new Array(Number(value) + 1).join(" ");
    }

    function escapeString(s: string): string {
        var out = "\"";
        for (var i = 0; i < s.length; i++) {
            var ch = s[i];
            if (ch === "\"") out += "\\\"";
            else if (ch === "\\") out += "\\\\";
            else if (ch === "\n") out += "\\n";
            else if (ch === "\r") out += "\\r";
            else if (ch === "\t") out += "\\t";
            else out += ch;
        }
        return out + "\"";
    }

    function serialize(node: JsonValue, indent: string, depth: number): string {
        var pad = new Array(depth + 1).join(indent);
        var childPad = new Array(depth + 2).join(indent);
        if (node.kind === "object") {
            if (node.entries.length === 0) return "{}";
            var lines = node.entries.map(function (e) {
                return childPad + escapeString(e.key) + ": " + serialize(e.value, indent, depth + 1);
            });
            return "{\n" + lines.join(",\n") + "\n" + pad + "}";
        }
        if (node.kind === "array") {
            if (node.items.length === 0) return "[]";
            var arrLines = node.items.map(function (it) {
                return childPad + serialize(it, indent, depth + 1);
            });
            return "[\n" + arrLines.join(",\n") + "\n" + pad + "]";
        }
        if (node.kind === "string") return escapeString(node.value);
        if (node.kind === "number") return node.value;
        if (node.kind === "boolean") return node.value ? "true" : "false";
        return "null";
    }

    function serializeCompact(node: JsonValue): string {
        if (node.kind === "object") {
            if (node.entries.length === 0) return "{}";
            return "{" + node.entries.map(function (e) {
                return escapeString(e.key) + ":" + serializeCompact(e.value);
            }).join(",") + "}";
        }
        if (node.kind === "array") {
            if (node.items.length === 0) return "[]";
            return "[" + node.items.map(serializeCompact).join(",") + "]";
        }
        if (node.kind === "string") return escapeString(node.value);
        if (node.kind === "number") return node.value;
        if (node.kind === "boolean") return node.value ? "true" : "false";
        return "null";
    }

    // ---------- collapsible, highlighted DOM render ----------
    function countLeaves(node: JsonValue): number {
        if (node.kind === "object") return node.entries.length;
        if (node.kind === "array") return node.items.length;
        return 0;
    }

    function span(tokenClass: string, text: string): HTMLElement {
        var el = document.createElement("span");
        el.className = "token-" + tokenClass;
        el.textContent = text;
        return el;
    }

    function escapeStringPlain(s: string): string {
        return "\"" + s + "\"";
    }

    // Each rendered line is a flex row: [.gutter (toggle slot, indented per depth)]
    // [.content (text)]. Indentation is applied as left margin on the gutter so the
    // toggle stays flush against the content at every depth (no stray spaces).
    function makeRow(toggle: HTMLElement | null, depth: number, indentChars: number): { row: HTMLElement; content: HTMLElement } {
        var row = document.createElement("div");
        row.className = "row";
        var gutter = document.createElement("span");
        gutter.className = "gutter";
        gutter.style.setProperty("--row-indent", String(depth * indentChars));
        if (toggle) gutter.appendChild(toggle);
        row.appendChild(gutter);
        var content = document.createElement("span");
        content.className = "content";
        row.appendChild(content);
        return { row, content };
    }

    function makeToggle(state: "expanded" | "collapsed" | "leaf"): HTMLElement {
        var t = document.createElement("span");
        t.className = "toggle " + state;
        return t;
    }

    // Renders a value into a fresh row appended to `container`.
    // `key` is non-empty for object entries; `isLast` controls trailing comma.
    function renderValue(node: JsonValue, key: string, indent: string, depth: number, container: HTMLElement, isLast: boolean): void {
        var indentChars = indent === "\t" ? 4 : indent.length;

        if (node.kind !== "object" && node.kind !== "array") {
            // primitive: leaf row (empty gutter)
            var primRow = makeRow(null, depth, indentChars);
            if (key) {
                primRow.content.appendChild(span("key", escapeStringPlain(key)));
                primRow.content.appendChild(span("punctuation", ": "));
            }
            appendPrimitive(node, primRow.content);
            if (!isLast) primRow.content.appendChild(span("punctuation", ","));
            container.appendChild(primRow.row);
            return;
        }

        // container
        var open = node.kind === "object" ? "{" : "[";
        var close = node.kind === "object" ? "}" : "]";
        var count = countLeaves(node);
        var empty = count === 0;

        var toggle = makeToggle(empty ? "leaf" : "expanded");
        var openerRow = makeRow(toggle, depth, indentChars);
        if (key) {
            openerRow.content.appendChild(span("key", escapeStringPlain(key)));
            openerRow.content.appendChild(span("punctuation", ": "));
        }
        openerRow.content.appendChild(span("punctuation", open));

        var summary = document.createElement("span");
        summary.className = "summary hidden";
        summary.textContent = count + (count === 1 ? " item" : " items");
        openerRow.content.appendChild(summary);

        var childrenContainer = document.createElement("div");
        childrenContainer.className = "children";
        if (node.kind === "object") {
            for (var i = 0; i < node.entries.length; i++) {
                renderValue(node.entries[i].value, node.entries[i].key, indent, depth + 1, childrenContainer, i === node.entries.length - 1);
            }
        } else {
            for (var j = 0; j < node.items.length; j++) {
                renderValue(node.items[j], "", indent, depth + 1, childrenContainer, j === node.items.length - 1);
            }
        }

        // closer row (indented to match opener's depth)
        var closerRow = makeRow(null, depth, indentChars);
        closerRow.content.appendChild(span("punctuation", close));
        if (!isLast) closerRow.content.appendChild(span("punctuation", ","));

        // assemble: opener row, children block, closer row
        container.appendChild(openerRow.row);
        container.appendChild(childrenContainer);
        container.appendChild(closerRow.row);

        if (!empty) {
            (function (tog, summ, childWrap) {
                tog.addEventListener("click", function () {
                    var collapsed = tog.classList.contains("collapsed");
                    if (collapsed) {
                        tog.classList.remove("collapsed");
                        tog.classList.add("expanded");
                        childWrap.classList.remove("hidden");
                        summ.classList.add("hidden");
                    } else {
                        tog.classList.remove("expanded");
                        tog.classList.add("collapsed");
                        childWrap.classList.add("hidden");
                        summ.classList.remove("hidden");
                    }
                });
            })(toggle, summary, childrenContainer);
        }
    }

    function appendPrimitive(node: JsonValue, content: HTMLElement): void {
        if (node.kind === "string") {
            content.appendChild(span("string", escapeString(node.value)));
        } else if (node.kind === "number") {
            content.appendChild(span("number", node.value));
        } else if (node.kind === "boolean") {
            content.appendChild(span("boolean", node.value ? "true" : "false"));
        } else {
            content.appendChild(span("null", "null"));
        }
    }

    function renderTree(node: JsonValue): void {
        outputElement.replaceChildren();
        renderValue(node, "", indentUnit(), 0, outputElement, true);
    }

    // ---------- actions ----------
    function showEmptyOutput(): void {
        outputElement.replaceChildren();
        var ph = document.createElement("span");
        ph.className = "placeholder";
        ph.textContent = bus.i18n.t("Plugin.JsonFormatter.Detail.EmptyOutput", { defaultValue: "(no output)" });
        outputElement.appendChild(ph);
    }

    function showMessage(kind: "error", text: string): void {
        messageElement.textContent = text;
        messageElement.className = "message " + kind;
    }

    function clearMessage(): void {
        messageElement.textContent = "";
        messageElement.className = "message hidden";
    }

    function format(): void {
        clearMessage();
        var text = inputElement.value.trim();
        if (!text) {
            lastParsed = null;
            showEmptyOutput();
            return;
        }
        try {
            lastParsed = parseLenient(text);
            renderTree(lastParsed);
        } catch (error) {
            lastParsed = null;
            showEmptyOutput();
            showMessage("error", bus.i18n.t("Plugin.JsonFormatter.Error.InvalidJson", {
                defaultValue: "Invalid JSON: {{message}}",
                message: error instanceof Error ? error.message : String(error)
            }));
        }
    }

    function minify(): void {
        clearMessage();
        var text = inputElement.value.trim();
        if (!text) {
            lastParsed = null;
            showEmptyOutput();
            return;
        }
        try {
            lastParsed = parseLenient(text);
            outputElement.replaceChildren();
            var pre = document.createElement("div");
            pre.className = "tree-line";
            pre.textContent = serializeCompact(lastParsed);
            outputElement.appendChild(pre);
        } catch (error) {
            lastParsed = null;
            showEmptyOutput();
            showMessage("error", bus.i18n.t("Plugin.JsonFormatter.Error.InvalidJson", {
                defaultValue: "Invalid JSON: {{message}}",
                message: error instanceof Error ? error.message : String(error)
            }));
        }
    }

    function copyResult(): void {
        if (!lastParsed) return;
        var text = serialize(lastParsed, indentUnit(), 0);
        void navigator.clipboard.writeText(text).then(function () {
            var original = bus.i18n.t("Plugin.JsonFormatter.Detail.Copy", { defaultValue: "Copy" });
            copyButton.textContent = bus.i18n.t("Plugin.JsonFormatter.Detail.Copied", { defaultValue: "Copied" });
            copyButton.classList.add("copied");
            window.setTimeout(function () {
                copyButton.textContent = original;
                copyButton.classList.remove("copied");
            }, 1200);
        });
    }

    function clearAll(): void {
        inputElement.value = "";
        lastParsed = null;
        showEmptyOutput();
        clearMessage();
        inputElement.focus();
    }

    function setAllCollapsed(collapsed: boolean): void {
        var toggles = outputElement.querySelectorAll(".toggle.expanded, .toggle.collapsed");
        toggles.forEach(function (t) {
            var tog = t as HTMLElement;
            var row = tog.closest(".row");
            if (!row) return;
            // summary lives inside this opener row's content
            var summ = row.querySelector(".summary");
            // children block is the .children sibling that follows this opener row
            var childWrap = row.nextElementSibling && row.nextElementSibling.classList.contains("children")
                ? row.nextElementSibling
                : null;
            if (collapsed) {
                tog.classList.remove("expanded");
                tog.classList.add("collapsed");
                if (childWrap) childWrap.classList.add("hidden");
                if (summ) summ.classList.remove("hidden");
            } else {
                tog.classList.remove("collapsed");
                tog.classList.add("expanded");
                if (childWrap) childWrap.classList.remove("hidden");
                if (summ) summ.classList.add("hidden");
            }
        });
    }

    formatButton.addEventListener("click", format);
    minifyButton.addEventListener("click", minify);
    copyButton.addEventListener("click", copyResult);
    clearButton.addEventListener("click", clearAll);
    collapseAllButton.addEventListener("click", function () { setAllCollapsed(true); });
    expandAllButton.addEventListener("click", function () { setAllCollapsed(false); });

    inputElement.addEventListener("keydown", function (event) {
        if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
            event.preventDefault();
            format();
        }
    });

    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        var initialState = payload && payload.initialState as { input?: string } | undefined;
        var initial = initialState && typeof initialState.input === "string" ? initialState.input : "";
        if (initial) {
            inputElement.value = initial;
            format();
        } else {
            showEmptyOutput();
        }
    });

    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        var query = payload && typeof payload.query === "string"
            ? payload.query
            : "";
        if (query) {
            inputElement.value = query;
            format();
        }
    });

    bus.on(HostEvents.LanguageChanged, function () {
        if (!lastParsed) showEmptyOutput();
    });
})();
