<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import VariableInput from './VariableInput.vue';
const { t, tab, authOptions, AuthKind, KeyLocation } = useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="config-content">
    <select v-model="tab.request.auth.kind" :aria-label="t.Authentication()">
      <option v-for="[kind, label] in authOptions" :key="kind" :value="kind">
        {{ label }}
      </option>
    </select>
    <div v-if="tab.request.auth.kind === AuthKind.Basic" class="row">
      <label class="field"
        >{{ t.Username() }}<VariableInput v-model="tab.request.auth.username" /></label
      ><label class="field"
        >{{ t.Password() }}<VariableInput v-model="tab.request.auth.password"
      /></label>
    </div>
    <label v-if="tab.request.auth.kind === AuthKind.Bearer" class="field"
      >{{ t.Token() }}<VariableInput v-model="tab.request.auth.token"
    /></label>
    <div v-if="tab.request.auth.kind === AuthKind.ApiKey" class="row">
      <label class="field"
        >{{ t.Key() }}<VariableInput v-model="tab.request.auth.key" /></label
      ><label class="field"
        >{{ t.Value() }}<VariableInput v-model="tab.request.auth.value" /></label
      ><select v-model="tab.request.auth.location" :aria-label="t.Location()">
        <option :value="KeyLocation.Header">{{ t.HeaderLocation() }}</option>
        <option :value="KeyLocation.Query">{{ t.QueryLocation() }}</option>
      </select>
    </div>
  </div>
</template>
