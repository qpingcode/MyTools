<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
import PairTable from './PairTable.vue';
import VariableInput from './VariableInput.vue';
const { t, tab, BodyKind, bodyOptions, formatBody, pickBinary } =
  useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="config-content">
    <div class="row">
      <select v-model="tab.request.body.kind" :aria-label="t.Body()">
        <option v-for="[kind, label] in bodyOptions" :key="kind" :value="kind">
          {{ label }}
        </option></select
      ><button
        v-if="tab.request.body.kind === BodyKind.Json"
        @click="formatBody(tab)"
      >
        {{ t.Format() }}
      </button>
    </div>
    <template
      v-if="[BodyKind.Json, BodyKind.Text].includes(tab.request.body.kind)"
      ><label class="field"
        >{{ t.ContentType()
        }}<input v-model="tab.request.body.contentType" /></label
      ><VariableInput multiline
        class="code-input"
        v-model="tab.request.body.text"
        :aria-label="t.Body()"
        spellcheck="false"
      /></template
    ><PairTable
      v-if="[BodyKind.Form, BodyKind.Multipart].includes(tab.request.body.kind)"
      :values="tab.request.body.fields"
      :multipart="tab.request.body.kind === BodyKind.Multipart"
    />
    <div v-if="tab.request.body.kind === BodyKind.Binary" class="row">
      <label class="field grow"
        >{{ t.File() }}<input v-model="tab.request.body.file" /></label
      ><IconButton
        icon="file"
        :label="t.SelectFile()"
        @click="pickBinary(tab)"
      /><label class="field"
        >{{ t.ContentType()
        }}<input v-model="tab.request.body.contentType" /></label
      ><span class="muted">{{
        t.FileInfo({
          name: tab.request.body.file,
          size: tab.request.body.fileSize ?? '—',
          type: tab.request.body.contentType || '—',
        })
      }}</span>
    </div>
  </div>
</template>
