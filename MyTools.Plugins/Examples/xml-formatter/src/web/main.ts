import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type {
    MyToolsHostDetailActionPayload,
    MyToolsHostInitializePayload,
    MyToolsHostSearchPayload
} from "@qping/plugin-bus/web";

(function () {
    const bus = createWebBusClient();
    var inputElement = document.getElementById("input") as HTMLTextAreaElement;
    var outputElement = document.getElementById("output") as HTMLElement;
    var indentSelect = document.getElementById("indent") as HTMLSelectElement;
    var formatButton = document.getElementById("formatButton") as HTMLButtonElement;
    var copyButton = document.getElementById("copyButton") as HTMLButtonElement;
    var clearButton = document.getElementById("clearButton") as HTMLButtonElement;
    var collapseAllButton = document.getElementById("collapseAllButton") as HTMLButtonElement;
    var expandAllButton = document.getElementById("expandAllButton") as HTMLButtonElement;
    var messageElement = document.getElementById("message") as HTMLElement;

    var lastSerialized: string | null = null;

    function syncOutput(): void {
        void bus.call("setOutput", { output: lastSerialized || "" });
    }

    function indentUnit(): string {
        //qq
        var value = indentSelect.value;
        return value === "tab" ? "\t" : new Array(Number(value) + 1).join(" ");
    }

    function span(tokenClass: string, text: string): HTMLElement {
        var el = document.createElement("span");
        el.className = "token-" + tokenClass;
        el.textContent = text;
        return el;
    }

    // ---------- XML parse ----------
    function getParserError(doc: Document): string | null {
        var errorNode = doc.getElementsByTagName("parsererror")[0];
        if (!errorNode) return null;
        var raw = (errorNode.textContent || "").trim().split(/\r?\n/)[0] || "Parse error";
        return raw;
    }

    function hasElementChildren(element: Element): boolean {
        for (var i = 0; i < element.childNodes.length; i++) {
            if (element.childNodes[i].nodeType === Node.ELEMENT_NODE) return true;
        }
        return false;
    }

    function elementTextContent(element: Element): string {
        var parts: string[] = [];
        for (var i = 0; i < element.childNodes.length; i++) {
            var child = element.childNodes[i];
            if (child.nodeType === Node.TEXT_NODE || child.nodeType === Node.CDATA_SECTION_NODE) {
                var text = (child.nodeValue || "").trim();
                if (text) parts.push(text);
            }
        }
        return parts.join(" ");
    }

    function childElementCountOf(element: Element): number {
        var count = 0;
        for (var i = 0; i < element.childNodes.length; i++) {
            if (element.childNodes[i].nodeType === Node.ELEMENT_NODE) count++;
        }
        return count;
    }

    // ---------- serializer (standard indented XML, for copy) ----------
    function serializeNode(node: Node, indent: string, depth: number, lines: string[]): void {
        var pad = new Array(depth + 1).join(indent);
        node.childNodes.forEach(function (child) {
            var type = child.nodeType;
            if (type === Node.ELEMENT_NODE) {
                var element = child as Element;
                var tag = element.tagName;
                var opening = "<" + tag;
                for (var i = 0; i < element.attributes.length; i++) {
                    var attr = element.attributes[i];
                    opening += " " + attr.name + "=\"" + attr.value.replace(/"/g, "&quot;") + "\"";
                }
                var childCount = childElementCountOf(element);
                var text = elementTextContent(element);
                if (childCount === 0 && !text) {
                    lines.push(pad + opening + " />");
                } else if (childCount === 0 && text) {
                    lines.push(pad + opening + ">" + text + "</" + tag + ">");
                } else {
                    lines.push(pad + opening + ">");
                    serializeNode(element, indent, depth + 1, lines);
                    lines.push(pad + "</" + tag + ">");
                }
            } else if (type === Node.TEXT_NODE) {
                var value = (child.nodeValue || "").trim();
                if (value) lines.push(pad + value);
            } else if (type === Node.CDATA_SECTION_NODE) {
                lines.push(pad + "<![CDATA[" + (child.nodeValue || "") + "]]>");
            } else if (type === Node.COMMENT_NODE) {
                lines.push(pad + "<!--" + (child.nodeValue || "") + "-->");
            } else if (type === Node.PROCESSING_INSTRUCTION_NODE) {
                var pi = child as ProcessingInstruction;
                lines.push(pad + "<?" + pi.target + " " + (pi.data || "") + "?>");
            }
        });
    }

    function serializeXml(doc: Document, indent: string): string {
        var lines: string[] = [];
        var hasDeclaration = false;
        doc.childNodes.forEach(function (top) {
            if (top.nodeType === Node.PROCESSING_INSTRUCTION_NODE && (top as ProcessingInstruction).target === "xml") {
                lines.push("<?" + (top as ProcessingInstruction).target + " " + (top.nodeValue || "") + "?>");
                hasDeclaration = true;
            }
        });
        if (!hasDeclaration) {
            lines.unshift('<?xml version="1.0" encoding="UTF-8"?>');
        }
        if (doc.documentElement) {
            serializeNode(doc, indent, 0, lines);
        }
        return lines.join("\n");
    }

    // ---------- collapsible highlighted render ----------
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

    function renderElement(element: Element, indent: string, depth: number, container: HTMLElement): void {
        var indentChars = indent === "\t" ? 4 : indent.length;

        var tag = element.tagName;
        var childCount = childElementCountOf(element);
        var text = elementTextContent(element);
        var collapsible = childCount > 0;
        var empty = childCount === 0 && !text;

        var toggle = makeToggle(collapsible ? "expanded" : "leaf");
        var openerRow = makeRow(toggle, depth, indentChars);
        openerRow.content.appendChild(span("punctuation", "<"));
        openerRow.content.appendChild(span("tag", tag));

        for (var i = 0; i < element.attributes.length; i++) {
            var attr = element.attributes[i];
            openerRow.content.appendChild(document.createTextNode(" "));
            openerRow.content.appendChild(span("attr-name", attr.name));
            openerRow.content.appendChild(span("punctuation", "="));
            openerRow.content.appendChild(span("attr-value", "\"" + attr.value + "\""));
        }

        if (empty) {
            openerRow.content.appendChild(span("punctuation", " />"));
            container.appendChild(openerRow.row);
            return;
        }

        openerRow.content.appendChild(span("punctuation", ">"));

        if (!collapsible) {
            // text-only element: inline opener + close on same row
            openerRow.content.appendChild(span("text", text));
            openerRow.content.appendChild(span("punctuation", "</"));
            openerRow.content.appendChild(span("tag", tag));
            openerRow.content.appendChild(span("punctuation", ">"));
            container.appendChild(openerRow.row);
            return;
        }

        // collapsible: summary + children block + closer row
        var summary = document.createElement("span");
        summary.className = "summary hidden";
        summary.textContent = childCount + (childCount === 1 ? " element" : " elements");
        openerRow.content.appendChild(summary);

        var childrenContainer = document.createElement("div");
        childrenContainer.className = "children";
        element.childNodes.forEach(function (child) {
            if (child.nodeType === Node.ELEMENT_NODE) {
                renderElement(child as Element, indent, depth + 1, childrenContainer);
            } else if (child.nodeType === Node.COMMENT_NODE) {
                renderComment(child as Comment, indent, depth + 1, childrenContainer);
            } else if (child.nodeType === Node.PROCESSING_INSTRUCTION_NODE) {
                renderPI(child as ProcessingInstruction, indent, depth + 1, childrenContainer);
            }
        });

        // closer row (indented to match opener's depth)
        var closerRow = makeRow(null, depth, indentChars);
        closerRow.content.appendChild(span("punctuation", "</"));
        closerRow.content.appendChild(span("tag", tag));
        closerRow.content.appendChild(span("punctuation", ">"));

        container.appendChild(openerRow.row);
        container.appendChild(childrenContainer);
        container.appendChild(closerRow.row);

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

    function renderComment(comment: Comment, indent: string, depth: number, container: HTMLElement): void {
        var indentChars = indent === "\t" ? 4 : indent.length;
        var row = makeRow(null, depth, indentChars);
        row.content.appendChild(span("comment", "<!--" + (comment.nodeValue || "") + "-->"));
        container.appendChild(row.row);
    }

    function renderPI(pi: ProcessingInstruction, indent: string, depth: number, container: HTMLElement): void {
        var indentChars = indent === "\t" ? 4 : indent.length;
        var row = makeRow(null, depth, indentChars);
        row.content.appendChild(span("declaration", "<?" + pi.target + " " + (pi.data || "") + "?>"));
        container.appendChild(row.row);
    }

    function renderTree(doc: Document): void {
        outputElement.replaceChildren();
        doc.childNodes.forEach(function (top) {
            if (top.nodeType === Node.PROCESSING_INSTRUCTION_NODE && (top as ProcessingInstruction).target === "xml") {
                var declRow = makeRow(null, 0, 0);
                declRow.content.appendChild(span("declaration", "<?" + (top as ProcessingInstruction).target + " " + (top.nodeValue || "") + "?>"));
                outputElement.appendChild(declRow.row);
            }
        });
        if (doc.documentElement) {
            renderElement(doc.documentElement, indentUnit(), 0, outputElement);
        }
    }

    // ---------- actions ----------
    function showEmptyOutput(): void {
        outputElement.replaceChildren();
        var ph = document.createElement("span");
        ph.className = "placeholder";
        ph.textContent = bus.i18n.t("Plugin.XmlFormatter.Detail.EmptyOutput", { defaultValue: "(no output)" });
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
            lastSerialized = null;
            syncOutput();
            showEmptyOutput();
            return;
        }
        try {
            var parser = new DOMParser();
            var doc = parser.parseFromString(text, "application/xml");
            var error = getParserError(doc);
            if (error) throw new Error(error);
            lastSerialized = serializeXml(doc, indentUnit());
            syncOutput();
            renderTree(doc);
        } catch (error) {
            lastSerialized = null;
            syncOutput();
            showEmptyOutput();
            showMessage("error", bus.i18n.t("Plugin.XmlFormatter.Error.InvalidXml", {
                defaultValue: "Invalid XML: {{message}}",
                message: error instanceof Error ? error.message : String(error)
            }));
        }
    }

    function copyResult(): void {
        // Enter can arrive before the input has ever been formatted, so produce the output first.
        if (!lastSerialized && inputElement.value.trim()) {
            format();
        }
        if (!lastSerialized) return;
        void navigator.clipboard.writeText(lastSerialized).then(function () {
            var original = bus.i18n.t("Plugin.XmlFormatter.Detail.Copy", { defaultValue: "Copy" });
            copyButton.textContent = bus.i18n.t("Plugin.XmlFormatter.Detail.Copied", { defaultValue: "Copied" });
            copyButton.classList.add("copied");
            window.setTimeout(function () {
                copyButton.textContent = original;
                copyButton.classList.remove("copied");
            }, 1200);
        });
    }

    function clearAll(): void {
        inputElement.value = "";
        lastSerialized = null;
        syncOutput();
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
            var summ = row.querySelector(".summary");
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
    copyButton.addEventListener("click", copyResult);
    clearButton.addEventListener("click", clearAll);
    collapseAllButton.addEventListener("click", function () { setAllCollapsed(true); });
    expandAllButton.addEventListener("click", function () { setAllCollapsed(false); });
    inputElement.addEventListener("input", format);
    indentSelect.addEventListener("change", format);

    // Ctrl+Enter is an explicit page-local format shortcut; plain Enter remains available for editing.
    document.addEventListener("keydown", function (event) {
        if (event.key !== "Enter" || event.altKey) return;
        if (event.ctrlKey || event.metaKey) {
            event.preventDefault();
            format();
            return;
        }
        return;
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
        if (!lastSerialized) showEmptyOutput();
    });
})();
