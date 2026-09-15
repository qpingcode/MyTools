<script setup lang="ts">
import Icon from './Icon.vue';

defineProps<{
  icon: InstanceType<typeof Icon>['$props']['name'];
  label: string;
  disabled?: boolean;
}>();
</script>
<template>
  <button
    type="button"
    class="icon-button"
    :title="label"
    :aria-label="label"
    :disabled="disabled"
  >
    <Icon :name="icon" />
  </button>
</template>
