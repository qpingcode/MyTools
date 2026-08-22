<script setup lang="ts">
import { computed } from "vue";
import { t } from "../i18n";
import { captureInputAction } from "../capture-input-action";

const props = defineProps<{
    modelValue: string | null | undefined;
    defaultHotKey?: string;
    excludePluginId?: string;
    excludeSearchHotKey?: boolean;
    currentSearchHotKey?: string;
}>();

const emit = defineEmits<{
    "update:modelValue": [value: string | null];
}>();

const label = computed(() => props.modelValue || t("Plugin.Settings.Keymap.NoHotkey", "None"));
const canReset = computed(() => props.defaultHotKey !== undefined
    && (props.modelValue || "") !== props.defaultHotKey);

async function record(): Promise<void> {
    const result = await captureInputAction({
        showKeyboard: true,
        showMouse: false,
        value: { kind: "hotkey", hotKey: props.modelValue || null },
        defaultHotKey: props.defaultHotKey ?? "",
        excludePluginId: props.excludePluginId,
        excludeSearchHotKey: props.excludeSearchHotKey,
        currentSearchHotKey: props.currentSearchHotKey,
    });
    if (!result) return;
    emit("update:modelValue", result.hotKey || null);
}

function reset(): void {
    emit("update:modelValue", props.defaultHotKey ?? null);
}
</script>

<template>
    <div class="hotkey-recorder">
        <n-button size="small" secondary class="hotkey-btn" @click="record">
            {{ label }}
        </n-button>
        <n-button
            v-if="defaultHotKey !== undefined"
            size="small"
            quaternary
            circle
            class="reset-btn"
            :disabled="!canReset"
            :title="t('Plugin.Settings.ActionPicker.Reset', 'Reset to default')"
            @click="reset"
        >
            <i class="mdi mdi-refresh" aria-hidden="true"></i>
        </n-button>
    </div>
</template>

<style scoped>
.hotkey-recorder {
    display: flex;
    align-items: center;
    gap: 4px;
    min-width: 0;
    width: 100%;
}

.hotkey-btn {
    min-width: 0;
    flex: 1 1 auto;
    justify-content: flex-start;
    --n-height: 34px;
    --n-border-radius: 14px;
    --n-padding: 0 12px;
    text-align: left;
    overflow: hidden;
}

.hotkey-btn :deep(.n-button__border),
.hotkey-btn :deep(.n-button__state-border) {
    border-radius: 14px !important;
}
</style>
