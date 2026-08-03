import { tool } from "@qping/plugin-common/web-tool";

(function () {
    var currentState = {};
    var currentQuery = "";

    function updateQuery(query: unknown): void {
        var text = typeof query === "string" ? query.trim() : "";
        currentQuery = text;
        var normalized = text.length > 0
            ? text
            : tool.i18n.t("Plugin.HelloSearch.Common.Empty", { defaultValue: "(empty)" });
        document.getElementById("query")!.textContent = normalized;
        document.getElementById("title")!.textContent = text.length === 0
            ? tool.i18n.t("Plugin.HelloSearch.Detail.TypeAfterHello", { defaultValue: "Type after hello" })
            : tool.i18n.t("Plugin.HelloSearch.Result.Greeting", { defaultValue: "Hello {{name}}", name: normalized });
    }

    function updateState(state: unknown): void {
        currentState = state || {};
        document.getElementById("state")!.textContent = JSON.stringify(currentState, null, 2);
    }

    document.getElementById("refresh")!.addEventListener("click", async function () {
        try {
            updateState(await tool.call("refresh", { currentQuery: document.getElementById("query")!.textContent }));
        } catch (error) {
            updateState({ error: error instanceof Error ? error.message : String(error) });
        }
    });

    tool.subscribe(tool.events.host.search, function (payload) {
        updateQuery(payload.query || "");
    });
    tool.subscribe(tool.events.host.initialize, function (payload) {
        updateQuery(payload.query || "");
        updateState(payload.initialState || {});
    });
    tool.subscribe(tool.events.host.languageChanged, function () {
        updateQuery(currentQuery);
        updateState(currentState);
    });
    tool.ready("hello-search");
})();
