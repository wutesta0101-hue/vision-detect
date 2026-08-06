import { defineStore } from 'pinia'
import { ref } from 'vue'
import { fetchDetections, uploadImage } from '../api/apiClient'
import { createHubConnection } from '../api/hubClient'

// 辨識紀錄的全域狀態。
//
// 資料有兩個來源：
//   1. 初次載入時用 HTTP 撈歷史
//   2. 之後靠 SignalR 推播即時更新
//
// 兩者可能重疊（推播的紀錄也在歷史裡），所以 upsert 時要依 id 去重。
export const useDetectionStore = defineStore('detection', () => {
  const records = ref([])           // 辨識紀錄，新到舊
  const connected = ref(false)      // hub 連線狀態，顯示在右上角
  const loading = ref(false)
  const uploading = ref(false)
  const error = ref(null)

  let connection = null

  // 插入或更新一筆紀錄。
  // 已存在就取代（狀態從 Processing 變成 Done），不存在就插到最前面。
  function upsert(record) {
    const index = records.value.findIndex(r => r.id === record.id)
    if (index >= 0) {
      records.value[index] = { ...record, isNew: records.value[index].isNew }
    } else {
      records.value.unshift({ ...record, isNew: true })
      // 1.6 秒後移除高亮標記，與 CSS 動畫長度一致
      setTimeout(() => {
        const target = records.value.find(r => r.id === record.id)
        if (target) target.isNew = false
      }, 1600)
    }
  }

  // 載入歷史紀錄
  async function loadHistory() {
    loading.value = true
    error.value = null
    try {
      const data = await fetchDetections()
      records.value = data.map(r => ({ ...r, isNew: false }))
    } catch (e) {
      error.value = `載入失敗：${e.message}`
    } finally {
      loading.value = false
    }
  }

  // 建立 SignalR 連線並訂閱事件
  async function connect() {
    if (connection) return

    connection = createHubConnection()

    connection.on('DetectionCompleted', upsert)
    connection.on('DetectionFailed', upsert)

    connection.onreconnecting(() => { connected.value = false })

    // 重連期間的推播會遺失，所以重連成功後重新載入一次，
    // 避免表格漏掉中斷期間發生的紀錄。
    connection.onreconnected(async () => {
      connected.value = true
      await loadHistory()
    })

    connection.onclose(() => { connected.value = false })

    try {
      await connection.start()
      connected.value = true
    } catch (e) {
      connected.value = false
      error.value = `即時連線失敗：${e.message}`
    }
  }

  // 斷線。應用卸載時呼叫，避免留下沒關閉的連線。
  async function disconnect() {
    if (!connection) return
    await connection.stop()
    connection = null
    connected.value = false
  }

  // 上傳影像。
  // 成功後不用手動加入列表 —— 推論完成時 SignalR 會推過來。
  async function upload(file) {
    uploading.value = true
    error.value = null
    try {
      await uploadImage(file)
    } catch (e) {
      error.value = `上傳失敗：${e.message}`
    } finally {
      uploading.value = false
    }
  }

  return {
    records, connected, loading, uploading, error,
    loadHistory, connect, disconnect, upload,
  }
})
