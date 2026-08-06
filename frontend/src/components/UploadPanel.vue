<script setup>
import { ref } from 'vue'
import { useDetectionStore } from '../stores/useDetectionStore'

// 上傳面板。
//
// 這是給桌機測試用的 —— 正式流程是手機拍照上傳。
// 有了它就不用開終端機打 curl，也方便展示即時推播的效果。

const store = useDetectionStore()
const fileInput = ref(null)

function onPick(event) {
  const file = event.target.files?.[0]
  if (file) store.upload(file)
  event.target.value = ''   // 清空才能重複選同一個檔案
}
</script>

<template>
  <div class="card">
    <div class="card-title">上傳測試</div>
    <div class="row">
      <button class="btn" :disabled="store.uploading" @click="fileInput.click()">
        {{ store.uploading ? '上傳中…' : '選擇影像' }}
      </button>
      <span class="dim">上傳後結果會自動出現在下方，不需重新整理</span>
    </div>
    <input
      ref="fileInput"
      type="file"
      accept="image/*"
      style="display: none"
      @change="onPick"
    />
    <div v-if="store.error" class="error">{{ store.error }}</div>
  </div>
</template>

<style scoped>
.row { display: flex; align-items: center; gap: 14px; }
.error { color: var(--failed); font-size: 13px; margin-top: 10px; }
</style>
