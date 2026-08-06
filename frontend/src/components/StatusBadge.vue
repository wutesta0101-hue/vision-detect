<script setup>
// 作業狀態的色塊標籤。
// 四種狀態各有顏色，讓表格一眼看得出哪些還在跑、哪些失敗了。

defineProps({ status: String })

const COLORS = {
  Pending: 'var(--pending)',
  Processing: 'var(--processing)',
  Done: 'var(--done)',
  Failed: 'var(--failed)',
}

const LABELS = {
  Pending: '等待中',
  Processing: '處理中',
  Done: '完成',
  Failed: '失敗',
}
</script>

<template>
  <span class="badge" :style="{ background: COLORS[status] ?? 'var(--pending)' }">
    {{ LABELS[status] ?? status }}
  </span>
</template>

<style scoped>
.badge {
  display: inline-block;
  color: #fff;
  font-size: 11px;
  font-weight: 500;
  padding: 3px 9px;
  border-radius: 5px;
  white-space: nowrap;
}
</style>
