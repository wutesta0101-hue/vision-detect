# 契約 — C# 業務層 ↔ Python 模型服務

> 這是整個系統唯一的跨語言邊界。兩邊各自實作，靠這份文件對齊。
> 修改此契約時，C# 與 Python 必須同時更新。

**Base URL（容器內部）**：`http://model:8000`

**對外不可達** —— 僅 Docker 內部網路可定址。

---

## 端點總覽

| 方法 | 路徑 | 用途 | 呼叫者 |
|---|---|---|---|
| POST | `/infer` | 影像推論 | C# `InferenceWorker`（經 `ModelServiceClient`） |
| GET | `/labels` | 目前模型的類別清單 | C# `DetectionsController`（轉發前端請求） |
| GET | `/health` | 服務與模型狀態 | Docker healthcheck |

---

## `POST /infer`

### 請求

`multipart/form-data`

| 欄位 | 型別 | 必填 | 說明 |
|---|---|---|---|
| `image` | file | ✅ | JPEG 或 PNG，上限 10 MB |
| `conf_threshold` | float | ❌ | 0.0–1.0，覆寫預設信心閾值 |

### 回應 `200`

```jsonc
{
  "model_version": "yolov8n-coco",   // 隨紀錄寫入資料庫
  "inference_ms": 842,               // 純推論耗時，不含網路
  "image_width": 4032,               // 座標的參考基準
  "image_height": 3024,
  "detections": [
    {
      "label": "person",
      "class_id": 0,
      "confidence": 0.91,
      "x": 120,        // 左上角 X
      "y": 340,        // 左上角 Y
      "width": 210,
      "height": 480
    }
  ]
}
```

**座標系統**：左上角 + 寬高，單位為**原圖像素**，整數。

原點在影像左上角，X 向右、Y 向下。YOLO 內部使用中心點與正規化寬高，轉換在模型服務內完成 —— C# 不需要知道這件事。

**空結果是合法的**：畫面中沒有可辨識物件時，`detections` 為空陣列，狀態碼仍是 `200`。

### 並行行為

服務內部以 Semaphore 限制同時推論數（預設 1，由 `MAX_CONCURRENCY` 控制）。

超過上限的請求會**排隊等待**而非被拒絕。C# 端的逾時設定必須把這段排隊時間算進去 —— 目前單次嘗試逾時為 10 秒（`Resilience:AttemptTimeoutSeconds`）。

---

## `GET /labels`

回傳目前載入模型的類別清單。C# 轉發前端的請求，供儀表板的類別篩選器使用。

```jsonc
{
  "model_version": "yolov8n-coco",
  "count": 80,
  "labels": [
    { "class_id": 0, "label": "person" },
    { "class_id": 1, "label": "bicycle" }
  ]
}
```

**類別清單來自模型本身**（Ultralytics 的 `model.names`），不是手動維護的常數。

換成自訂模型後，這個端點會自動回傳新的類別 —— 前端不需要修改，這也是前端不寫死類別清單的理由。

---

## `GET /health`

```jsonc
{
  "status": "ok",                  // "ok" | "loading"
  "model_version": "yolov8n-coco",
  "model_loaded": true,
  "label_count": 80
}
```

模型尚未載入完成時回 `503`，`status` 為 `"loading"`。

**這個端點的用途是容器編排，不是業務邏輯。**

`model-service/Dockerfile` 的 `HEALTHCHECK` 會定期呼叫它；`docker-compose.yml` 的 `api` 服務靠 `depends_on: model: condition: service_healthy` 等它就緒才啟動。

少了這個機制，API 會在模型還在載入時就開始接受請求，前幾個作業會白白失敗。

> C# 程式碼本身不呼叫 `/health` —— 服務可用性由 Polly 的斷路器處理。

---

## 錯誤回應

**所有錯誤共用同一結構：**

```jsonc
{
  "error": "invalid_image",
  "detail": "無法解析影像檔案"
}
```

### 狀態碼對照 — C# 的重試策略依據

| 狀態碼 | `error` | 情境 | C# 該做什麼 |
|---|---|---|---|
| `400` | `invalid_image` | 檔案損毀、格式不支援 | **不重試**，直接標記 `Failed` |
| `400` | `invalid_parameter` | `conf_threshold` 超出範圍 | **不重試** |
| `413` | `image_too_large` | 超過 10 MB | **不重試** |
| `503` | `model_not_ready` | 模型還在載入 | **重試**，退避等待 |
| `500` | `inference_error` | 推論過程未預期錯誤 | **重試**，有限次數 |

**這張表是這份契約最實用的部分。** 全部回 `500` 的話，C# 會對著一張損毀的影像重試三次才放棄 —— 浪費時間，而且日誌看起來像服務不穩定。

### C# 端的對應實作

分類邏輯在 `VisionApi.Infrastructure/ModelService/ModelServiceErrors.cs`：

```csharp
public static bool IsRetryable(HttpStatusCode status) => status switch
{
    HttpStatusCode.BadRequest => false,              // 400
    HttpStatusCode.RequestEntityTooLarge => false,   // 413
    HttpStatusCode.NotFound => false,                // 404 端點不存在（設定錯誤）
    HttpStatusCode.ServiceUnavailable => true,       // 503
    // ...
    _ => (int)status >= 500                          // 其餘 5xx 一律可重試
};
```

**未列舉的狀態碼有明確的預設行為**：4xx 不重試、5xx 重試。這樣本服務新增錯誤碼時，C# 不會因為沒更新而落到未定義的行為。

此外還有兩類**不經過狀態碼**的失敗，一律視為可重試：

| 情境 | C# 收到的例外 |
|---|---|
| 服務未啟動、連線被拒 | `HttpRequestException` |
| 連得上但無回應 | `TimeoutRejectedException`（Polly 的逐次逾時） |

### 兩層重試

| 層 | 觸發 | 時間尺度 | 作業狀態 |
|---|---|---|---|
| HTTP 層（Polly） | 可重試的狀態碼、連線失敗、逾時 | 秒級，退避 2 / 4 / 8 秒 | 停留在 `Processing` |
| 作業層（`InferenceWorker`） | Polly 全部耗盡後仍失敗 | 分鐘級，間隔 20 秒 | 回到 `Pending` 重新排隊 |

最壞情況為 `3 次作業嘗試 × (1 + 3 次 HTTP 重試) = 12 次實際呼叫`，橫跨約一分鐘，之後才標記 `Failed`。

---

## 命名慣例

| | 慣例 | 範例 |
|---|---|---|
| Python（本服務） | `snake_case` | `model_version` |
| C#（業務層） | `PascalCase` | `ModelVersion` |
| 對外 API（給前端） | `camelCase` | `modelVersion` |

各層保持自己的語言慣例，不遷就彼此。兩次轉換分別由：

| 轉換 | 由誰處理 |
|---|---|
| `snake_case` ⇄ `PascalCase` | `ModelServiceClient` 的 `JsonSerializerOptions`（`JsonNamingPolicy.SnakeCaseLower`） |
| `PascalCase` → `camelCase` | ASP.NET Core 的預設序列化 |

轉換集中在這兩處，其他程式碼不需要知道命名差異的存在。

---

## 環境變數

模型服務的可調參數，定義於 `model-service/app/config.py`：

| 變數 | 預設 | 說明 |
|---|---|---|
| `MODEL_WEIGHTS` | `/app/weights/yolov8n.pt` | 權重檔路徑。映像建置時已下載至此 |
| `MODEL_VERSION` | `yolov8n-coco` | 回應中的版本字串，隨紀錄寫入資料庫 |
| `CONFIDENCE_THRESHOLD` | `0.25` | 預設信心閾值，可被請求的 `conf_threshold` 覆寫 |
| `MAX_CONCURRENCY` | `1` | 同時推論數上限 |
| `MAX_IMAGE_MB` | `10` | 上傳大小上限 |

**換模型只需改前兩項**，並以 volume 掛載新的權重檔：

```yaml
model:
  environment:
    MODEL_WEIGHTS: /app/weights/custom.pt
    MODEL_VERSION: ppe-v1
  volumes:
    - ./model-service/weights:/app/weights:ro
```

業務層與客戶端完全不動 —— 這是本契約設計的主要目的。

---

## 版本化

`/infer` 的回應結構保持穩定。若未來需要不相容的變更，新增 `/v2/infer`，不直接修改現有端點。

**已預留的擴充點**：

- `model_version` 讓客戶端能區分不同模型產出的結果
- `class_id` 與 `label` 並存 —— 名稱可能改，索引相對穩定
- `image_width` / `image_height` 明確宣告座標基準，未來若支援多種輸出座標系不會產生歧義

---

## 契約測試

C# 側的 CI 應抓取模型服務的 `/openapi.json`（FastAPI 自動產生），斷言欄位名稱與型別未變。Python 那邊改了欄位名，CI 立刻紅燈。

**目前的實作狀態**：尚未建立自動化的契約測試。C# 側改以**假的 HTTP 處理器**驗證各種回應的處理行為（`VisionApi.Infrastructure.Tests/ModelServiceClientTests.cs`），涵蓋：

| 測試情境 | 驗證什麼 |
|---|---|
| 200 正常回應 | `snake_case` 正確對應到 C# 屬性 |
| 空的 `detections` | 不拋例外 |
| 400 / 413 | 標記為不可重試 |
| 503 / 500 | 標記為可重試 |
| 非 JSON 的錯誤回應（例如代理層的 HTML 錯誤頁） | 解析失敗不掩蓋原始錯誤 |

這組測試**不需要啟動 Python 服務**，但也因此無法偵測「Python 那邊改了欄位名」—— 那正是 `/openapi.json` 契約測試要補上的缺口。

---

## 手動驗證

服務啟動後可直接測試：

```bash
# 健康檢查
curl http://localhost:8000/health

# 類別清單
curl http://localhost:8000/labels

# 影像推論
curl -X POST http://localhost:8000/infer -F "image=@test.jpg"

# 錯誤處理（拿文字檔冒充影像，應回 400）
curl -i -X POST http://localhost:8000/infer -F "image=@README.md"
```

互動式文件：<http://localhost:8000/docs>

> `:8000` 僅在開發環境開放（`docker-compose.override.yml`）。
> 正式部署時模型服務不對外，只能從 api 容器內存取。