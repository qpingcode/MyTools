<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import VariableInput from './VariableInput.vue';
const { t, tab, authOptions, AuthKind, KeyLocation } = useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="auth-layout">
    <div class="auth-types" role="radiogroup" :aria-label="t.Authentication()">
      <label
        v-for="[kind, label] in authOptions"
        :key="kind"
        class="auth-type"
        :class="{ selected: tab.request.auth.kind === kind }"
      >
        <input type="radio" v-model="tab.request.auth.kind" :value="kind" />
        {{ label }}
      </label>
    </div>
    <div class="auth-fields">
      <p v-if="tab.request.auth.kind === AuthKind.None" class="muted">
        {{ t.AuthNoneHint() }}
      </p>
      <template v-if="tab.request.auth.kind === AuthKind.Basic">
        <label class="auth-field"
          ><span>{{ t.Username() }}</span
          ><VariableInput v-model="tab.request.auth.username"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Password() }}</span
          ><VariableInput v-model="tab.request.auth.password"
        /></label>
      </template>
      <label v-if="tab.request.auth.kind === AuthKind.Bearer" class="auth-field"
        ><span>{{ t.Token() }}</span
        ><VariableInput v-model="tab.request.auth.token"
      /></label>
      <template v-if="tab.request.auth.kind === AuthKind.ApiKey">
        <label class="auth-field"
          ><span>{{ t.Key() }}</span
          ><VariableInput v-model="tab.request.auth.key"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Value() }}</span
          ><VariableInput v-model="tab.request.auth.value"
        /></label>
        <label class="auth-field"
          ><span>{{ t.Location() }}</span
          ><select v-model="tab.request.auth.location">
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
