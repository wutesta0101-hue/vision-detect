<script setup>
import { onMounted, onUnmounted } from 'vue'
import { useDetectionStore } from './stores/useDetectionStore'
import ConnectionIndicator from './components/ConnectionIndicator.vue'
import UploadPanel from './components/UploadPanel.vue'
import DetectionTable from './components/DetectionTable.vue'

// 儀表板主畫面。
//
// 掛載時做兩件事：載入歷史紀錄、建立 SignalR 連線。
// 卸載時記得斷線 —— 開發階段熱重載會反覆掛載，不斷線會累積連線。

const store = useDetectionStore()

onMounted(async () => {
  await store.loadHistory()
  await store.connect()
})

onUnmounted(() => store.disconnect())
</script>

<template>
  <div class="app">
    <div class="header">
      <div>
        <div class="title">vision-detect</div>
        <div class="subtitle">辨識紀錄儀表板</div>
      </div>
      <ConnectionIndicator :connected="store.connected" />
    </div>

    <UploadPanel />
    <DetectionTable />
  </div>
</template>
