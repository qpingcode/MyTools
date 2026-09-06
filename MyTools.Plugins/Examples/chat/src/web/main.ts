import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";
import DOMPurify from "dompurify";
import hljs from "highlight.js/lib/common";
import { marked } from "marked";
import mermaid from "mermaid";

(function () {
    type ChatTokenUsage = { inputTokens: number; outputTokens: number; totalTokens: number };
    type ChatMessage = {
        role: string;
        content: string;
        usage?: ChatTokenUsage | null;
        durationMilliseconds?: number | null;
        interactionResponse?: InteractionResponse | null;
    };
    type ChatState = {
        sessionId: string;
        messages: ChatMessage[];
        selectedModel: string;
        streaming: boolean;
        cancelled: boolean;
        error: string;
    };
    type ChatStatus = {
        available: boolean;
        provider: string;
        selectedModel: string;
        models: string[];
        unavailableReason?: string;
    };
    type ChatConversationSummary = { sessionId: string; title: string; updatedAt: string };
    type ChatConversationList = { conversations: ChatConversationSummary[] };

    const bus = createWebBusClient();
    const POLL_INTERVAL_MS = 120;
    const STICK_TO_BOTTOM_THRESHOLD = 24;
    const messagesElement = document.getElementById("messages") as HTMLElement;
    const promptInput = document.getElementById("promptInput") as HTMLTextAreaElement;
    const sendButton = document.getElementById("sendButton") as HTMLButtonElement;
    const newChatButton = document.getElementById("newChatButton") as HTMLButtonElement;
    const conversationList = document.getElementById("conversationList") as HTMLElement;
    const conversationTitle = document.getElementById("conversationTitle") as HTMLElement;
    const modelSelect = document.getElementById("modelSelect") as HTMLSelectElement;
    let sessionId = "";
    let pollTimer: number | null = null;
    let pollInFlight = false;
    let pollAfterCurrent = false;
    let currentState: ChatState | null = null;
    let cancelRequested = false;
    let userScrolledUp = false;
    let pendingStreamingFrame: number | null = null;
    let pendingStreamingRender: { entry: RenderedMessage; content: string } | null = null;
    let recentConversations: ChatConversationSummary[] = [];

    type MarkdownToken = ReturnType<typeof marked.lexer>[number];
    type RenderedMarkdownBlock = {
        element: HTMLDivElement;
        type: string;
        raw: string;
        language: string;
        signature: string;
        highlighted: boolean;
        mermaidSourceVisible: boolean;
    };
    type RenderedMessage = {
        role: string;
        bubble: HTMLDivElement;
        usage: HTMLDivElement | null;
        content: string;
        markdown: MarkdownRenderer | null;
        finalized: boolean;
    };
    type InteractionQuestion = {
        id: string;
        prompt: string;
        options: string[];
        multiple: boolean;
        allowText: boolean;
        textPlaceholder: string;
    };
    type InteractionSpec = { id: string; title: string; questions: InteractionQuestion[] };
    type InteractionAnswer = { questionId: string; prompt: string; values: string[]; text: string };
    type InteractionResponse = { interactionId: string; answers: InteractionAnswer[] };

    const renderedMessages: RenderedMessage[] = [];
    let mermaidRenderQueue: Promise<void> = Promise.resolve();
    let noticeElement: HTMLDivElement | null = null;
    let emptyElement: HTMLDivElement | null = null;
    const INTERACTION_PATTERN = /```mytools-interaction\s*\r?\n([\s\S]*?)\r?\n```/i;

    function normalize(value: unknown): string {
        return typeof value === "string" ? value.trim() : "";
    }

    function newSessionId(): string {
        return crypto.randomUUID().replaceAll("-", "");
    }

    const markdownOptions = { async: false as const, breaks: true, gfm: true };

    function sanitizeMarkdown(html: string): string {
        return DOMPurify.sanitize(html, {
            USE_PROFILES: { html: true },
            FORBID_TAGS: ["style", "iframe", "object", "embed"]
        });
    }

    function sanitizeDiagram(svg: string): string {
        return DOMPurify.sanitize(svg, {
            USE_PROFILES: { svg: true, svgFilters: true },
            FORBID_TAGS: ["foreignObject", "script"]
        });
    }

    function themeColor(name: string, fallback: string): string {
        return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
    }

    function configureMermaid(): void {
        const background = themeColor("--mt-surface-alt", "#202020");
        const surface = themeColor("--mt-surface", "#242424");
        const text = themeColor("--mt-text", "#eeeeee");
        const mutedText = themeColor("--mt-text-muted", "#b8b8b8");
        const border = themeColor("--mt-border", "#393939");
        mermaid.initialize({
            startOnLoad: false,
            securityLevel: "strict",
            suppressErrorRendering: true,
            theme: "base",
            flowchart: { htmlLabels: false },
            themeVariables: {
                background,
                primaryColor: background,
                primaryTextColor: text,
                primaryBorderColor: border,
                secondaryColor: surface,
                secondaryTextColor: text,
                secondaryBorderColor: border,
                tertiaryColor: themeColor("--mt-surface-hover", "#303030"),
                tertiaryTextColor: text,
                tertiaryBorderColor: border,
                lineColor: mutedText,
                textColor: text,
                mainBkg: background,
                nodeBorder: border,
                clusterBkg: surface,
                clusterBorder: border,
                titleColor: text,
                edgeLabelBackground: surface
            }
        });
    }

    function enqueueMermaidRender<T>(operation: () => Promise<T>): Promise<T> {
        const queued = mermaidRenderQueue.then(operation, operation);
        mermaidRenderQueue = queued.then(
            (): void => undefined,
            (): void => undefined
        );
        return queued;
    }

    function limitedText(value: unknown, maxLength: number): string {
        return typeof value === "string" ? value.trim().slice(0, maxLength) : "";
    }

    function stableInteractionId(value: string): string {
        let hash = 2166136261;
        for (let index = 0; index < value.length; index++) {
            hash ^= value.charCodeAt(index);
            hash = Math.imul(hash, 16777619);
        }
        return `interaction_${(hash >>> 0).toString(16)}`;
    }

    function structuredId(value: unknown, fallback: string): string {
        const candidate = limitedText(value, 64);
        return /^[a-zA-Z0-9_.-]{1,64}$/.test(candidate) ? candidate : fallback;
    }

    function parseInteraction(markdown: string): { markdown: string; interaction: InteractionSpec | null } {
        const match = INTERACTION_PATTERN.exec(markdown);
        if (!match) return { markdown, interaction: null };
        try {
            const raw = JSON.parse(match[1]) as Record<string, unknown>;
            if (raw.version !== undefined && raw.version !== 1) return { markdown, interaction: null };
            if (!Array.isArray(raw.questions) || raw.questions.length === 0 || raw.questions.length > 12) {
                return { markdown, interaction: null };
            }
            const questions = raw.questions.map(function (value, index): InteractionQuestion | null {
                if (!value || typeof value !== "object") return null;
                const question = value as Record<string, unknown>;
                const prompt = limitedText(question.prompt, 500);
                const options = Array.isArray(question.options)
                    ? question.options.map((option) => limitedText(option, 120)).filter(Boolean).slice(0, 12)
                    : [];
                const allowText = question.allowText === true;
                if (!prompt || (options.length === 0 && !allowText)) return null;
                return {
                    id: structuredId(question.id, `question_${index + 1}`),
                    prompt,
                    options,
                    multiple: question.multiple === true,
                    allowText,
                    textPlaceholder: limitedText(question.textPlaceholder, 120)
                };
            });
            if (questions.some((question) => question === null)) return { markdown, interaction: null };
            return {
                markdown: markdown.replace(match[0], "").trimEnd(),
                interaction: {
                    id: structuredId(raw.id, stableInteractionId(match[1])),
                    title: limitedText(raw.title, 160),
                    questions: questions as InteractionQuestion[]
                }
            };
        } catch {
            return { markdown, interaction: null };
        }
    }

    function untitledConversation(): string {
        return bus.i18n.t("Plugin.Chat.Detail.Untitled", { defaultValue: "New conversation" });
    }

    function titleFromState(): string {
        const firstUserMessage = currentState?.messages.find((message) => message.role === "user")?.content;
        if (!firstUserMessage) return untitledConversation();
        const title = firstUserMessage.trim().replace(/\s+/g, " ");
        return title.length > 42 ? `${title.slice(0, 42).trimEnd()}…` : title;
    }

    function renderConversationList(): void {
        const activeTitle = titleFromState();
        conversationTitle.textContent = activeTitle;
        const items = [...recentConversations];
        if (sessionId && !items.some((item) => item.sessionId === sessionId)) {
            items.unshift({ sessionId, title: activeTitle, updatedAt: new Date().toISOString() });
        }
        conversationList.replaceChildren();
        if (items.length === 0) {
            const empty = document.createElement("div");
            empty.className = "conversation-list-empty";
            empty.textContent = bus.i18n.t("Plugin.Chat.Detail.NoRecent", { defaultValue: "No recent conversations" });
            conversationList.appendChild(empty);
            return;
        }
        items.forEach(function (conversation) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = `conversation-item${conversation.sessionId === sessionId ? " active" : ""}`;
            button.textContent = conversation.sessionId === sessionId ? activeTitle : conversation.title;
            button.title = button.textContent;
            button.disabled = currentState?.streaming === true;
            button.addEventListener("click", function () { void switchConversation(conversation.sessionId); });
            conversationList.appendChild(button);
        });
    }

    async function loadConversations(): Promise<void> {
        try {
            const response = await bus.call<ChatConversationList>("list");
            recentConversations = Array.isArray(response.conversations) ? response.conversations : [];
            renderConversationList();
        } catch {
            renderConversationList();
        }
    }

    function secureLinks(element: ParentNode): void {
        element.querySelectorAll<HTMLAnchorElement>("a").forEach(function (link) {
            link.target = "_blank";
            link.rel = "noopener noreferrer";
        });
    }

    class MarkdownRenderer {
        private readonly blocks: RenderedMarkdownBlock[] = [];
        private diagramRevision = 0;
        private interactionElement: HTMLDivElement | null = null;
        private interactionSpec: InteractionSpec | null = null;
        private interactionResponse: InteractionResponse | null = null;
        private interactionExpanded = true;
        private interactionStateSignature = "";

        public constructor(private readonly element: HTMLElement) {}

        public render(markdown: string, final: boolean): void {
            const completedInteraction = parseInteraction(markdown);
            const interactionStart = markdown.toLowerCase().indexOf("```mytools-interaction");
            const parsed = final
                ? completedInteraction
                : {
                    markdown: completedInteraction.interaction
                        ? completedInteraction.markdown
                        : interactionStart >= 0 ? markdown.slice(0, interactionStart).trimEnd() : markdown,
                    interaction: null
                };
            const tokens = marked.lexer(parsed.markdown, markdownOptions)
                .filter((token) => token.type !== "space");
            let stableCount = 0;
            while (stableCount < tokens.length && stableCount < this.blocks.length) {
                const token = tokens[stableCount];
                const block = this.blocks[stableCount];
                if (block.type === token.type && block.signature === this.tokenSignature(token)) {
                    stableCount++;
                    continue;
                }
                if (this.updateStreamingCodeBlock(block, token)) {
                    stableCount++;
                }
                break;
            }

            this.removeBlocksFrom(stableCount);
            for (let index = stableCount; index < tokens.length; index++) {
                this.appendBlock(tokens[index]);
            }
            if (final) {
                this.finalizeCodeBlocks();
                void this.renderMermaidBlocks();
                this.renderInteraction(parsed.interaction);
            }
        }

        public refreshCodeTools(): void {
            this.blocks.forEach((block) => {
                this.updateCopyButtonLabel(block);
                this.updateMermaidToggleLabel(block);
            });
        }

        public refreshMermaidDiagrams(): void {
            this.blocks.forEach((block) => {
                block.element.querySelector(".mermaid-diagram")?.remove();
                block.element.querySelector(".mermaid-error")?.remove();
                block.element.querySelector(".mermaid-toggle-button")?.remove();
                const shell = block.element.querySelector<HTMLElement>(".code-block-shell");
                const pre = shell?.querySelector<HTMLElement>("pre");
                if (pre) pre.hidden = false;
            });
            void this.renderMermaidBlocks();
        }

        public setInteractionState(response: InteractionResponse | null, expanded: boolean): void {
            const signature = JSON.stringify({ response, expanded });
            if (this.interactionStateSignature === signature) return;
            this.interactionStateSignature = signature;
            this.interactionResponse = response;
            this.interactionExpanded = expanded;
            if (this.interactionSpec) this.renderInteraction(this.interactionSpec);
        }

        public clear(): void {
            this.diagramRevision++;
            this.blocks.length = 0;
            this.interactionElement = null;
            this.interactionSpec = null;
            this.interactionResponse = null;
            this.interactionStateSignature = "";
            this.element.replaceChildren();
        }

        private renderInteraction(interaction: InteractionSpec | null): void {
            this.interactionElement?.remove();
            this.interactionSpec = interaction;
            this.interactionElement = interaction
                ? this.interactionResponse
                    ? createInteractionSummary(interaction, this.interactionResponse)
                    : this.interactionExpanded ? createInteraction(interaction) : null
                : null;
            if (this.interactionElement) this.element.appendChild(this.interactionElement);
        }

        private updateStreamingCodeBlock(block: RenderedMarkdownBlock, token: MarkdownToken): boolean {
            if (block.type !== "code" || token.type !== "code" || !token.raw.startsWith(block.raw)) {
                return false;
            }
            const language = "lang" in token ? token.lang ?? "" : "";
            if (language !== block.language) {
                return false;
            }
            const code = block.element.querySelector("code");
            if (!code || !("text" in token)) {
                return false;
            }
            code.textContent = token.text;
            block.raw = token.raw;
            block.signature = this.tokenSignature(token);
            block.highlighted = false;
            return true;
        }

        private appendBlock(token: MarkdownToken): void {
            const block = document.createElement("div");
            block.className = "markdown-block";
            block.innerHTML = sanitizeMarkdown(marked.parser([token], markdownOptions));
            secureLinks(block);
            this.element.appendChild(block);
            this.blocks.push({
                element: block,
                type: token.type,
                raw: token.raw,
                language: token.type === "code" && "lang" in token ? token.lang ?? "" : "",
                signature: this.tokenSignature(token),
                highlighted: false,
                mermaidSourceVisible: false
            });
        }

        private finalizeCodeBlocks(): void {
            this.blocks.filter((block) => block.type === "code").forEach((block) => {
                const code = block.element.querySelector<HTMLElement>("code");
                if (!code) return;
                if (block.language.trim().toLowerCase() === "mermaid") {
                    this.ensureCodeTools(block, code);
                    return;
                }
                if (!block.highlighted) {
                    const source = code.textContent ?? "";
                    const requestedLanguage = block.language.trim().toLowerCase();
                    const result = requestedLanguage && hljs.getLanguage(requestedLanguage)
                        ? hljs.highlight(source, { language: requestedLanguage, ignoreIllegals: true })
                        : hljs.highlightAuto(source);
                    code.innerHTML = result.value;
                    code.classList.add("hljs");
                    if (result.language) code.classList.add(`language-${result.language}`);
                    block.highlighted = true;
                }
                this.ensureCodeTools(block, code);
            });
        }

        private async renderMermaidBlocks(): Promise<void> {
            const revision = ++this.diagramRevision;
            const blocks = this.blocks.filter((block) =>
                block.type === "code" && block.language.trim().toLowerCase() === "mermaid");
            for (const block of blocks) {
                if (revision !== this.diagramRevision) return;
                await this.renderMermaidBlock(block, revision);
            }
        }

        private async renderMermaidBlock(block: RenderedMarkdownBlock, revision: number): Promise<void> {
            const code = block.element.querySelector<HTMLElement>("code");
            const shell = code?.closest<HTMLElement>(".code-block-shell");
            if (!code || !shell || block.element.querySelector(".mermaid-diagram")) return;
            const source = code.textContent ?? "";
            try {
                const result = await enqueueMermaidRender(async () => {
                    if (revision !== this.diagramRevision || !block.element.isConnected) return null;
                    configureMermaid();
                    return mermaid.render(`mermaid-${crypto.randomUUID()}`, source);
                });
                if (!result) return;
                if (revision !== this.diagramRevision || !block.element.isConnected) return;
                const svg = sanitizeDiagram(result.svg);
                if (!svg.includes("<svg")) throw new Error("Mermaid returned an invalid SVG.");
                const diagram = document.createElement("div");
                diagram.className = "mermaid-diagram";
                diagram.innerHTML = svg;
                shell.appendChild(diagram);
                this.ensureMermaidToggle(block, shell, diagram);
            } catch (error) {
                if (revision !== this.diagramRevision || !block.element.isConnected) return;
                const message = document.createElement("div");
                message.className = "mermaid-error";
                message.textContent = bus.i18n.t("Plugin.Chat.Detail.MermaidError", {
                    defaultValue: "Unable to render Mermaid diagram"
                });
                message.title = error instanceof Error ? error.message : String(error);
                block.element.appendChild(message);
                console.warn("Unable to render Mermaid diagram.", error);
            }
        }

        private ensureCodeTools(block: RenderedMarkdownBlock, code: HTMLElement): void {
            const pre = block.element.querySelector("pre");
            if (!pre) return;
            let shell = pre.parentElement?.classList.contains("code-block-shell")
                ? pre.parentElement
                : null;
            if (!shell) {
                shell = document.createElement("div");
                shell.className = "code-block-shell";
                pre.replaceWith(shell);
                shell.appendChild(pre);
            }
            let toolbar = shell.querySelector<HTMLDivElement>(":scope > .code-toolbar");
            if (!toolbar) {
                toolbar = document.createElement("div");
                toolbar.className = "code-toolbar";
                const language = document.createElement("span");
                language.className = "code-language";
                language.textContent = block.language || "text";
                const actions = document.createElement("div");
                actions.className = "code-actions";
                const copy = document.createElement("button");
                copy.type = "button";
                copy.className = "code-copy-button";
                copy.addEventListener("click", () => { void this.copyCode(copy, code); });
                actions.appendChild(copy);
                toolbar.append(language, actions);
                shell.prepend(toolbar);
            }
            this.updateCopyButtonLabel(block);
        }

        private ensureMermaidToggle(
            block: RenderedMarkdownBlock,
            shell: HTMLElement,
            diagram: HTMLElement
        ): void {
            const pre = shell.querySelector<HTMLElement>("pre");
            const actions = shell.querySelector<HTMLElement>(".code-actions");
            if (!pre || !actions) return;
            const toggle = document.createElement("button");
            toggle.type = "button";
            toggle.className = "mermaid-toggle-button";
            toggle.addEventListener("click", () => {
                block.mermaidSourceVisible = !block.mermaidSourceVisible;
                pre.hidden = !block.mermaidSourceVisible;
                diagram.hidden = block.mermaidSourceVisible;
                this.updateMermaidToggleLabel(block);
            });
            actions.prepend(toggle);
            pre.hidden = !block.mermaidSourceVisible;
            diagram.hidden = block.mermaidSourceVisible;
            this.updateMermaidToggleLabel(block);
        }

        private updateMermaidToggleLabel(block: RenderedMarkdownBlock): void {
            const toggle = block.element.querySelector<HTMLButtonElement>(".mermaid-toggle-button");
            if (!toggle) return;
            toggle.textContent = block.mermaidSourceVisible
                ? bus.i18n.t("Plugin.Chat.Detail.MermaidDiagram", { defaultValue: "Diagram" })
                : bus.i18n.t("Plugin.Chat.Detail.MermaidSource", { defaultValue: "Source" });
            toggle.title = toggle.textContent;
            toggle.setAttribute("aria-pressed", String(block.mermaidSourceVisible));
        }

        private updateCopyButtonLabel(block: RenderedMarkdownBlock): void {
            const copy = block.element.querySelector<HTMLButtonElement>(".code-copy-button");
            if (!copy || copy.dataset.copied === "true") return;
            copy.textContent = bus.i18n.t("Plugin.Chat.Detail.CopyCode", { defaultValue: "Copy" });
            copy.title = copy.textContent;
        }

        private async copyCode(button: HTMLButtonElement, code: HTMLElement): Promise<void> {
            const text = code.textContent ?? "";
            try {
                await navigator.clipboard.writeText(text);
            } catch {
                const textarea = document.createElement("textarea");
                textarea.value = text;
                textarea.style.position = "fixed";
                textarea.style.opacity = "0";
                document.body.appendChild(textarea);
                textarea.select();
                document.execCommand("copy");
                textarea.remove();
            }
            button.dataset.copied = "true";
            button.textContent = bus.i18n.t("Plugin.Chat.Detail.CodeCopied", { defaultValue: "Copied" });
            button.title = button.textContent;
            window.setTimeout(() => {
                delete button.dataset.copied;
                const block = this.blocks.find((candidate) => candidate.element.contains(button));
                if (block) this.updateCopyButtonLabel(block);
            }, 1400);
        }

        private tokenSignature(token: MarkdownToken): string {
            return JSON.stringify(token);
        }

        private removeBlocksFrom(index: number): void {
            for (let current = this.blocks.length - 1; current >= index; current--) {
                this.blocks[current].element.remove();
                this.blocks.pop();
            }
        }
    }

    function createInteraction(spec: InteractionSpec): HTMLDivElement {
        const card = document.createElement("div");
        card.className = "interaction-card";
        const heading = document.createElement("div");
        heading.className = "interaction-heading";
        heading.textContent = spec.title || bus.i18n.t("Plugin.Chat.Detail.Questions", { defaultValue: "A few questions" });
        const progress = document.createElement("div");
        progress.className = "interaction-progress";
        const body = document.createElement("div");
        body.className = "interaction-body";
        const footer = document.createElement("div");
        footer.className = "interaction-footer";
        card.append(heading, progress, body, footer);

        const answers = spec.questions.map(() => ({ values: new Set<string>(), text: "" }));
        const inputGroup = `interaction-${crypto.randomUUID()}`;
        let page = 0;
        let submitting = false;

        function hasAnswer(index: number): boolean {
            return answers[index].values.size > 0 || answers[index].text.trim().length > 0;
        }

        function renderPage(): void {
            const question = spec.questions[page];
            const answer = answers[page];
            progress.textContent = bus.i18n.t("Plugin.Chat.Detail.QuestionProgress", {
                defaultValue: "{{current}} of {{total}}",
                current: page + 1,
                total: spec.questions.length
            });
            body.replaceChildren();
            const prompt = document.createElement("div");
            prompt.className = "interaction-prompt";
            prompt.textContent = question.prompt;
            const choices = document.createElement("div");
            choices.className = "interaction-choices";
            question.options.forEach(function (option, optionIndex) {
                const label = document.createElement("label");
                label.className = "interaction-choice";
                const input = document.createElement("input");
                input.type = question.multiple ? "checkbox" : "radio";
                input.name = `${inputGroup}-${page}`;
                input.value = option;
                input.checked = answer.values.has(option);
                input.disabled = submitting;
                label.classList.toggle("selected", input.checked);
                label.classList.toggle("disabled", input.disabled);
                input.addEventListener("change", function () {
                    if (question.multiple) {
                        if (input.checked) answer.values.add(option); else answer.values.delete(option);
                    } else {
                        answer.values.clear();
                        if (input.checked) answer.values.add(option);
                        answer.text = "";
                        const textInput = body.querySelector<HTMLTextAreaElement>(".interaction-text");
                        if (textInput) textInput.value = "";
                    }
                    choices.querySelectorAll<HTMLLabelElement>(".interaction-choice").forEach(function (choice) {
                        const choiceInput = choice.querySelector<HTMLInputElement>("input");
                        choice.classList.toggle("selected", choiceInput?.checked === true);
                    });
                    updateActions();
                });
                const marker = document.createElement("span");
                marker.className = "interaction-choice-marker";
                const text = document.createElement("span");
                text.textContent = option;
                label.append(input, marker, text);
                choices.appendChild(label);
            });
            body.append(prompt, choices);

            if (question.allowText) {
                const textInput = document.createElement("textarea");
                textInput.className = "interaction-text";
                textInput.rows = 2;
                textInput.maxLength = 1000;
                textInput.value = answer.text;
                textInput.disabled = submitting;
                textInput.placeholder = question.textPlaceholder || bus.i18n.t("Plugin.Chat.Detail.OtherAnswer", {
                    defaultValue: "Enter another answer"
                });
                textInput.addEventListener("input", function () {
                    answer.text = textInput.value;
                    if (!question.multiple && answer.text.trim()) {
                        answer.values.clear();
                        choices.querySelectorAll<HTMLInputElement>("input").forEach(function (input) {
                            input.checked = false;
                            input.closest(".interaction-choice")?.classList.remove("selected");
                        });
                    }
                    updateActions();
                });
                body.appendChild(textInput);
            }
            renderActions();
        }

        function actionButton(className: string, text: string, action: () => void): HTMLButtonElement {
            const button = document.createElement("button");
            button.type = "button";
            button.className = className;
            button.textContent = text;
            button.addEventListener("click", action);
            return button;
        }

        function updateActions(): void {
            const primary = footer.querySelector<HTMLButtonElement>(".interaction-primary");
            if (primary) primary.disabled = submitting || !hasAnswer(page);
        }

        function renderActions(): void {
            footer.replaceChildren();
            const previous = actionButton("interaction-button", bus.i18n.t("Plugin.Chat.Detail.Previous", {
                defaultValue: "Previous"
            }), function () { page--; renderPage(); });
            previous.disabled = page === 0 || submitting;
            previous.hidden = spec.questions.length === 1;
            footer.appendChild(previous);

            if (page < spec.questions.length - 1) {
                const next = actionButton("interaction-button interaction-primary", bus.i18n.t("Plugin.Chat.Detail.Next", {
                    defaultValue: "Next"
                }), function () { page++; renderPage(); });
                next.disabled = submitting || !hasAnswer(page);
                footer.appendChild(next);
                return;
            }
            const submit = actionButton("interaction-button interaction-primary", bus.i18n.t("Plugin.Chat.Detail.SubmitAnswers", {
                defaultValue: "Submit"
            }), function () {
                if (submitting || answers.some((_, index) => !hasAnswer(index))) return;
                submitting = true;
                card.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLButtonElement>(
                    "input, textarea, button"
                ).forEach((control) => { control.disabled = true; });
                const result = spec.questions.map((question, index): InteractionAnswer => ({
                    questionId: question.id,
                    prompt: question.prompt,
                    values: [...answers[index].values],
                    text: answers[index].text.trim()
                }));
                void sendInteractionAnswers(spec.id, result, card).then(function (sent) {
                    if (sent) card.classList.add("submitted");
                    else {
                        submitting = false;
                        renderPage();
                    }
                });
            });
            submit.disabled = submitting || answers.some((_, index) => !hasAnswer(index));
            footer.appendChild(submit);
        }

        renderPage();
        return card;
    }

    function createInteractionSummary(
        spec: InteractionSpec,
        response: InteractionResponse
    ): HTMLDivElement {
        const summary = document.createElement("div");
        summary.className = "interaction-summary";
        const row = document.createElement("div");
        row.className = "interaction-summary-row";
        const status = document.createElement("span");
        status.className = "interaction-summary-status";
        status.textContent = "✓";
        const text = document.createElement("span");
        text.className = "interaction-summary-text";
        text.textContent = bus.i18n.t("Plugin.Chat.Detail.AnswersSubmitted", {
            defaultValue: "Submitted {{count}} answers",
            count: response.answers.length
        });
        const toggle = document.createElement("button");
        toggle.type = "button";
        toggle.className = "interaction-summary-toggle";
        toggle.textContent = bus.i18n.t("Plugin.Chat.Detail.ViewAnswers", { defaultValue: "View" });
        row.append(status, text, toggle);

        const details = document.createElement("div");
        details.className = "interaction-summary-details";
        details.hidden = true;
        if (spec.title) {
            const title = document.createElement("div");
            title.className = "interaction-summary-title";
            title.textContent = spec.title;
            details.appendChild(title);
        }
        response.answers.forEach(function (answer, index) {
            const item = document.createElement("div");
            item.className = "interaction-summary-answer";
            const prompt = document.createElement("div");
            prompt.className = "interaction-summary-prompt";
            prompt.textContent = `${index + 1}. ${answer.prompt}`;
            const value = document.createElement("div");
            value.className = "interaction-summary-value";
            value.textContent = [...answer.values, answer.text].filter(Boolean).join(" · ");
            item.append(prompt, value);
            details.appendChild(item);
        });
        toggle.addEventListener("click", function () {
            details.hidden = !details.hidden;
            toggle.textContent = details.hidden
                ? bus.i18n.t("Plugin.Chat.Detail.ViewAnswers", { defaultValue: "View" })
                : bus.i18n.t("Plugin.Chat.Detail.HideAnswers", { defaultValue: "Hide" });
        });
        summary.append(row, details);
        return summary;
    }

    function clearRenderedMessages(): void {
        if (pendingStreamingFrame !== null) cancelAnimationFrame(pendingStreamingFrame);
        pendingStreamingFrame = null;
        pendingStreamingRender = null;
        renderedMessages.forEach((entry) => entry.markdown?.clear());
        renderedMessages.length = 0;
        noticeElement = null;
        emptyElement = null;
        messagesElement.replaceChildren();
    }

    function truncateRenderedMessages(length: number): void {
        while (renderedMessages.length > length) {
            const entry = renderedMessages.pop()!;
            entry.bubble.remove();
            entry.usage?.remove();
        }
    }

    function createRenderedMessage(role: string): RenderedMessage {
        const bubble = document.createElement("div");
        bubble.className = role === "user" ? "message user" : "message assistant markdown markdown-body";
        const entry: RenderedMessage = {
            role,
            bubble,
            usage: null,
            content: "",
            markdown: role === "assistant" ? new MarkdownRenderer(bubble) : null,
            finalized: false
        };
        messagesElement.insertBefore(bubble, noticeElement);
        renderedMessages.push(entry);
        return entry;
    }

    function renderMessageContent(entry: RenderedMessage, content: string, final: boolean): void {
        if (entry.content === content && entry.finalized === final) {
            if (final) entry.markdown?.refreshCodeTools();
            return;
        }
        entry.content = content;
        entry.finalized = final;
        if (entry.markdown) entry.markdown.render(content, final);
        else entry.bubble.textContent = content;
    }

    function scheduleStreamingMarkdown(entry: RenderedMessage, content: string): void {
        pendingStreamingRender = { entry, content };
        if (pendingStreamingFrame !== null) return;
        pendingStreamingFrame = requestAnimationFrame(function () {
            pendingStreamingFrame = null;
            const pending = pendingStreamingRender;
            pendingStreamingRender = null;
            if (!pending || !renderedMessages.includes(pending.entry)) return;
            renderMessageContent(pending.entry, pending.content, false);
            scrollToBottom();
        });
    }

    function renderUsage(entry: RenderedMessage, message: ChatMessage): void {
        if (!message.usage && !message.durationMilliseconds) {
            entry.usage?.remove();
            entry.usage = null;
            return;
        }
        if (!entry.usage) {
            entry.usage = document.createElement("div");
            entry.usage.className = "token-usage";
            entry.bubble.after(entry.usage);
        }
        const details: string[] = [];
        if (message.usage) {
            details.push(bus.i18n.t("Plugin.Chat.Detail.TokenUsage", {
                defaultValue: "{{total}} tokens · input {{input}} · output {{output}}",
                total: message.usage.totalTokens,
                input: message.usage.inputTokens,
                output: message.usage.outputTokens
            }));
        }
        if (message.durationMilliseconds) {
            details.push(formatDuration(message.durationMilliseconds));
        }
        entry.usage.textContent = details.join(" · ");
    }

    function formatDuration(durationMilliseconds: number): string {
        if (durationMilliseconds < 1000) {
            return bus.i18n.t("Plugin.Chat.Detail.DurationMilliseconds", {
                defaultValue: "{{duration}} ms",
                duration: Math.max(1, Math.round(durationMilliseconds))
            });
        }
        const seconds = durationMilliseconds / 1000;
        return bus.i18n.t("Plugin.Chat.Detail.DurationSeconds", {
            defaultValue: "{{duration}} s",
            duration: seconds < 10 ? seconds.toFixed(1) : Math.round(seconds)
        });
    }

    function renderNotice(state: ChatState, hasMessages: boolean): void {
        const message = state.error && hasMessages
            ? { className: "message error", text: state.error }
            : state.cancelled
                ? { className: "status-message", text: bus.i18n.t("Plugin.Chat.Detail.Cancelled", { defaultValue: "Response stopped" }) }
                : null;
        if (!message) {
            noticeElement?.remove();
            noticeElement = null;
            return;
        }
        if (!noticeElement) {
            noticeElement = document.createElement("div");
            messagesElement.appendChild(noticeElement);
        }
        noticeElement.className = message.className;
        noticeElement.textContent = message.text;
    }

    function renderState(state: ChatState): void {
        const wasStreaming = currentState?.streaming === true;
        if (currentState && currentState.sessionId !== state.sessionId) clearRenderedMessages();
        currentState = state;
        sessionId = state.sessionId || sessionId;
        if (state.selectedModel) modelSelect.value = state.selectedModel;
        const messages = Array.isArray(state.messages) ? state.messages : [];
        messagesElement.className = messages.length === 0 ? "messages empty" : "messages";
        if (messages.length === 0) {
            truncateRenderedMessages(0);
            noticeElement?.remove();
            noticeElement = null;
            if (!emptyElement) {
                emptyElement = document.createElement("div");
                messagesElement.replaceChildren(emptyElement);
            }
            emptyElement.textContent = state.error || bus.i18n.t("Plugin.Chat.Detail.Empty", {
                defaultValue: "Ask MyTools anything"
            });
        } else {
            emptyElement?.remove();
            emptyElement = null;
        }

        const interactionResponses = new Map<string, InteractionResponse>();
        messages.forEach(function (message) {
            const response = message.interactionResponse;
            if (message.role === "user" && response?.interactionId) {
                interactionResponses.set(response.interactionId, response);
            }
        });
        let latestPendingInteractionIndex = -1;
        messages.forEach(function (message, index) {
            if (message.role !== "assistant") return;
            const interaction = parseInteraction(message.content).interaction;
            const hasLaterUserMessage = messages.slice(index + 1).some((later) => later.role === "user");
            if (interaction && !interactionResponses.has(interaction.id) && !hasLaterUserMessage) {
                latestPendingInteractionIndex = index;
            }
        });

        messages.forEach(function (message, index) {
            if (renderedMessages[index]?.role !== message.role) truncateRenderedMessages(index);
            const entry = renderedMessages[index] ?? createRenderedMessage(message.role);
            if (message.role === "assistant") {
                const content = message.content || (state.streaming
                    ? bus.i18n.t("Plugin.Chat.Detail.Streaming", { defaultValue: "…" }) : "");
                const isStreamingMessage = state.streaming && index === messages.length - 1;
                if (isStreamingMessage) scheduleStreamingMarkdown(entry, content);
                else {
                    if (pendingStreamingRender?.entry === entry) pendingStreamingRender = null;
                    renderMessageContent(entry, content, true);
                }
            } else {
                renderMessageContent(entry, message.content, true);
            }
            const interaction = message.role === "assistant" ? parseInteraction(message.content).interaction : null;
            entry.markdown?.setInteractionState(
                interaction ? interactionResponses.get(interaction.id) ?? null : null,
                index === latestPendingInteractionIndex);
            renderUsage(entry, message);
        });
        truncateRenderedMessages(messages.length);

        renderNotice(state, messages.length > 0);

        const streaming = state.streaming === true;
        if (!streaming) cancelRequested = false;
        sendButton.classList.toggle("working", streaming);
        const actionLabel = streaming
            ? bus.i18n.t("Plugin.Chat.Detail.Stop", { defaultValue: "Stop" })
            : bus.i18n.t("Plugin.Chat.Detail.Send", { defaultValue: "Send" });
        sendButton.title = actionLabel;
        sendButton.setAttribute("aria-label", actionLabel);
        sendButton.disabled = streaming && cancelRequested;
        modelSelect.disabled = streaming;
        newChatButton.disabled = streaming;
        renderConversationList();
        scrollToBottom();
        if (streaming) startPolling(); else stopPolling();
        if (wasStreaming && !streaming) void loadConversations();
    }

    async function poll(): Promise<void> {
        if (pollInFlight) {
            pollAfterCurrent = true;
            return;
        }
        pollInFlight = true;
        const requestedSessionId = sessionId;
        try {
            const state = await bus.call<ChatState>("poll", { sessionId: requestedSessionId, model: modelSelect.value });
            if (requestedSessionId === sessionId) renderState(state);
        } catch (error) {
            if (currentState?.streaming) {
                renderState({ ...currentState, streaming: false, error: error instanceof Error ? error.message : String(error) });
            }
        } finally {
            pollInFlight = false;
            if (pollAfterCurrent) {
                pollAfterCurrent = false;
                void poll();
            }
        }
    }

    function startPolling(): void {
        if (pollTimer !== null) return;
        pollTimer = window.setInterval(function () { void poll(); }, POLL_INTERVAL_MS);
    }

    function stopPolling(): void {
        if (pollTimer !== null) window.clearInterval(pollTimer);
        pollTimer = null;
    }

    function scrollToBottom(force = false): void {
        if (!force && userScrolledUp) return;
        messagesElement.scrollTop = messagesElement.scrollHeight;
    }

    function isNearBottom(): boolean {
        return messagesElement.scrollHeight - messagesElement.scrollTop - messagesElement.clientHeight
            <= STICK_TO_BOTTOM_THRESHOLD;
    }

    async function sendMessage(
        messageOverride?: string,
        interactionResponse?: InteractionResponse
    ): Promise<boolean> {
        const message = normalize(messageOverride ?? promptInput.value);
        if (!message || currentState?.streaming) return false;
        const previousState = currentState;
        const model = modelSelect.value;
        if (messageOverride === undefined) promptInput.value = "";
        userScrolledUp = false;
        const optimistic: ChatState = currentState
            ? { ...currentState, messages: [...currentState.messages, { role: "user", content: message, interactionResponse }, { role: "assistant", content: "" }], selectedModel: model, streaming: true, cancelled: false, error: "" }
            : { sessionId, messages: [{ role: "user", content: message, interactionResponse }, { role: "assistant", content: "" }], selectedModel: model, streaming: true, cancelled: false, error: "" };
        renderState(optimistic);
        try {
            await bus.call("send", { sessionId, message, model, interactionResponse });
            return true;
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : String(error);
            renderState(interactionResponse && previousState
                ? { ...previousState, streaming: false, error: errorMessage }
                : { ...optimistic, streaming: false, error: errorMessage });
            return false;
        }
    }

    async function sendInteractionAnswers(
        interactionId: string,
        answers: InteractionAnswer[],
        card: HTMLElement
    ): Promise<boolean> {
        if (currentState?.streaming || card.classList.contains("submitted")) return false;
        const lines = [bus.i18n.t("Plugin.Chat.Detail.AnswerHeading", { defaultValue: "My answers:" })];
        answers.forEach(function (answer, index) {
            lines.push(`${index + 1}. ${answer.prompt}`);
            answer.values.forEach((value) => lines.push(`   - ${value}`));
            if (answer.text) lines.push(`   - ${answer.text}`);
        });
        return sendMessage(lines.join("\n"), { interactionId, answers });
    }

    async function startNewChat(): Promise<void> {
        if (currentState?.streaming) return;
        stopPolling();
        sessionId = newSessionId();
        currentState = null;
        clearRenderedMessages();
        userScrolledUp = false;
        await poll();
        promptInput.focus();
    }

    async function switchConversation(nextSessionId: string): Promise<void> {
        if (!nextSessionId || nextSessionId === sessionId || currentState?.streaming) return;
        stopPolling();
        sessionId = nextSessionId;
        userScrolledUp = false;
        await poll();
        promptInput.focus();
    }

    async function initialize(): Promise<void> {
        try {
            const status = await bus.call<ChatStatus>("status");
            modelSelect.replaceChildren();
            status.models.forEach(function (model) {
                const option = document.createElement("option");
                option.value = model;
                option.textContent = model;
                modelSelect.appendChild(option);
            });
            modelSelect.value = status.selectedModel;
            if (!status.available) {
                promptInput.disabled = true;
                sendButton.disabled = true;
                messagesElement.textContent = status.unavailableReason || "AI unavailable";
                return;
            }
            const conversations = await bus.call<ChatConversationList>("list");
            recentConversations = Array.isArray(conversations.conversations) ? conversations.conversations : [];
            sessionId = recentConversations[0]?.sessionId || newSessionId();
            await poll();
            promptInput.focus();
        } catch (error) {
            messagesElement.textContent = error instanceof Error ? error.message : String(error);
        }
    }

    messagesElement.addEventListener("scroll", function () {
        if (isNearBottom()) userScrolledUp = false;
        else if (currentState?.streaming) userScrolledUp = true;
    });
    sendButton.addEventListener("click", async function () {
        if (!currentState?.streaming) {
            void sendMessage();
            return;
        }
        if (cancelRequested) return;
        cancelRequested = true;
        sendButton.disabled = true;
        try { await bus.call("cancel", { sessionId }); await poll(); }
        finally {
            cancelRequested = false;
            sendButton.disabled = false;
        }
    });
    newChatButton.addEventListener("click", function () { void startNewChat(); });
    promptInput.addEventListener("keydown", function (event) {
        if (event.key !== "Enter" || event.shiftKey) return;
        event.preventDefault();
        void sendMessage();
    });

    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function () { void initialize(); });
    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        const query = normalize(payload.query);
        if (query && !normalize(promptInput.value)) promptInput.value = query;
    });
    bus.on(HostEvents.LanguageChanged, function () {
        if (currentState) renderState(currentState);
        else renderConversationList();
    });
    bus.on(HostEvents.ThemeChanged, function () {
        requestAnimationFrame(() => {
            renderedMessages.forEach((entry) => {
                if (entry.finalized) entry.markdown?.refreshMermaidDiagrams();
            });
        });
    });
})();
