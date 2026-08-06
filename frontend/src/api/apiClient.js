import axios from 'axios'

// 後端 API 客戶端。
//
// 開發時 API 在別的 port，用 VITE_API_BASE 指定。
// 部署後前後端在同一個 Nginx 後面，改成空字串走相對路徑即可。
const client = axios.create({
  baseURL: import.meta.env.VITE_API_BASE ?? 'http://localhost:5273',
  timeout: 15000,
})

// 取得歷史紀錄（新到舊）
export async function fetchDetections(skip = 0, take = 50) {
  const { data } = await client.get('/api/v1/detections', { params: { skip, take } })
  return data
}

// 查詢單筆。SignalR 推播不可用時的備援。
export async function fetchDetection(jobId) {
  const { data } = await client.get(`/api/v1/detections/${jobId}`)
  return data
}

// 上傳影像。回傳 { jobId, status }。
//
// requestId 由前端產生，用於去重 —— 網路不穩導致重送時，
// 伺服器靠這個鍵確保只建立一筆紀錄。
export async function uploadImage(file, deviceId = 'browser') {
  const form = new FormData()
  form.append('image', file)
  form.append('requestId', crypto.randomUUID())
  form.append('deviceId', deviceId)
  form.append('capturedAt', new Date().toISOString())

  const { data } = await client.post('/api/v1/detections', form)
  return data
}
