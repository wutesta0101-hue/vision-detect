# 開發環境

本文件說明在本機啟動 vision-detect 的四個元件並驗證運作。

專案架構與設計說明見 [README](../README.md)。

---

## 前置需求

| 工具 | 版本 | 用途 |
|---|---|---|
| Docker Desktop | 最新版 | PostgreSQL 與 pgAdmin |
| .NET SDK | 8.0+ | C# 業務層 |
| Node.js | 20+ | Vue 儀表板 |
| Python | 3.12 | 模型推論服務 |


---

## 啟動

![啟動流程](demo-startup.gif)

四個元件要分別啟動。除了資料庫之外，各開一個終端機視窗。

### 1. 資料庫

```powershell
cd C:\dev\vision-detect
docker compose up -d
docker compose ps
```

等 `vd_postgres` 顯示 **`Up (healthy)`** 再繼續。

> Docker Desktop 關閉或電腦重開後容器不會自動啟動，每次開發前都要執行這一步。
> 忘記的話，C# API 會在啟動時報 `Failed to connect to 127.0.0.1:5432`。

### 2. Python 推論服務（視窗 A）

```powershell
cd model-service
.\.venv\Scripts\python.exe -m uvicorn app.main:app --port 8000
```

看到 `Application startup complete.` 表示就緒。

> 用完整路徑呼叫 `python.exe` 是為了避開系統上多個 Python 版本的干擾。

### 3. C# API（視窗 B）

```powershell
cd backend\src\VisionApi
dotnet run
```

啟動訊息會顯示監聽的 port，例如 `Now listening on: http://localhost:5273`。

日誌出現 **`InferenceWorker 已啟動`** 表示背景工作者正常運作。

### 4. Vue 儀表板（視窗 C）

```powershell
cd frontend
npm run dev
```

打開 <http://localhost:5173>

---

## 各元件位址

| 元件 | 位址 | 說明 |
|---|---|---|
| Vue 儀表板 | <http://localhost:5173> | 主畫面 |
| C# API | `http://localhost:5273` | port 每次可能不同 |
| Swagger | `http://localhost:5273/swagger` | API 互動文件 |
| Python 服務 | <http://localhost:8000/docs> | 推論服務文件 |
| pgAdmin | <http://localhost:8080> | 資料庫管理 |
| PostgreSQL | `localhost:5432` | 資料庫 |

---

## 驗證

![上傳測試](demo-upload.gif)

### 1. 連線狀態

打開儀表板，右上角應顯示**綠點 +「即時連線中」**。

C# 日誌會出現 `客戶端已連線：xxxxx`。

紅點的話，檢查瀏覽器 Console：

| 錯誤 | 原因 |
|---|---|
| CORS 相關 | `appsettings.json` 的 `Cors:Origins` 沒包含 `http://localhost:5173` |
| 連線被拒 | `.env.development` 的 port 與 C# API 不符 |
| 404 | `Program.cs` 缺少 `MapHub` |

### 2. 上傳影像

點「選擇影像」，選一張有人或車的照片。

**預期**：一兩秒後表格自動多一列，閃一下藍色高亮，狀態顯示綠色「完成」。

全程沒有重新整理頁面。

### 3. 即時推播

![即時推播](demo-realtime.gif)

**保持儀表板開著**，在另一個視窗執行：

```powershell
cd model-service
curl.exe -X POST http://localhost:5273/api/v1/detections `
  -F "image=@C:\dev\vision-detect\model-service\test.jpg" `
  -F "deviceId=curl-test"
```

這就是手機拍照時桌機會看到的效果。

