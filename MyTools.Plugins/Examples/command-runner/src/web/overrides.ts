import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload } from "@qping/plugin-bus/web";

type CommandConfig = {
    name?: string;
    command?: string;
    args?: string;
    runAsAdmin?: boolean;
    isBashScript?: boolean;
    scripts?: string | string[];
    workingDirectory?: string;
};

type OverridesState = {
    itemId?: string;
    command?: CommandConfig;
};

(function () {
    const bus = createWebBusClient();
    const nameInput = document.getElementById("name") as HTMLInputElement;
    const scriptModeInput = document.getElementById("isBashScript") as HTMLInputElement;
    const commandInput = document.getElementById("command") as HTMLInputElement;
    const argsInput = document.getElementById("args") as HTMLInputElement;
    const scriptsInput = document.getElementById("scripts") as HTMLTextAreaElement;
    const workingDirectoryInput = document.getElementById("workingDirectory") as HTMLInputElement;
    const runAsAdminInput = document.getElementById("runAsAdmin") as HTMLInputElement;
    const commandFields = document.getElementById("commandFields") as HTMLElement;
    const scriptsField = document.getElementById("scriptsField") as HTMLElement;
    const status = document.getElementById("status") as HTMLElement;
    let itemId = "";

    function scriptsText(value: string | string[] | undefined): string {
        return Array.isArray(value) ? value.join("\n") : String(value || "");
    }

    function readCommand(): CommandConfig {
        return {
            name: nameInput.value,
            command: commandInput.value,
            args: argsInput.value,
            runAsAdmin: runAsAdminInput.checked,
            isBashScript: scriptModeInput.checked,
            scripts: scriptsInput.value,
            workingDirectory: workingDirectoryInput.value,
        };
    }

    function updateMode(): void {
        commandFields.classList.toggle("hidden", scriptModeInput.checked);
        scriptsField.classList.toggle("hidden", !scriptModeInput.checked);
    }

    function showSyncError(error: unknown): void {
        status.textContent = bus.i18n.t("Plugin.CommandRunner.Error.OverridesSyncFailed", {
            defaultValue: "Could not update the temporary configuration: {{message}}",
            message: error instanceof Error ? error.message : String(error),
        });
    }

    function stage(): void {
        if (!itemId) return;
        status.textContent = "";
        void bus.call("stageOverrides", { itemId, command: readCommand() }).catch(showSyncError);
    }

    function populate(command: CommandConfig): void {
        nameInput.value = command.name || "";
        commandInput.value = command.command || "";
        argsInput.value = command.args || "";
        scriptsInput.value = scriptsText(command.scripts);
        workingDirectoryInput.value = command.workingDirectory || "";
        runAsAdminInput.checked = command.runAsAdmin === true;
        scriptModeInput.checked = command.isBashScript === true;
        updateMode();
    }

    [nameInput, commandInput, argsInput, scriptsInput, workingDirectoryInput].forEach(function (input) {
        input.addEventListener("input", stage);
    });
    runAsAdminInput.addEventListener("change", stage);
    scriptModeInput.addEventListener("change", function () {
        updateMode();
        stage();
    });

    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        const initial = (payload.initialState || {}) as OverridesState;
        itemId = typeof initial.itemId === "string" ? initial.itemId : payload.itemId || "";
        populate(initial.command || {});
        stage();
        nameInput.focus();
        nameInput.select();
    });
})();
