import { tool } from "@qping/plugin-common/web-tool";

(function () {
    var currentState = {};

    function updateQuery(query: unknown): void {
        var text = typeof query === "string" ? query.trim() : "";
        var normalized = text.length > 0 ? text : "(empty)";
        document.getElementById("query").textContent = normalized;
        document.getElementById("title").textContent = normalized === "(empty)"
            ? "Type after hello"
            : "Hello " + normalized;
    }

    function updateState(state: unknown): void {
        currentState = state || {};
        document.getElementById("state").textContent = JSON.stringify(currentState, null, 2);
    }

    document.getElementById("refresh").addEventListener("click", async function () {
        try {
            updateState(await tool.call("refresh", { currentQuery: document.getElementById("query").textContent }));
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
    tool.ready("hello-search");
})();
