<script setup lang="ts">
const paths = {
  plus: 'M12 5v14M5 12h14',
  close: 'm6 6 12 12M6 18 18 6',
  trash: 'M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7',
  copy: 'M9 9h12v12H9zM15 9V3H3v12h6',
  edit: 'm16 3 5 5-12 12-6 1 1-6ZM13 6l5 5',
  'arrow-up': 'M12 20V4m-6 6 6-6 6 6',
  'arrow-down': 'M12 4v16m-6-6 6 6 6-6',
  'chevron-down': 'm6 9 6 6 6-6',
  'chevron-right': 'm9 6 6 6-6 6',
  folder: 'M3 5h7l2 3h9v12H3z',
  'folder-plus': 'M3 5h7l2 3h9v12H3zM12 11v6m-3-3h6',
  search: 'M10 3a7 7 0 1 0 0 14 7 7 0 0 0 0-14m5 12 6 6',
  save: 'M3 3h15l3 3v15H3zM7 3v6h10V3M7 21v-8h10v8',
  play: 'm8 4 12 8-12 8z',
  stop: 'M6 6h12v12H6z',
  more: 'M5 12h.01M12 12h.01M19 12h.01',
  globe:
      'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0M3 12h18M12 3c-5 5-5 13 0 18 5-5 5-13 0-18',
  cookie:
      'M12 3a9 9 0 1 0 8.7 11.5 5 5 0 0 1-6.2-6.2A9 9 0 0 0 12 3M8.2 10.2h.01M12.5 13.5h.01M15.2 9.8h.01',
  terminal: 'm5 7 5 5-5 5M13 17h6',
  inbox: 'M3 14 6 4h12l3 10v6H3zM3 14h5l2 3h4l2-3h5',
  file: 'M4 3h10l6 6v12H4zM14 3v6h6',
  download: 'M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4',
  upload: 'M12 16V3m-5 5 5-5 5 5M4 17v4h16v-4',
  history: 'M3 11a9 9 0 1 1 2 7M3 4v7h7M12 7v5l3 2',
} as const;
defineProps<{ name: keyof typeof paths }>();
</script>
<template>
  <svg
      class="icon"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
  >
    <path :d="paths[name]"/>
  </svg>
</template>
