<script setup lang="ts">
import {computed, ref} from 'vue';
import {ScriptPhase, type RequestScripts} from '../../../shared/model.js';
import {useText} from '../../localization/locale.js';

const props = defineProps<{ modelValue?: RequestScripts }>();
const emit = defineEmits<{ 'update:modelValue': [value: RequestScripts] }>();
const t = useText();
const phase = ref(ScriptPhase.Before);
const scripts = computed(() => props.modelValue || {enabled: true, before: '', after: ''});

function update(key: keyof RequestScripts, value: string | boolean) {
  emit('update:modelValue', {...scripts.value, [key]: value});
}
</script>
<template>
  <div class="config-content scripts-editor">
    <label class="check"><input type="checkbox" :checked="scripts.enabled"
                                @change="update('enabled', ($event.target as HTMLInputElement).checked)"/>{{
        t.EnableScripts()
      }}</label>
    <div class="body-toolbar">
      <button :class="{ selected: phase === ScriptPhase.Before }" @click="phase = ScriptPhase.Before">
        {{ t.BeforeScript() }}
      </button>
      <button :class="{ selected: phase === ScriptPhase.After }" @click="phase = ScriptPhase.After">{{
          t.AfterScript()
        }}
      </button>
    </div>
    <textarea class="script-code" :value="scripts[phase]"
              :aria-label="phase === ScriptPhase.Before ? t.BeforeScript() : t.AfterScript()" spellcheck="false"
              @input="update(phase, ($event.target as HTMLTextAreaElement).value)"/>
    <p class="muted">{{ t.ScriptsHint() }}</p>
    <details>
      <summary>{{ t.ScriptExamples() }}</summary>
      <pre class="response-code">pm.variables.set('timestamp', String(Date.now()));
pm.request.headers.upsert({ key: 'X-Time', value: pm.variables.get('timestamp') });
console.log(pm.request.method, pm.request.url);

// {{ t.AfterScript() }}
pm.test('status', () =&gt; pm.response.to.have.status(200));
pm.test('payload', () =&gt; pm.expect(pm.response.json()).to.have.property('id'));
pm.variables.set('token', pm.response.json().token);</pre>
    </details>
  </div>
</template>
