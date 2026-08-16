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
            <v-card rounded="lg" class="command-editor">
                <v-card-title>
                    {{ editingIndex == null ? labels.add : labels.edit }}
                </v-card-title>
                <v-card-text class="editor-body editor-form">
                    <div class="form-row">
                        <div class="form-label">{{ labels.name }}</div>
                        <v-text-field v-model="draft.name" variant="solo" hide-details />
                    </div>
                    <div class="form-row">
                        <div class="form-label">{{ labels.scriptMode }}</div>
                        <v-switch v-model="draft.isBashScript" hide-details />
                    </div>
                    <div v-if="draft.isBashScript" class="form-row form-row-top">
                        <div class="form-label">
                            <div>{{ labels.scripts }}</div>
                            <div class="form-hint">{{ labels.scriptsHint }}</div>
                        </div>
                        <v-textarea
                            v-model="scriptsText"
                            variant="solo"
                            hide-details
                            auto-grow
                            rows="4"
                        />
                    </div>
                    <template v-else>
                        <div class="form-row">
                            <div class="form-label">{{ labels.command }}</div>
                            <v-text-field v-model="draft.command" variant="solo" hide-details />
                        </div>
                        <div class="form-row">
                            <div class="form-label">{{ labels.args }}</div>
                            <v-text-field v-model="draft.args" variant="solo" hide-details />
                        </div>
                    </template>
                    <div class="form-row">
                        <div class="form-label">{{ labels.workingDirectory }}</div>
                        <v-text-field v-model="draft.workingDirectory" variant="solo" hide-details />
                    </div>
                    <div class="form-row">
                        <div class="form-label">{{ labels.runAsAdmin }}</div>
                        <v-switch v-model="draft.runAsAdmin" hide-details />
                    </div>
                </v-card-text>
                <v-card-actions class="editor-actions">
                    <v-spacer />
                    <v-btn variant="tonal" size="default" rounded="lg" class="editor-btn" @click="editorOpen = false">
                        {{ labels.cancel }}
                    </v-btn>
                    <v-btn
                        color="primary"
                        variant="flat"
                        size="default"
                        rounded="lg"
                        class="editor-btn"
                        :disabled="!canSave()"
                        @click="saveEditor"
                    >
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

.editor-form {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    column-gap: 12px;
    row-gap: 6px;
    align-items: center;
}

.form-row {
    display: contents;
}

.form-row-top .form-label {
    align-self: start;
    line-height: 1.35;
}

.form-label {
    font-size: 13px;
    font-weight: 500;
    line-height: 32px;
    white-space: nowrap;
    color: var(--mt-text, #fff);
}

.form-hint {
    margin-top: 0;
    font-size: 11px;
    font-weight: 400;
    line-height: 1.35;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.form-row :deep(.v-input) {
    min-width: 0;
}

.form-row :deep(.v-switch) {
    justify-self: start;
    width: auto;
}

.form-row :deep(.v-field) {
    border: none;
    box-shadow: none;
    border-radius: 8px;
    background: var(--mt-surface-alt, #333333);
}

.form-row :deep(.v-field__overlay) {
    background: var(--mt-surface-alt, #333333);
    opacity: 1;
}

.form-row :deep(.v-field__outline) {
    display: none;
}

.form-row :deep(.v-field--focused .v-field__overlay) {
    background: var(--mt-surface-hover, #3a3a3a);
}

.editor-body {
    padding: 4px 16px 8px !important;
}

.editor-actions {
    padding: 4px 16px 12px !important;
    gap: 8px;
}

.editor-btn {
    min-width: 80px;
}

.command-editor :deep(.v-card-title) {
    padding: 12px 16px 4px !important;
}
</style>
