<script setup lang="ts">
const model = defineModel<string>({ default: "" });

defineProps<{
    placeholder: string;
}>();
</script>

<template>
    <div class="table-toolbar">
        <div class="table-search">
            <n-input
                v-model:value="model"
                size="small"
                clearable
                :placeholder="placeholder"
            >
                <template #prefix>
                    <i class="mdi mdi-magnify"></i>
                </template>
            </n-input>
        </div>
        <slot />
    </div>
</template>

<style scoped>
.table-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-start;
    gap: 8px;
    margin-bottom: 10px;
    min-width: 0;
}

.table-search {
    flex: 0 0 50%;
    width: 50%;
    min-width: 0;
    box-sizing: border-box;
}

.table-search :deep(.n-input) {
    width: 100%;
}
</style>
