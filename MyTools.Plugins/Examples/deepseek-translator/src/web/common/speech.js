(function () {
    function normalize(value) {
        return typeof value === "string" ? value.trim() : "";
    }

    function getSpeechSynthesis() {
        return typeof window.speechSynthesis === "object" ? window.speechSynthesis : null;
    }

    function getEnglishVoice(synth) {
        var voices = typeof synth.getVoices === "function" ? synth.getVoices() : [];
        return voices.find(function (voice) {
            return voice.lang && voice.lang.toLowerCase() === "en-us";
        }) || voices.find(function (voice) {
            return voice.lang && voice.lang.toLowerCase().indexOf("en") === 0;
        }) || null;fcf
    }

    function canSpeak() {
        return !!getSpeechSynthesis() && typeof window.SpeechSynthesisUtterance === "function";
    }

    function speakWord(word) {
        var text = normalize(word);
        var synth = getSpeechSynthesis();
        if (!text || !synth || typeof window.SpeechSynthesisUtterance !== "function") {
            return;
        }

        synth.cancel();
        var utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = "en-US";
        utterance.rate = 0.95;

        var voice = getEnglishVoice(synth);
        if (voice) {
            utterance.voice = voice;
            utterance.lang = voice.lang || utterance.lang;
        }

        synth.speak(utterance);
    }

    function appendPhoneticRow(parent, options) {
        var current = options || {};
        var phonetic = normalize(current.phonetic);
        var word = normalize(current.word);
        if (!phonetic && (!canSpeak() || !word)) {
            return;
        }

        var row = document.createElement("div");
        row.className = current.rowClassName || "phonetic-row";

        if (phonetic) {
            var phoneticElement = document.createElement("span");
            phoneticElement.className = current.phoneticClassName || "phonetic";
            phoneticElement.textContent = phonetic;
            row.appendChild(phoneticElement);
        }

        if (canSpeak() && word) {
            var button = document.createElement("button");
            button.className = current.buttonClassName || "pronounce-button";
            button.type = "button";
            button.title = "Play pronunciation";
            button.setAttribute("aria-label", "Play pronunciation");
            button.textContent = "🔊";
            button.addEventListener("click", function () {
                speakWord(word);
            });
            row.appendChild(button);
        }

        parent.appendChild(row);
    }

    window.DeepSeekTranslatorSpeech = {
        appendPhoneticRow: appendPhoneticRow,
        canSpeak: canSpeak,
        speakWord: speakWord
    };
})();
