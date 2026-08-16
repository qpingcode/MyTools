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

function clear(): void {
    emit("update:modelValue", null);
}
</script>

<template>
    <div class="hotkey-recorder">
        <v-btn size="small" variant="flat" class="hotkey-btn" @click="record">
            {{ label }}
        </v-btn>
        <v-btn
            icon="mdi-close"
            size="x-small"
            variant="text"
            :title="t('Plugin.Settings.Keymap.ClearHotkey', 'Clear hotkey')"
            @click="clear"
        />
    </div>
</template>

<style scoped>
.hotkey-recorder {
    display: flex;
    align-items: center;
    gap: 2px;
    min-width: 0;
    width: 100%;
}

.hotkey-btn {
    min-width: 0;
    flex: 1 1 auto;
    padding: 0 8px;
    background: var(--mt-surface, #292929) !important;
    color: var(--mt-text, #fff) !important;
}
</style>
