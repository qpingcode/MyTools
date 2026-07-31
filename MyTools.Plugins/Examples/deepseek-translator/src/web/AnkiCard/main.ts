import { tool } from "@qping/plugin-common/web-tool";

(function () {
    type Summary = {
        due?: number;
        total?: number;
    };

    type AnkiCard = {
        id?: string;
        createdAt?: string;
        dueAt?: string;
        stability?: number;
        difficulty?: number;
        reps?: number;
        lapses?: number;
        state?: string;
        type?: string;
        direction?: string;
        sourceText?: string;
        phonetic?: string;
        front?: string;
        back?: string;
        prompt?: string;
        answer?: string;
        options: string[];
    };

    type BrowseState = {
        card?: AnkiCard | null;
        page?: number;
        total?: number;
        hasPrevious?: boolean;
        hasNext?: boolean;
    };

    type AnkiState = {
        status?: string;
        summary?: Summary;
        card?: AnkiCard | null;
        browse?: BrowseState;
        error?: string;
    };

    type FieldControl = {
        wrapper: HTMLLabelElement;
        control: HTMLInputElement | HTMLTextAreaElement;
    };

    var hostEvents = tool.events.host;
    var cardHost = document.getElementById("cardHost") as HTMLElement;
    var summary = document.getElementById("summary") as HTMLElement;
    var backButton = document.getElementById("backButton") as HTMLButtonElement;
    var browseButton = document.getElementById("browseButton") as HTMLButtonElement;
    var currentCard: AnkiCard | null = null;
    var selectedOption = "";
    var reviewTimer: number | null = null;
    var currentBrowsePage = 0;

    async function callState(action: string, data: Record<string, unknown> = {}): Promise<void> {
        try {
            renderState(await tool.call(action, data || {}));
        } catch (error) {
            renderState({
                status: "error",
                summary: {},
                card: null,
                error: error instanceof Error ? error.message : String(error)
            });
        }
    }

    function updateSummary(value: Summary | undefined): void {
        var current = value || {};
        summary.textContent = (current.due || 0) + " due · " + (current.total || 0) + " total";
    }

    function setHeaderMode(mode: string): void {
        var browsing = mode === "browse" || mode === "edit";
        backButton.hidden = !browsing;
        browseButton.hidden = browsing;
    }

    function isBasicCard(card: AnkiCard | null): boolean {
        return (card && card.type) === "basic";
    }

    function cardTypeLabel(card: AnkiCard | null): string {
        var type = card ? card.type : "";
        if (type === "basic") {
            return "Original";
        }

        if (type === "choice-zh-to-en" || card && card.direction === "zh-to-en") {
            return "中文选英文";
        }

        return "英文选中文";
    }

    function appendCardPhoneticRow(parent: Element, card: AnkiCard | null): void {
        if (!isBasicCard(card) || !window.DeepSeekTranslatorSpeech) {
            return;
        }

        var word = card.front || card.prompt || card.sourceText;
        if (!card.phonetic && !/^[A-Za-z][A-Za-z'-]*$/.test(word || "")) {
            return;
        }

        window.DeepSeekTranslatorSpeech.appendPhoneticRow(parent, {
            phonetic: card.phonetic,
            word: word
        });
    }

    function renderState(state: AnkiState): void {
        var current = state || {};
        updateSummary(current.summary);
        currentCard = current.card || null;
        selectedOption = "";
        if (reviewTimer !== null) {
            window.clearTimeout(reviewTimer);
        }
        cardHost.replaceChildren();

        if (current.status === "error") {
            setHeaderMode("review");
            cardHost.className = "card-host error";
            cardHost.textContent = current.error || "Failed to load cards";
            return;
        }

        if (current.status === "browse") {
            setHeaderMode("browse");
            renderBrowseState(current.browse || {});
            return;
        }

        setHeaderMode("review");
        if (!currentCard) {
            cardHost.className = "card-host empty";
            var empty = document.createElement("div");
            empty.textContent = "No cards are due now. 收藏单词后会自动生成 Anki 卡片。";
            cardHost.append(empty);
            return;
        }

        cardHost.className = "card-host";

        var source = document.createElement("div");
        source.className = "source";
        source.textContent = currentCard.sourceText + " · " + cardTypeLabel(currentCard);
        cardHost.appendChild(source);

        var prompt = document.createElement("div");
        prompt.className = "prompt";
        prompt.textContent = currentCard.front || currentCard.prompt;
        cardHost.appendChild(prompt);

        if (isBasicCard(currentCard)) {
            appendCardPhoneticRow(cardHost, currentCard);

            var showAnswer = document.createElement("button");
            showAnswer.className = "show-answer";
            showAnswer.type = "button";
            showAnswer.textContent = "Show Answer";
            showAnswer.addEventListener("click", function () {
                showAnswer.disabled = true;
                renderBasicAnswer();
            });
            var showAnswerRow = document.createElement("div");
            showAnswerRow.className = "show-answer-row";
            showAnswerRow.appendChild(showAnswer);
            cardHost.appendChild(showAnswerRow);
            return;
        }

        var options = document.createElement("div");
        options.className = "options";
        currentCard.options.forEach(function (option: string) {
            var button = document.createElement("button");
            button.className = "option";
            button.type = "button";
            button.textContent = option;
            button.addEventListener("click", function () {
                selectedOption = option;
                renderChoiceAnswer(options, option === currentCard.answer ? 4 : 1);
            });
            options.appendChild(button);
        });
        cardHost.appendChild(options);
    }

    function renderBrowseState(browse: BrowseState): void {
        currentCard = browse.card || null;
        currentBrowsePage = browse.page || 0;
        cardHost.replaceChildren();
        if (!currentCard) {
            cardHost.className = "card-host empty";
            var empty = document.createElement("div");
            empty.textContent = "No cards have been generated yet.";
            var emptyTop = document.createElement("div");
            emptyTop.className = "browse-card-top";
            var emptySource = document.createElement("div");
            emptySource.className = "source";
            emptySource.textContent = "Browse All Cards";
            emptyTop.append(emptySource, createCardActions(browse));
            cardHost.append(emptyTop, empty);
            return;
        }

        cardHost.className = "card-host browse";
        var top = document.createElement("div");
        top.className = "browse-card-top";
        var source = document.createElement("div");
        source.className = "source";
        source.textContent = "Browse " + (browse.page + 1) + " / " + browse.total + " · " + cardTypeLabel(currentCard) + " · " + currentCard.sourceText;
        top.append(source, createCardActions(browse));
        cardHost.appendChild(top);

        var prompt = document.createElement("div");
        prompt.className = "prompt";
        prompt.textContent = currentCard.front || currentCard.prompt;
        cardHost.appendChild(prompt);
        appendCardPhoneticRow(cardHost, currentCard);

        if (!isBasicCard(currentCard)) {
            var options = document.createElement("div");
            options.className = "options";
            currentCard.options.forEach(function (option: string) {
                var item = document.createElement("div");
                item.className = option === currentCard.answer ? "option correct browse-option" : "option browse-option";
                item.textContent = option;
                options.appendChild(item);
            });
            cardHost.appendChild(options);
        }

        var back = document.createElement("div");
        back.className = "answer";
        back.textContent = (isBasicCard(currentCard) ? "Back: " : "Answer: " + currentCard.answer + " · ") + (currentCard.back || currentCard.answer);
        cardHost.appendChild(back);

        var nav = document.createElement("div");
        nav.className = "browse-nav";
        var previous = document.createElement("button");
        previous.type = "button";
        previous.textContent = "Previous";
        previous.disabled = !browse.hasPrevious;
        previous.addEventListener("click", function () {
            requestBrowsePage(browse.page - 1);
        });
        var next = document.createElement("button");
        next.type = "button";
        next.textContent = "Next";
        next.disabled = !browse.hasNext;
        next.addEventListener("click", function () {
            requestBrowsePage(browse.page + 1);
        });
        nav.append(previous, next);
        cardHost.appendChild(nav);
    }

    function createCardActions(browse: BrowseState): HTMLDivElement {
        var toolbar = document.createElement("div");
        toolbar.className = "card-actions";
        ([
            ["＋", "New Card", function () { renderEditForm(null, browse.page || 0); }],
            ["✎", "Edit", function () { if (currentCard) { renderEditForm(currentCard, browse.page || 0); } }],
            ["🗑", "Delete", function () { deleteCurrentCard(browse.page || 0); }]
        ] as [string, string, () => void][]).forEach(function (item) {
            var button = document.createElement("button");
            button.type = "button";
            button.textContent = item[0];
            button.title = item[1];
            button.setAttribute("aria-label", item[1]);
            button.disabled = (item[1] === "Edit" || item[1] === "Delete") && !currentCard;
            button.addEventListener("click", item[2]);
            toolbar.appendChild(button);
        });
        return toolbar;
    }

    function renderEditForm(card: AnkiCard | null, page: number): void {
        var editing = !!card;
        setHeaderMode("edit");
        cardHost.className = "card-host";
        cardHost.replaceChildren();

        var title = document.createElement("div");
        title.className = "source";
        title.textContent = editing ? "Edit card" : "New card";
        cardHost.appendChild(title);

        var form = document.createElement("div");
        form.className = "card-form";
        var source = createField("Source", card ? card.sourceText : "");
        var phonetic = createField("Phonetic", card ? card.phonetic : "");
        var front = createField("Front", card ? (card.front || card.prompt) : "");
        var back = createField("Back", card ? (card.back || card.answer) : "", true);
        var answer = createField("Answer", card ? card.answer : "");
        var options = createField("Options (one per line)", card ? card.options.join("\n") : "", true);
        var type = document.createElement("select");
        type.className = "form-control";
        [["basic", "Original Anki card"], ["choice-en-to-zh", "Choice: English to Chinese"], ["choice-zh-to-en", "Choice: Chinese to English"]].forEach(function (item) {
            var option = document.createElement("option");
            option.value = item[0];
            option.textContent = item[1];
            option.selected = (card ? (card.type || "choice-en-to-zh") : "basic") === item[0];
            type.appendChild(option);
        });
        var answerWrapper = answer.wrapper;
        var optionsWrapper = options.wrapper;
        form.append(source.wrapper, wrapControl("Type", type), phonetic.wrapper, front.wrapper, back.wrapper, answerWrapper, optionsWrapper);

        function updateFormVisibility() {
            var choice = type.value !== "basic";
            answerWrapper.style.display = choice ? "grid" : "none";
            optionsWrapper.style.display = choice ? "grid" : "none";
        }

        type.addEventListener("change", updateFormVisibility);
        updateFormVisibility();

        var actions = document.createElement("div");
        actions.className = "browse-nav";
        var cancel = document.createElement("button");
        cancel.type = "button";
        cancel.textContent = "Cancel";
        cancel.addEventListener("click", function () { requestBrowsePage(page); });
        var save = document.createElement("button");
        save.type = "button";
        save.textContent = "Save";
        save.addEventListener("click", function () {
            var isChoice = type.value !== "basic";
            var nextOptions = isChoice ? splitOptions(options.control.value) : [];
            var nextAnswer = isChoice ? answer.control.value.trim() : back.control.value.trim();
            if (isChoice && editing && nextAnswer === card.answer && nextOptions.indexOf(card.answer) < 0) {
                var answerIndex = card.options.indexOf(card.answer);
                if (answerIndex >= 0 && answerIndex < nextOptions.length) {
                    nextAnswer = nextOptions[answerIndex];
                }
            }

            callState("saveCard", {
                page: editing ? page : 0,
                card: {
                    id: card ? card.id : "",
                    createdAt: card ? card.createdAt : "",
                    dueAt: card ? card.dueAt : "",
                    stability: card ? card.stability : 0,
                    difficulty: card ? card.difficulty : 0,
                    reps: card ? card.reps : 0,
                    lapses: card ? card.lapses : 0,
                    state: card ? card.state : "new",
                    type: type.value,
                    sourceText: source.control.value,
                    phonetic: phonetic.control.value,
                    front: front.control.value,
                    back: back.control.value,
                    prompt: front.control.value,
                    answer: nextAnswer,
                    options: nextOptions
                }
            });
        });
        actions.append(cancel, save);
        cardHost.append(form, actions);
    }

    function splitOptions(value: string): string[] {
        var seen: Record<string, boolean> = {};
        return (value || "").split(/\r?\n/)
            .map(function (item: string) { return item.trim(); })
            .filter(function (item: string) {
                if (!item || seen[item]) {
                    return false;
                }

                seen[item] = true;
                return true;
            });
    }

    function createField(labelText: string, value: unknown, multiline = false): FieldControl {
        var control = multiline ? document.createElement("textarea") : document.createElement("input");
        control.className = "form-control";
        control.value = typeof value === "string" ? value : "";
        return {
            wrapper: wrapControl(labelText, control),
            control: control
        };
    }

    function wrapControl(labelText: string, control: HTMLElement): HTMLLabelElement {
        var wrapper = document.createElement("label");
        wrapper.className = "form-field";
        var label = document.createElement("span");
        label.textContent = labelText;
        wrapper.append(label, control);
        return wrapper;
    }

    function deleteCurrentCard(page: number): void {
        if (!currentCard || !window.confirm("Delete this card?")) {
            return;
        }

        callState("deleteCard", {
            cardId: currentCard.id,
            page: page
        });
    }

    function renderChoiceAnswer(optionsElement: HTMLElement, rating: number): void {
        if (!currentCard) {
            return;
        }
        var isCorrect = rating === 4;
        Array.prototype.forEach.call(optionsElement.children, function (button: HTMLButtonElement) {
            button.disabled = true;
            if (button.textContent === currentCard.answer) {
                button.classList.add("correct");
            } else if (button.textContent === selectedOption) {
                button.classList.add("wrong");
            }
        });

        var result = document.createElement("div");
        result.className = isCorrect ? "choice-result correct" : "choice-result wrong";
        result.textContent = isCorrect ? "Correct · rating Easy" : "Wrong · rating Again";
        cardHost.appendChild(result);

        var answer = document.createElement("div");
        answer.className = "answer";
        answer.textContent = "Answer: " + currentCard.answer + (currentCard.back ? " · " + currentCard.back : "");
        cardHost.appendChild(answer);

        var countdownRow = document.createElement("div");
        countdownRow.className = "countdown-row";
        var countdown = document.createElement("div");
        countdown.className = "countdown";
        var nextButton = document.createElement("button");
        nextButton.className = "next-card-button";
        nextButton.type = "button";
        nextButton.textContent = "Next";
        countdownRow.append(countdown, nextButton);
        cardHost.appendChild(countdownRow);
        startChoiceAutoReview(rating, countdown, nextButton);
    }

    function startChoiceAutoReview(rating: number, countdownElement: HTMLElement, nextButton: HTMLButtonElement): void {
        var secondsLeft = 5;
        var submitted = false;
        countdownElement.textContent = "Next card in " + secondsLeft + "s";
        if (reviewTimer !== null) {
            window.clearTimeout(reviewTimer);
        }

        function submitNow(): void {
            if (submitted) {
                return;
            }

            submitted = true;
            nextButton.disabled = true;
            submitReview(rating);
        }

        nextButton.addEventListener("click", submitNow);

        function tick(): void {
            secondsLeft -= 1;
            if (secondsLeft <= 0) {
                submitNow();
                return;
            }

            countdownElement.textContent = "Next card in " + secondsLeft + "s";
            reviewTimer = window.setTimeout(tick, 1000);
        }

        reviewTimer = window.setTimeout(tick, 1000);
    }

    function renderBasicAnswer(): void {
        if (!currentCard) {
            return;
        }
        if (cardHost.querySelector(".answer.back") || cardHost.querySelector(".review-actions")) {
            return;
        }

        var answer = document.createElement("div");
        answer.className = "answer back";
        answer.textContent = currentCard.back || currentCard.answer;
        cardHost.appendChild(answer);
        renderRatingButtons();
    }

    function renderRatingButtons(): void {
        var actions = document.createElement("div");
        actions.className = "review-actions";
        ([
            ["Again", 1],
            ["Hard", 2],
            ["Good", 3],
            ["Easy", 4]
        ] as [string, number][]).forEach(function (item) {
            var button = document.createElement("button");
            button.type = "button";
            button.dataset.rating = String(item[1]);
            button.textContent = item[0];
            button.addEventListener("click", function () {
                submitReview(item[1]);
            });
            actions.appendChild(button);
        });
        cardHost.appendChild(actions);
    }

    function submitReview(rating: number): void {
        if (!currentCard) {
            return;
        }

        if (reviewTimer !== null) {
            window.clearTimeout(reviewTimer);
        }
        cardHost.classList.add("submitting");
        callState("review", {
            cardId: currentCard.id,
            rating: rating
        });
    }

    function requestNextCard() {
        setHeaderMode("review");
        cardHost.className = "card-host empty";
        cardHost.textContent = "Loading cards";
        callState("load");
    }

    function requestBrowsePage(page: number): void {
        setHeaderMode("browse");
        cardHost.className = "card-host empty";
        cardHost.textContent = "Loading cards";
        callState("browse", { page: page });
    }

    backButton.addEventListener("click", requestNextCard);
    browseButton.addEventListener("click", function () {
        requestBrowsePage(0);
    });
    setHeaderMode("review");

    tool.subscribe(hostEvents.initialize, function () {
        requestNextCard();
    });
    tool.ready("deepseek-translator:ankicard");
})();
