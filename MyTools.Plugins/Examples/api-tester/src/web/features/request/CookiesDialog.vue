<script setup lang="ts">
import {computed, nextTick, ref, watch} from 'vue';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';

const {t, cookies, clearCookies} = useWorkspaceContext();
const open = defineModel<boolean>('open', {required: true});
const dialog = ref<HTMLDialogElement | null>(null);
const groups = computed(() => {
  const map = new Map<string, typeof cookies.value>();
  for (const cookie of cookies.value) {
    const list = map.get(cookie.domain) || [];
    list.push(cookie);
    map.set(cookie.domain, list);
  }
  return [...map.entries()].sort(([left], [right]) => left.localeCompare(right));
});

function expiresText(value: string) {
  return value ? new Date(value).toLocaleString() : t.value.CookieSession();
}

watch(open, async (value) => {
  if (!value) return;
  await nextTick();
  dialog.value?.showModal();
});

function close() {
  dialog.value?.close();
  open.value = false;
}
</script>
<template>
  <dialog
      v-if="open"
      ref="dialog"
      class="standard-dialog cookies-dialog"
      :aria-label="t.Cookies()"
      @cancel.prevent="close"
  >
    <div class="dialog-titlebar"><h2>{{ t.Cookies() }}</h2>
      <IconButton icon="close" :label="t.Close()" @click="close"/>
    </div>
    <div class="dialog-content cookies-content">
      <p class="muted">{{ t.CookiesHint() }}</p>
      <p v-if="!cookies.length" class="muted">{{ t.CookiesEmpty() }}</p>
      <section
          v-for="[domain, items] in groups"
          :key="domain"
          class="cookie-domain"
      >
        <h3>{{ domain }}</h3>
        <div class="cookies-table-scroll">
          <table>
            <colgroup>
              <col class="cookie-name-column"/>
              <col class="cookie-value-column"/>
              <col class="cookie-path-column"/>
              <col class="cookie-expires-column"/>
              <col class="cookie-flag-column"/>
              <col class="cookie-flag-column"/>
            </colgroup>
            <thead>
            <tr>
              <th>{{ t.Key() }}</th>
              <th>{{ t.Value() }}</th>
              <th>{{ t.CookiePath() }}</th>
              <th>{{ t.CookieExpires() }}</th>
              <th>{{ t.CookieHttpOnly() }}</th>
              <th>{{ t.CookieSecure() }}</th>
            </tr>
            </thead>
            <tbody>
            <tr
                v-for="cookie in items"
                :key="cookie.domain + cookie.path + cookie.name"
            >
              <td>{{ cookie.name }}</td>
              <td>
                <div class="cookie-value" :title="cookie.value">{{ cookie.value }}</div>
              </td>
              <td>{{ cookie.path }}</td>
              <td>{{ expiresText(cookie.expires) }}</td>
              <td>{{ cookie.httpOnly ? t.CookieYes() : t.CookieNo() }}</td>
              <td>{{ cookie.secure ? t.CookieYes() : t.CookieNo() }}</td>
            </tr>
            </tbody>
          </table>
        </div>
      </section>
    </div>
    <div class="dialog-actions">
      <button :disabled="!cookies.length" @click="clearCookies">
        {{ t.ClearCookies() }}
      </button>
      <button class="primary" @click="close">{{ t.Close() }}</button>
    </div>
  </dialog>
</template>
