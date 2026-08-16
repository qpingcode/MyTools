<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import HighlightText from "../components/HighlightText.vue";
import { t } from "../i18n";
import { markCommandsDirty, store } from "../store";
import type { CommandConfig } from "../types";

const editorOpen = ref(false);
const editingIndex = ref<number | null>(null);
const draft = reactive<CommandConfig>({
    name: "",
    command: "",
    args: "",
    runAsAdmin: false,
    isBashScript: false,
    scripts: [],
    workingDirectory: "",
});
const scriptsText = ref("");

const commands = computed(() => store.commands || []);

const labels = computed(() => ({
    empty: t("Plugin.Settings.CommandRunner.Empty", "No commands configured"),
    headerName: t("Plugin.Settings.CommandRunner.HeaderName", "Name"),
    headerCommand: t("Plugin.Settings.CommandRunner.HeaderCommand", "Command"),
    headerAdmin: t("Plugin.Settings.CommandRunner.HeaderAdmin", "Admin"),
    add: t("Plugin.Settings.CommandRunner.Add", "Add command"),
    edit: t("Plugin.Settings.CommandRunner.Edit", "Edit command"),
    delete: t("Plugin.Settings.CommandRunner.Delete", "Delete"),
    name: t("Plugin.Settings.CommandRunner.Name", "Name"),
    scriptMode: t("Plugin.Settings.CommandRunner.ScriptMode", "Batch script"),
    scripts: t("Plugin.Settings.CommandRunner.Scripts", "Script lines"),
    scriptsHint: t("Plugin.Settings.CommandRunner.ScriptsHint", "One command per line"),
    command: t("Plugin.Settings.CommandRunner.Command", "Command / URL"),
    args: t("Plugin.Settings.CommandRunner.Args", "Arguments"),
    workingDirectory: t("Plugin.Settings.CommandRunner.WorkingDirectory", "Working directory"),
    runAsAdmin: t("Plugin.Settings.CommandRunner.RunAsAdmin", "Run as administrator"),
    apply: t("Plugin.Settings.CommandRunner.Apply", "Apply"),
    cancel: t("Plugin.Settings.Cancel", "Cancel"),
}));

function subtitle(command: CommandConfig): string {
    if (command.isBashScript) {
        return (command.scripts || []).join(" && ");
    }
    return `${command.command || ""} ${command.args || ""}`.trim();
}

function markDirty(): void {
    markCommandsDirty();
}

function emptyDraft(): CommandConfig {
    return {
        name: "",
        command: "",
        args: "",
        runAsAdmin: false,
        isBashScript: false,
        scripts: [],
        workingDirectory: "",
    };
}

function openEditor(index: number | null): void {
    editingIndex.value = index;
    const source = index == null ? emptyDraft() : { ...commands.value[index] };
    Object.assign(draft, {
        name: source.name || "",
        command: source.command || "",
        args: source.args || "",
        runAsAdmin: !!source.runAsAdmin,
        isBashScript: !!source.isBashScript,
        scripts: source.scripts ? [...source.scripts] : [],
        workingDirectory: source.workingDirectory || "",
    });
    scriptsText.value = (draft.scripts || []).join("\n");
    editorOpen.value = true;
}

function saveEditor(): void {
    const next: CommandConfig = {
        name: draft.name.trim(),
        runAsAdmin: !!draft.runAsAdmin,
        isBashScript: !!draft.isBashScript,
        workingDirectory: draft.workingDirectory?.trim() || undefined,
    };
    if (next.isBashScript) {
        next.scripts = scriptsText.value.split(/\r?\n/).map((line) => line.trimEnd()).filter((line) => line.length > 0);
    } else {
        next.command = draft.command?.trim() || "";
        next.args = draft.args || "";
    }
    if (!store.commands) store.commands = [];
    if (editingIndex.value == null) {
        store.commands.push(next);
    } else {
        store.commands.splice(editingIndex.value, 1, next);
    }
    markDirty();
    editorOpen.value = false;
}

function removeCommand(index: number): void {
    if (!store.commands) return;
    store.commands.splice(index, 1);
    markDirty();
}

function canSave(): boolean {
    if (!draft.name.trim()) return false;
    if (draft.isBashScript) return scriptsText.value.trim().length > 0;
    return !!(draft.command && draft.command.trim());
}
</script>

<template>
    <div>
        <div v-if="commands.length === 0" class="empty">
            {{ labels.empty }}
        </div>
        <v-table v-else density="compact" class="command-table">
            <thead>
                <tr>
                    <th class="col-name">{{ labels.headerName }}</th>
                    <th>{{ labels.headerCommand }}</th>
                    <th class="col-admin">{{ labels.headerAdmin }}</th>
                    <th class="col-actions"></th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="(command, index) in commands" :key="index">
                    <td class="col-name">
                        <span class="ellipsis" :title="command.name">
                            <HighlightText :text="command.name" :query="store.searchQuery" />
                        </span>
                    </td>
                    <td class="col-command">
                        <span class="mono ellipsis" :title="subtitle(command)">
                            <HighlightText :text="subtitle(command)" :query="store.searchQuery" />
                        </span>
                    </td>
                    <td class="col-admin">
                        <v-icon v-if="command.runAsAdmin" icon="mdi-shield-account" size="small" />
                    </td>
                    <td class="col-actions">
                        <v-btn icon="mdi-pencil" size="x-small" variant="text" @click="openEditor(index)" />
                        <v-btn
                            icon="mdi-close"
                            size="x-small"
                            variant="text"
                            :title="labels.delete"
                            @click="removeCommand(index)"
                        />
                    </td>
                </tr>
            </tbody>
        </v-table>
        <div class="add-bar">
            <v-btn variant="tonal" size="small" prepend-icon="mdi-plus" @click="openEditor(null)">
                {{ labels.add }}
            </v-btn>
        </div>

        <v-dialog v-model="editorOpen" max-width="560" persistent>
            <v-card rounded="lg">
                <v-card-title>
                    {{ editingIndex == null ? labels.add : labels.edit }}
                </v-card-title>
                <v-card-text>
                    <v-text-field
                        v-model="draft.name"
                        :label="labels.name"
                    />
                    <v-switch
                        v-model="draft.isBashScript"
                        :label="labels.scriptMode"
                        class="mb-3"
                    />
                    <v-textarea
                        v-if="draft.isBashScript"
                        v-model="scriptsText"
                        :label="labels.scripts"
                        :hint="labels.scriptsHint"
                        persistent-hint
                        auto-grow
                        rows="6"
                    />
                    <template v-else>
                        <v-text-field
                            v-model="draft.command"
                            :label="labels.command"
                        />
                        <v-text-field
                            v-model="draft.args"
                            :label="labels.args"
                        />
                    </template>
                    <v-text-field
                        v-model="draft.workingDirectory"
                        :label="labels.workingDirectory"
                    />
                    <v-switch
                        v-model="draft.runAsAdmin"
                        :label="labels.runAsAdmin"
                    />
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" rounded="lg" @click="editorOpen = false">
                        {{ labels.cancel }}
                    </v-btn>
                    <v-btn color="primary" rounded="lg" :disabled="!canSave()" @click="saveEditor">
                        {{ labels.apply }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>
    </div>
</template>

<style scoped>
.empty {
    padding: 40px;
    text-align: center;
    opacity: 0.6;
}

.command-table {
    table-layout: fixed;
    width: 100%;
}

.col-name {
    width: 25%;
}

.col-command {
    max-width: 0;
}

.ellipsis {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.mono {
    font-family: "Cascadia Code", Consolas, monospace;
    font-size: 12px;
    opacity: 0.8;
}

.col-admin,
.col-actions {
    width: 72px;
    white-space: nowrap;
}

.add-bar {
    padding: 16px 0;
}

.mb-3 {
    margin-bottom: 12px;
}
</style>
