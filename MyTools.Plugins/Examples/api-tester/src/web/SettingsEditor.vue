<script setup lang="ts">
import type { Settings } from '../shared/model.js';
import { useText } from './locale.js';
defineProps<{ value: Settings }>();
const t = useText();
</script>
<template>
  <div class="settings-fields">
    <label class="field"
      >{{ t.Timeout()
      }}<input type="number" v-model.number="value.timeoutMs" /></label
    ><label class="check"
      ><input type="checkbox" v-model="value.followRedirects" />{{
        t.Follow()
      }}</label
    ><label class="check"
      ><input type="checkbox" v-model="value.verifyTls" />{{
        t.VerifyTls()
      }}</label
    >
  </div>
</template>
