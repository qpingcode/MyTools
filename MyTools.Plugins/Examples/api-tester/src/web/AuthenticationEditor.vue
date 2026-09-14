<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import VariableInput from './VariableInput.vue';
import { computed } from 'vue';
import type { ApiRequest } from '../shared/model.js';
const props = defineProps<{ auth?: ApiRequest['auth']; collection?: boolean }>();
const { t, tab, authOptions, AuthKind, KeyLocation } = useWorkspaceContext();
const authentication = computed(() => props.auth || tab.value?.request.auth);
const options = computed(() => authOptions.value.filter(([kind]) => !props.collection || kind !== AuthKind.Inherit));
</script>
<template>
  <div v-if="authentication" class="auth-layout">
    <div class="auth-types" role="radiogroup" :aria-label="t.Authentication()">
      <label
        v-for="[kind, label] in options"
        :key="kind"
        class="auth-type"
        :class="{ selected: authentication.kind === kind }"
      >
        <input type="radio" v-model="authentication.kind" :value="kind" />
        {{ label }}
      </label>
    </div>
    <div class="auth-fields">
      <p v-if="authentication.kind === AuthKind.Inherit" class="muted">{{ t.InheritAuthHint() }}</p>
      <p v-if="authentication.kind === AuthKind.None" class="muted">
        {{ t.AuthNoneHint() }}
      </p>
      <template v-if="authentication.kind === AuthKind.Basic">
        <label class="auth-field"
          ><span>{{ t.Username() }}</span
          ><VariableInput v-model="authentication.username"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Password() }}</span
          ><VariableInput v-model="authentication.password"
        /></label>
      </template>
      <label v-if="authentication.kind === AuthKind.Bearer" class="auth-field"
        ><span>{{ t.Token() }}</span
        ><VariableInput v-model="authentication.token"
      /></label>
      <template v-if="authentication.kind === AuthKind.ApiKey">
        <label class="auth-field"
          ><span>{{ t.Key() }}</span
          ><VariableInput v-model="authentication.key"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Value() }}</span
          ><VariableInput v-model="authentication.value"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Location() }}</span
          ><select v-model="authentication.location">
            <option :value="KeyLocation.Header">
              {{ t.HeaderLocation() }}
            </option>
            <option :value="KeyLocation.Query">
              {{ t.QueryLocation() }}
            </option>
          </select></label
        >
      </template>
    </div>
  </div>
</template>
