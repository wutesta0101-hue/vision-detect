<script setup>
import { useDetectionStore } from '../stores/useDetectionStore'
import StatusBadge from './StatusBadge.vue'

// 辨識紀錄表格。
//
// 新紀錄由 SignalR 推播插入最前面，並帶 1.6 秒的高亮動畫，
// 讓「即時更新」這件事在畫面上看得見。

const store = useDetectionStore()

// 顯示當地時間。後端存的是 UTC，帶 Z 的字串會自動轉換。
function formatTime(iso) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString('zh-TW', { hour12: false })
}

// 把偵測框依類別彙整成「person ×3」的形式，
// 直接列出十幾個框在表格裡會太雜。
function summarize(detections) {
  const counts = {}
  for (const d of detections) counts[d.label] = (counts[d.label] ?? 0) + 1
  return Object.entries(counts).map(([label, count]) => ({ label, count }))
}
</script>

<template>
  <div class="card">
    <div class="card-title">辨識紀錄（{{ store.records.length }}）</div>

    <div v-if="store.loading" class="empty">載入中…</div>
    <div v-else-if="store.records.length === 0" class="empty">
      尚無紀錄。上傳一張影像試試。
    </div>

    <table v-else>
      <thead>
        <tr>
          <th style="width: 150px">時間</th>
          <th style="width: 90px">狀態</th>
          <th style="width: 110px">裝置</th>
          <th>辨識結果</th>
          <th style="width: 80px">耗時</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in store.records" :key="r.id" :class="{ 'row-new': r.isNew }">
          <td class="mono">{{ formatTime(r.receivedAt) }}</td>
          <td><StatusBadge :status="r.status" /></td>
          <td>{{ r.deviceId }}</td>
          <td>
            <template v-if="r.status === 'Failed'">
              <span class="failed">{{ r.failureReason }}</span>
            </template>
            <template v-else-if="r.detections.length">
              <span v-for="item in summarize(r.detections)" :key="item.label" class="chip">
                {{ item.label }} ×{{ item.count }}
              </span>
            </template>
            <span v-else class="dim">—</span>
          </td>
          <td class="mono dim">{{ r.inferenceMs ? r.inferenceMs + ' ms' : '—' }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.failed { color: var(--failed); font-size: 12px; }
</style>
