# 契約 — C# 業務層 ↔ Python 模型服務

> 這是整個系統唯一的跨語言邊界。兩邊各自實作，靠這份文件對齊。
> 修改此契約時，C# 與 Python 必須同時更新。

**Base URL（容器內部）**：`http://model:8000`
**對外不可達** —— 僅 Docker 內部網路可定址。

---

## 端點總覽

| 方法 | 路徑 | 用途 |
|---|---|---|
| POST | `/infer` | 影像推論 |
| GET | `/labels` | 目前模型的類別清單 |
| GET | `/health` | 服務與模型狀態 |

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

---

## `GET /labels`

回傳目前載入模型的類別清單。C# 啟動時取得並快取，供前端篩選器使用。

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

---

## 命名慣例

| | 慣例 | 範例 |
|---|---|---|
| Python（本服務） | `snake_case` | `model_version` |
| C#（業務層） | `PascalCase` | `ModelVersion` |

兩邊各保持自己的語言慣例。C# 在 `JsonSerializerOptions` 設定命名策略，轉換集中在一處。

---

## 版本化

`/infer` 的回應結構保持穩定。若未來需要不相容的變更，新增 `/v2/infer`，不直接修改現有端點。

---

## 契約測試

C# 側的 CI 應抓取模型服務的 `/openapi.json`（FastAPI 自動產生），斷言欄位名稱與型別未變。Python 那邊改了欄位名，CI 立刻紅燈。
