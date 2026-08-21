<script setup lang="ts">
import { computed } from "vue";
import { t } from "../i18n";
import { defaultUiHint, isPathType, normalizePathKind } from "../setting-utils";
import PathField from "./PathField.vue";

const props = defineProps<{
    type: string;
    uiHint?: string | null;
    title?: string | null;
    modelValue: unknown;
}>();

const emit = defineEmits<{
    "update:modelValue": [value: unknown];
}>();

const hint = computed(() => defaultUiHint(props.type, props.uiHint));
const showPath = computed(() => isPathType(props.type) || hint.value === "file" || hint.value === "directory" || hint.value === "fileordirectory");
const pathKind = computed(() => normalizePathKind(hint.value));
const inputType = computed(() => {
    if (hint.value === "email") return "email";
    if (hint.value === "telephone" || hint.value === "tel") return "tel";
    return "text";
});
const textValue = computed(() => (props.modelValue == null ? "" : String(props.modelValue)));
const numberValue = computed(() => {
    const value = typeof props.modelValue === "number" ? props.modelValue : Number(props.modelValue);
    return Number.isFinite(value) ? value : null;
});
const boolValue = computed(() => props.modelValue === true || props.modelValue === "True" || props.modelValue === "true");
const boolOptions = computed(() => [
    { label: t("Plugin.Settings.Table.Yes", "Yes"), value: true },
    { label: t("Plugin.Settings.Table.No", "No"), value: false },
]);

function emitText(value: string | null): void {
    emit("update:modelValue", value ?? "");
}

function emitNumber(value: number | null): void {
    emit("update:modelValue", value);
}

function emitBool(value: boolean): void {
    emit("update:modelValue", !!value);
}
</script>

<template>
    <PathField
        v-if="showPath"
        :model-value="textValue"
        :kind="pathKind"
        :title="title"
        @update:model-value="emitText($event)"
    />
    <n-input
        v-else-if="hint === 'textarea'"
        :value="textValue"
        type="textarea"
        size="small"
        :autosize="{ minRows: 3, maxRows: 8 }"
        @update:value="emitText($event)"
    />
    <n-input-number
        v-else-if="hint === 'input-number'"
        :value="numberValue"
        :placeholder="t('Plugin.Settings.PleaseInput', 'Please input')"
        size="small"
        class="field-stretch"
        @update:value="emitNumber($event)"
    />
    <n-checkbox
        v-else-if="hint === 'checkbox'"
        :checked="boolValue"
        @update:checked="emitBool($event)"
    />
    <n-radio-group
        v-else-if="hint === 'radio'"
        :value="boolValue"
        @update:value="emitBool($event as boolean)"
    >
        <n-radio :value="true">{{ boolOptions[0].label }}</n-radio>
        <n-radio :value="false">{{ boolOptions[1].label }}</n-radio>
    </n-radio-group>
    <n-select
        v-else-if="hint === 'select' && (type === 'bool' || type === 'Bool')"
        :value="boolValue"
        :options="boolOptions"
        size="small"
        @update:value="emitBool($event as boolean)"
    />
    <n-switch
        v-else-if="type === 'Bool' || type === 'bool'"
        :value="boolValue"
        @update:value="emitBool(!!$event)"
    />
    <n-input
        v-else
        :value="textValue"
        :type="inputType"
        size="small"
        @update:value="emitText($event)"
    />
</template>

<style scoped>
.field-stretch {
    width: 100%;
}
</style>
