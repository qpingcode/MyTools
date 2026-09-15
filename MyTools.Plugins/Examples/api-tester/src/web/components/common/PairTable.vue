<script setup lang="ts">
import {computed, ref, watch} from 'vue';
import IconButton from './IconButton.vue';
import VariableInput from './VariableInput.vue';
import type {Pair} from '../../../shared/model.js';
import {Routes} from '../../../shared/model.js';
import {
  CommonRequestHeaderNames,
  headerValueSuggestions,
} from '../../features/request/headerSuggestions.js';
import {useText} from '../../localization/locale.js';
import {rpc} from '../../services/rpc.js';

const props = defineProps<{
  values: Pair[];
  query?: boolean;
  multipart?: boolean;
  headers?: boolean;
}>();
const emit = defineEmits<{ changed: [] }>();
const t = useText();
const emptyPair = (): Pair => ({name: '', value: '', enabled: true});
const draft = ref<Pair>(emptyPair());
const rows = computed(() => [...props.values, draft.value]);
const rowIds = new WeakMap<Pair, string>();

function rowId(pair: Pair) {
  let id = rowIds.get(pair);
  if (!id) {
    id = crypto.randomUUID();
    rowIds.set(pair, id);
  }
  return id;
}

// Switching request or body field arrays must not carry placeholder settings across.
watch(
    () => props.values,
    () => {
      draft.value = emptyPair();
    },
);

function changed(pair: Pair) {
  if (pair === draft.value) {
    if (!pair.name && !pair.value && !pair.file && !pair.contentType) return;
    props.values.push(pair);
    draft.value = emptyPair();
  }
  emit('changed');
}

async function pick(pair: Pair) {
  const file = await rpc<{
    path: string;
    size: number;
    contentType: string;
  } | null>(Routes.file);
  if (file) {
    pair.file = file.path;
    pair.size = file.size;
    pair.contentType = file.contentType;
    changed(pair);
  }
}

function remove(index: number) {
  if (index === props.values.length) {
    draft.value = emptyPair();
    return;
  }
  props.values.splice(index, 1);
  emit('changed');
}

</script>
<template>
  <div class="pair-table">
    <div class="pair-header" :class="{ extra: query || multipart }">
      <span></span><span>{{ t.Key() }}</span
    ><span>{{ t.Value() }}</span
    ><span v-if="query || multipart"></span><span></span>
    </div>
    <div v-for="(pair, index) in rows" :key="rowId(pair)">
      <div class="pair" :class="{ extra: query || multipart }">
        <input
            type="checkbox"
            v-model="pair.enabled"
            :aria-label="t.Enabled()"
            @change="changed(pair)"
        />
        <VariableInput
            v-model="pair.name"
            :aria-label="t.Key()"
            :suggestions="headers ? CommonRequestHeaderNames : undefined"
            @input="changed(pair)"
        />
        <input
            v-if="pair.file"
            v-model="pair.file"
            :aria-label="t.File()"
            @input="changed(pair)"
        />
        <VariableInput
            v-else
            v-model="pair.value"
            :aria-label="t.Value()"
            :suggestions="headers ? headerValueSuggestions(pair.name) : undefined"
            @input="changed(pair)"
        />
        <label v-if="query" class="check"
        ><input
            type="checkbox"
            v-model="pair.noEquals"
            @change="changed(pair)"
        />{{ t.NoEquals() }}</label
        >
        <IconButton
            v-if="multipart"
            icon="file"
            :label="t.SelectFile()"
            @click="pick(pair)"
        />
        <IconButton v-if="pair !== draft" icon="trash" :label="t.Delete()" @click="remove(index)"/>
      </div>
      <div v-if="multipart && pair.file" class="row">
        <span class="muted">{{
            t.FileInfo({
              name: pair.file,
              size: pair.size ?? '—',
            })
          }}</span>
        <button
            @click="
            delete pair.file;
            delete pair.size;
            delete pair.contentType;
            changed(pair);
          "
        >
          {{ t.Text() }}
        </button>
      </div>
    </div>
  </div>
</template>
