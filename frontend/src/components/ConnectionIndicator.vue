<script setup>
// SignalR 連線狀態指示。
//
// 為什麼需要：即時推播斷線時畫面看起來完全正常，只是不再更新 —— 
// 使用者會以為系統沒事，其實資料已經停止流入。
// 這個指示器讓「沒有更新」和「連線中斷」能被區分開。

defineProps({ connected: Boolean })
</script>

<template>
  <div class="indicator">
    <span class="dot" :class="{ on: connected }"></span>
    {{ connected ? '即時連線中' : '連線中斷' }}
  </div>
</template>

<style scoped>
.indicator {
  display: flex;
  align-items: center;
  gap: 7px;
  font-size: 12px;
  color: var(--text-dim);
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--failed);
}

.dot.on {
  background: var(--done);
  /* 呼吸效果，表示連線活著 */
  animation: pulse 2s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50%      { opacity: 0.35; }
}
</style>
