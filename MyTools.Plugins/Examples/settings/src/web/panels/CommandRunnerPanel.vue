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
        <div v-else class="command-table">
            <div class="table-head">
                <div class="col-name">{{ labels.headerName }}</div>
                <div class="col-command">{{ labels.headerCommand }}</div>
                <div class="col-admin">{{ labels.headerAdmin }}</div>
                <div class="col-actions"></div>
            </div>
            <div v-for="(command, index) in commands" :key="index" class="table-row">
                <div class="col-name">
                    <span class="ellipsis" :title="command.name">
                        <HighlightText :text="command.name" :query="store.searchQuery" />
                    </span>
                </div>
                <div
                    class="col-command col-command-clickable"
                    :title="t('Plugin.Settings.CommandRunner.Edit', 'Edit command')"
                    @click="openEditor(index)"
                >
                    <span class="mono ellipsis" :title="subtitle(command)">
                        <HighlightText :text="subtitle(command)" :query="store.searchQuery" />
                    </span>
                </div>
                <div class="col-admin">
                    <i v-if="command.runAsAdmin" class="mdi mdi-shield-account"></i>
                </div>
                <div class="col-actions">
                    <button
                        type="button"
                        class="icon-delete-btn"
                        :title="labels.delete"
                        @click="removeCommand(index)"
                    >
                        <i class="mdi mdi-trash-can-outline delete-icon"></i>
                    </button>
                </div>
            </div>
        </div>
        <div class="add-bar">
            <n-button secondary size="small" @click="openEditor(null)">
                <template #icon>
                    <i class="mdi mdi-plus"></i>
                </template>
                {{ labels.add }}
            </n-button>
        </div>

        <n-modal v-model:show="editorOpen" :mask-closable="false">
            <n-card class="command-editor" role="dialog" aria-modal="true">
                <div class="editor-title">
                    {{ editingIndex == null ? labels.add : labels.edit }}
                </div>
                <div class="editor-body editor-form">
                    <div class="form-label">{{ labels.name }}</div>
                    <n-input v-model:value="draft.name" size="small" />

                    <div class="form-label">{{ labels.scriptMode }}</div>
                    <n-switch v-model:value="draft.isBashScript" />

                    <template v-if="draft.isBashScript">
                        <div class="form-label">
                            <div>{{ labels.scripts }}</div>
                            <div class="form-hint">{{ labels.scriptsHint }}</div>
                        </div>
                        <n-input
                            v-model:value="scriptsText"
                            type="textarea"
                            :autosize="{ minRows: 4 }"
                        />
                    </template>
                    <template v-else>
                        <div class="form-label">{{ labels.command }}</div>
                        <n-input v-model:value="draft.command" size="small" />

                        <div class="form-label">{{ labels.args }}</div>
                        <n-input v-model:value="draft.args" size="small" />
                    </template>

                    <div class="form-label">{{ labels.workingDirectory }}</div>
                    <n-input v-model:value="draft.workingDirectory" size="small" />

                    <div class="form-label">{{ labels.runAsAdmin }}</div>
                    <n-switch v-model:value="draft.runAsAdmin" />
                </div>
                <div class="editor-actions">
                    <n-button size="small" @click="editorOpen = false">
                        {{ labels.cancel }}
                    </n-button>
                    <n-button
                        type="primary"
                        size="small"
                        :disabled="!canSave()"
                        @click="saveEditor"
                    >
                        {{ labels.apply }}
                    </n-button>
                </div>
            </n-card>
        </n-modal>
    </div>
</template>

<style scoped>
.empty {
    padding: 40px;
    text-align: center;
    opacity: 0.6;
}

.command-table {
    width: 100%;
    min-width: 0;
}

.table-head,
.table-row {
    display: flex;
    align-items: center;
    gap: 8px;
}

.table-head {
    padding: 8px 0 10px;
    border-bottom: 1px solid var(--mt-border, #404040);
    font-size: 12px;
    font-weight: 600;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.table-row {
    padding: 10px 0;
    border-bottom: 1px solid var(--mt-border, #404040);
    min-width: 0;
}

.col-name {
    flex: 0 0 180px;
    min-width: 0;
}

.col-command {
    flex: 1 1 auto;
    min-width: 0;
}

.col-command-clickable {
    cursor: pointer;
    border-radius: 8px;
    padding: 4px 6px;
}

.col-command-clickable:hover {
    background: var(--mt-surface-hover, #3a3a3a);
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
    flex: 0 0 64px;
    min-width: 64px;
    display: flex;
    justify-content: center;
    gap: 4px;
}

.icon-delete-btn {
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text-tertiary, #aaaaaa);
    cursor: pointer;
    transition: background-color 140ms ease, color 140ms ease, transform 120ms ease;
}

.icon-delete-btn:hover {
    background: rgba(239, 68, 68, 0.14);
    color: #ef4444;
}

.icon-delete-btn:active {
    transform: scale(0.96);
}

.delete-icon {
    font-size: 16px;
    line-height: 1;
}

.add-bar {
    padding: 16px 0;
}

.command-editor {
    width: min(560px, calc(100vw - 32px));
    background: var(--mt-surface, #292929);
}

.editor-title {
    font-size: 16px;
    font-weight: 600;
    margin-bottom: 12px;
}

.editor-form {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    column-gap: 12px;
    row-gap: 8px;
    align-items: center;
}

.form-label {
    font-size: 13px;
    font-weight: 500;
    color: var(--mt-text, #fff);
    white-space: nowrap;
}

.form-hint {
    margin-top: 2px;
    font-size: 11px;
    font-weight: 400;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.editor-actions {
    margin-top: 14px;
    display: flex;
    justify-content: flex-end;
    gap: 8px;
}
</style>
