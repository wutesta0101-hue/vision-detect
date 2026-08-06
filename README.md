*[English](README.md)*

# vision-detect

> 分散式物件辨識系統 —— 手機拍攝、Python 推論、C# 編排、桌機即時檢視。

**狀態：第一階段開發中。** 標示「規劃中」的章節尚未實作。

![系統架構](docs/architecture\(zh\).png)

---

## 專案目錄

- [專案目的](#專案目的)
- [期望學習的技能](#期望學習的技能)
- [開發階段](#開發階段)
- [系統架構](#系統架構)
- [部署拓樸](#部署拓樸)
- [API 契約](#api-契約)
- [技術組成](#技術組成)
- [測試策略](#測試策略)
- [專案結構](#專案結構)
- [已知限制](#已知限制)
- [授權](#授權)

---

## 專案目的

藉由 YOLO 技術學習影像偵測，並用 C# 模擬企業環境與其測試情境。

**企業環境面向** —— 佇列、韌性、即時推播、權限、可觀測性
**測試情境面向** —— 服務逾時、重試導致重複寫入、上傳成功但推論失敗、手機離線補傳同一張圖的去重

| 元件 | 學習面向 |
|---|---|
| **Python 推論服務** | 應用電腦視覺 —— YOLO、前處理慣例、模型服務化，以及後續的資料集與評估工作 |
| **C# 業務層** | 分散式系統工程 —— 非同步作業處理、即時推播、韌性、冪等性、可觀測性 |
| **Vue + MAUI 客戶端** | 全端交付 —— 面對非同步 API 的儀表板與行動端 |

**期望功能目標：** 手機對物件拍照，模型辨識畫面內容，桌機儀表板即時更新，並保留每一次辨識的可查詢歷史。

推論歸 Python —— 訓練、資料增強、評估、模型迭代都在這個生態。

C# 負責作業生命週期、持久化、裝置身分、去重，以及客戶端仰賴的各種保證。Python 服務則是無狀態的計算資源，可以重啟、擴充或替換而業務層不受影響。

---

## 期望學習的技能

**以非同步為前提設計。**
`POST /detections` 不等待推論。它先把上傳的影像持久化、建立 `Pending` 作業，立刻回 `202` 與作業識別碼。背景工作者從佇列取出、呼叫模型服務，並推進紀錄的狀態：`Pending → Processing → Done | Failed`。

**即時推播。**
完成的辨識結果透過 SignalR 推送給已連線的客戶端。手機拍的照片一處理完，桌機儀表板就多一列 —— 不輪詢、不重新整理。

**契約層級的冪等性。**
每次拍攝都帶一個由客戶端產生的 `requestId`。行動端離線佇列的重送、或部分失敗後的重複提交，都會對應到同一筆紀錄而非產生第二筆。

**處理錯誤情境。**
逾時、模型服務重啟、部分失敗都被明確建模：指數退避重試、斷路器避免雪崩，以及儀表板會呈現 `Failed`。

---

## 開發階段

本專案分三個階段開發。

| 階段 | 主題 | 狀態 |
|---|---|---|
| 第一階段 | 系統跑通 —— 完成推論服務與企業級後端環境 | 進行中 |
| 第二階段 | PyTorch 基礎 —— 手寫訓練迴圈 | 規劃中 |
| 第三階段 | 自訓練模型 —— 資料集、fine-tune、評估報告 | 規劃中 |

### 第一階段 — 系統跑通

用 COCO 預訓練模型完成整套系統，不訓練。

| 產出 | 預期效用 |
|---|---|
| 非同步作業管線：`BackgroundService` + `Channel`，明確的狀態機 | 佇列設計、背壓、終態處理 |
| SignalR hub，儀表板與行動端同時訂閱 | 反向代理下的即時傳輸、連線生命週期 |
| 韌性層：逾時、指數退避重試、斷路器、冪等鍵 | 分散式系統最核心的一課 —— 跨服務邊界沒有原子操作 |
| Python FastAPI 推論服務 | 模型服務化、請求結構設計、容器封裝 |
| ASP.NET Core API + EF Core + PostgreSQL | 分層後端、上傳處理、查詢與索引設計 |
| Vue 3 儀表板 | 即時更新的表格、篩選、分頁 |
| .NET MAUI Android 端 | 相機權限、拍攝、離線佇列與補傳 |
| Docker Compose 部署 | 四服務拓樸、含 WebSocket 升級的反向代理 |


### 第二階段 — PyTorch 基礎

從零手寫一個小型圖片分類專案：`Dataset`、`DataLoader`、訓練迴圈、反向傳播、優化器更新。

**預期效用：** Ultralytics 把訓練迴圈藏在 YAML 底下。

刻意選分類任務 —— 偵測的損失函式混合分類、定位與 objectness 三項。

### 第三階段 — 自訓練模型

建立領域資料集、fine-tune、評估。

| 產出 | 預期效用 |
|---|---|
| 標註完成的資料集，含類別分布統計 | 資料品質決定模型品質 —— ML 中最反直覺的一課 |
| fine-tune 後的權重部署到推論服務 | 模型上線而不觸碰業務層 |
| 評估報告：mAP@50、mAP@50-95、各類別 PR 曲線、混淆矩陣 | 理解為什麼「準確率」在偵測任務裡沒有意義 |
| 最差樣本的失敗分析 | 多數改進來自修正資料，而非調整超參數 |

**評估報告讓專案可以用數字說話**，而不是停在「用了 YOLO，效果不錯」。

---

## 系統架構

![系統架構圖](docs/architecture\(zh\).png)

1.`IdempotencyFilter`、`InferenceWorker`、`ModelServiceClient`、`DetectionHub` 才是讓這套系統從「請求回應包裝」變成「失敗時行為明確的系統」的元件。

2.上傳路徑與結果路徑刻意分離。`POST` 在作業持久化後立刻返回，結果稍後經 hub 送達。儀表板與行動端都訂閱同一個 hub，這就是為什麼手機拍的照片會出現在桌機而不需重新整理。

3.`Channel<DetectionJob>` 是行程內佇列。單一 API 實例夠用，也讓部署維持在四個容器 —— 但它不能在重啟後存活，也無法跨副本分派。這個取捨列在[已知限制](#已知限制)。

---

## 部署拓樸

![部署拓樸圖](docs/deployment\(zh\).png)

**對外只發布一個連接埠。**
Nginx 提供靜態檔、代理 `/api/*`，並把 `/hub/*` 升級為 WebSocket。瀏覽器與手機看到同一個來源，不需要 CORS 設定。

**模型服務對外不可達。**
只在 Docker 內部網路可定址，業務層是唯一呼叫者。

**WebSocket 需要明確的代理設定。**
`proxy_http_version 1.1` 加上 `Upgrade` 與 `Connection` 標頭。少了它們，SignalR 會靜靜退回長輪詢，或直接連不上。

**權重以 volume 掛載而非包進 image。**
換模型只是替換檔案並重啟一個容器。

---

## API 契約

### `POST /api/v1/detections`

`multipart/form-data`：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `image` | file | JPEG 或 PNG |
| `requestId` | GUID | **由客戶端產生**，去重鍵 |
| `deviceId` | string | 裝置識別 |
| `capturedAt` | ISO 8601 | 拍攝時間，斷網後可能早於上傳時間 |

回傳 `202 Accepted`：

```jsonc
{ "jobId": "...", "status": "Pending" }
```

重複的 `requestId` 會回傳原本的作業而非建立新的 —— 此時狀態碼為 `200`，客戶端可據此分辨兩者。

### `GET /api/v1/detections/{jobId}`

查詢目前狀態；完成後包含辨識結果。作為 hub 連線不可用時的備援。

### `GET /api/v1/detections`

分頁歷史。篩選條件：`from`、`to`、`label`、`deviceId`、`status`。

### SignalR hub — `/hub/detections`

| 事件 | 內容 |
|---|---|
| `DetectionCompleted` | 完整辨識紀錄 |
| `DetectionFailed` | 作業 id、失敗原因、嘗試次數 |

### 作業狀態

```
Pending ──→ Processing ──→ Done
                │
                └──→ Failed   （重試耗盡，或不可重試的錯誤）
```

### 內部契約 — C# 與模型服務之間

```jsonc
// POST /infer 回應
{
  "model_version": "yolo-coco-v1",
  "inference_ms": 842,
  "image_width": 4032,
  "image_height": 3024,
  "detections": [
    {
      "label": "person", "class_id": 0, "confidence": 0.91,
      "x": 120, "y": 340, "width": 210, "height": 480
    }
  ]
}
```

座標為**左上角 + 寬高、原圖像素座標**。YOLO 內部使用中心點與正規化寬高，轉換在模型服務內完成。

`model_version` 隨每筆紀錄寫入資料庫 —— 沒有它，第三階段換模型後分不清準確度變化的來源。

---

## 技術組成

| 層 | 技術 | 階段 |
|---|---|---|
| 業務 API | ASP.NET Core · EF Core | 1 |
| 非同步處理 | `BackgroundService` · `System.Threading.Channels` | 1 |
| 即時通訊 | SignalR | 1 |
| 韌性 | Polly —— 逾時、重試、斷路器 | 1 |
| 可觀測性 | Serilog · 健康檢查 · OpenTelemetry | 1 |
| 推論服務 | Python · FastAPI · Ultralytics YOLO | 1 |
| 資料庫 | PostgreSQL 16 | 1 |
| 前端 | Vue 3 · Vite · Pinia | 1 |
| 行動端 | .NET MAUI（Android） | 1 |
| 容器化 | Docker · Docker Compose | 1 |
| 持續整合 | GitHub Actions | 1 |
| 訓練 | PyTorch · Ultralytics | 3 |
| 評估 | Python · matplotlib · scikit-learn | 3 |

---

## 測試策略

推論位於 HTTP 邊界之後，因此業務層是對**模擬的模型服務**測試，而非真實服務。契約測試涵蓋真正重要的情境：

| 情境 | 預期行為 |
|---|---|
| 模型服務回 200 | 作業進入 `Done`，hub 事件只發一次 |
| 模型服務逾時 | 依退避策略重試，最終進入 `Failed` |
| 模型服務連續兩次 500 後成功 | 作業進入 `Done`，不產生重複紀錄 |
| 斷路器開啟 | 快速失敗，不呼叫服務 |
| 相同 `requestId` 提交兩次 | 只有一筆紀錄，第二次回傳第一次的作業 |
| 相同 `requestId` **併發**提交 | 由資料庫唯一索引仲裁，仍只有一筆 |
| 上傳成功但工作者中途崩潰 | 作業不會被靜默遺失 |

這比測試順利路徑更貼近正式環境，也是大部分設計決定真正被驗證的地方。

---

## 專案結構

*初步規劃；各階段完成後依實際檔案更新。*

```
vision-detect/
├── model-service/      Python FastAPI 推論服務
├── ml/                 第三階段 —— 資料集、訓練、評估報告
├── backend/
│   └── src/
│       ├── VisionApi/                Controllers · Hubs · BackgroundServices
│       ├── VisionApi.Core/           實體 · 作業狀態機 · 介面
│       ├── VisionApi.Infrastructure/ 模型服務客戶端 · 韌性 · 儲存
│       └── VisionApi.Data/           EF Core
├── frontend/           Vue 3 儀表板
├── mobile/             .NET MAUI 行動端
├── docs/               架構圖與截圖
├── .github/workflows/  CI 流程
└── docker-compose.yml
```

---

## 已知限制

誠實列出 —— 這些是決定而非疏漏：

| 限制 | 理由 |
|---|---|
| 行程內佇列，非持久化訊息代理 | 單一實例足夠；導入代理會多一個容器，但在此階段學不到新東西 |
| 單一 API 實例 | SignalR 跨副本需要 backplane，超出本專案範圍 |
| 無使用者帳號 | 以 `deviceId` 識別裝置，未建模認證授權 |
| 影像存於本機 volume | 非物件儲存；單機部署足夠 |
| 未使用 GPU | CPU 推論；對「拍攝後檢視」的工作流程延遲可接受 |

---

## 授權

### 本專案

本專案以 **GNU Affero General Public License v3.0（AGPL-3.0）** 釋出，完整條款見 [`LICENSE`](LICENSE)。


### 若要作為商業用途

需向 Ultralytics 取得 Enterprise License，或改用授權較寬鬆的偵測模型（例如 Apache-2.0 授權者）。後者在本架構下的改動成本很低 —— 推論服務是獨立容器，替換模型不影響業務層與客戶端。



