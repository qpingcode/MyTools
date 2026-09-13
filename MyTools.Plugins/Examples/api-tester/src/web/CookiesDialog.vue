<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { useWorkspaceContext } from './context.js';
const { t, cookies, clearCookies } = useWorkspaceContext();
const open = defineModel<boolean>('open', { required: true });
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
    class="cookies-dialog"
    :aria-label="t.Cookies()"
    @cancel.prevent="close"
  >
    <h2>{{ t.Cookies() }}</h2>
    <p class="muted">{{ t.CookiesHint() }}</p>
    <p v-if="!cookies.length" class="muted">{{ t.CookiesEmpty() }}</p>
    <section
      v-for="[domain, items] in groups"
      :key="domain"
      class="cookie-domain"
    >
      <h3>{{ domain }}</h3>
      <table>
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
            <td>{{ cookie.value }}</td>
            <td>{{ cookie.path }}</td>
            <td>{{ expiresText(cookie.expires) }}</td>
            <td>{{ cookie.httpOnly ? t.CookieYes() : t.CookieNo() }}</td>
            <td>{{ cookie.secure ? t.CookieYes() : t.CookieNo() }}</td>
          </tr>
        </tbody>
      </table>
    </section>
    <div class="row">
      <button :disabled="!cookies.length" @click="clearCookies">
        {{ t.ClearCookies() }}
      </button>
      <button @click="close">{{ t.Close() }}</button>
    </div>
  </dialog>
</template>
