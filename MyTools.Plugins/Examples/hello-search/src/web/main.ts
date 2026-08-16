import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

(function () {
    const bus = createWebBusClient();
    var currentState = {};
    var currentQuery = "";

    function updateQuery(query: unknown): void {
        var text = typeof query === "string" ? query.trim() : "";
        currentQuery = text;
        var normalized = text.length > 0
            ? text
            : bus.i18n.t("Plugin.HelloSearch.Common.Empty", { defaultValue: "(empty)" });
        document.getElementById("query")!.textContent = normalized;
        document.getElementById("title")!.textContent = text.length === 0
            ? bus.i18n.t("Plugin.HelloSearch.Detail.TypeAfterHello", { defaultValue: "Type after hello" })
            : bus.i18n.t("Plugin.HelloSearch.Result.Greeting", { defaultValue: "Hello {{name}}", name: normalized });
    }

    function updateState(state: unknown): void {
        currentState = state || {};
        document.getElementById("state")!.textContent = JSON.stringify(currentState, null, 2);
    }

    document.getElementById("refresh")!.addEventListener("click", async function () {
        try {
            updateState(await bus.detailCall("refresh", { currentQuery: document.getElementById("query")!.textContent }));
        } catch (error) {
            updateState({ error: error instanceof Error ? error.message : String(error) });
        }
    });

    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        updateQuery(payload.query || "");
    });
    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        updateQuery(payload.query || "");
        updateState(payload.initialState || {});
    });
    bus.on(HostEvents.LanguageChanged, function () {
        updateQuery(currentQuery);
        updateState(currentState);
    });
})();
