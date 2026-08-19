<script setup lang="ts">
import { computed, ref } from "vue";
import { bus } from "../bus";
import { t } from "../i18n";
import { normalizePathKind, type PathKind } from "../setting-utils";

const props = defineProps<{
    modelValue: unknown;
    kind?: string | null;
    title?: string | null;
}>();

const emit = defineEmits<{
    "update:modelValue": [value: string];
}>();

type ValidatePathResult = { valid?: boolean; message?: string };
type PickPathResult = { cancelled?: boolean; path?: string };

const error = ref("");
const pathKind = computed((): PathKind => normalizePathKind(props.kind));
const textValue = computed(() => (props.modelValue == null ? "" : String(props.modelValue)));

function emitText(value: string): void {
    error.value = "";
    emit("update:modelValue", value);
}

async function validate(value: string): Promise<boolean> {
    const result = await bus.call<ValidatePathResult>("validatePath", {
        path: value || "",
        kind: pathKind.value,
    });
    if (!result?.valid) {
        error.value = result?.message || t("Plugin.Settings.Path.Invalid", "Invalid path");
        return false;
    }
    error.value = "";
    return true;
}

async function commit(): Promise<void> {
    await validate(textValue.value);
}

async function browse(): Promise<void> {
    const result = await bus.call<PickPathResult>("pickPath", {
        title: props.title || (pathKind.value === "directory" ? "Select folder" : "Select file"),
        kind: pathKind.value,
        filter: pathKind.value === "directory"
            ? undefined
            : "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
        initialPath: textValue.value,
    });
    if (!result || result.cancelled || !result.path) {
        return;
    }
    emit("update:modelValue", result.path);
    await validate(result.path);
}
</script>

<template>
    <div class="path-field">
        <div class="path-control">
            <n-input
                :value="textValue"
                size="small"
                class="control-input"
                @update:value="emitText(String($event || ''))"
                @blur="commit"
            />
            <n-button size="small" secondary class="browse-btn" @click="browse">
                <template #icon>
                    <i class="mdi" :class="pathKind === 'file' ? 'mdi-file-outline' : 'mdi-folder-open-outline'"></i>
                </template>
            </n-button>
        </div>
        <div v-if="error" class="path-error">{{ error }}</div>
    </div>
</template>

<style scoped>
.path-field {
    width: 100%;
    min-width: 0;
}

.path-control {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 6px;
}

.control-input {
    width: 100%;
    min-width: 0;
}

.browse-btn {
    flex: 0 0 auto;
}

.path-error {
    margin-top: 6px;
    color: #f44336;
    font-size: 12px;
}
</style>
