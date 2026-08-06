# model-service

Python 推論服務。無狀態，只回答「這張圖裡有什麼」。

契約定義見 [`CONTRACT.md`](CONTRACT.md)。

---

## 檔案

```
model-service/
├── app/
│   ├── config.py      環境變數，所有可調參數
│   ├── schemas.py     Pydantic 回應結構（＝跨語言契約）
│   ├── detector.py    模型載入與推論，座標轉換
│   └── main.py        FastAPI 三個端點與錯誤處理
├── tests/
│   └── test_api.py    契約行為測試
├── requirements.txt
├── Dockerfile
└── CONTRACT.md
```

約 250 行。

---

## 本機執行

```bash
cd model-service
python -m venv .venv

# Windows
.\.venv\Scripts\Activate.ps1
# Mac / Linux
source .venv/bin/activate

pip install -r requirements.txt
uvicorn app.main:app --reload --port 8000
```

> 第一次啟動會下載約 6 MB 的預訓練權重，並自動安裝 PyTorch（較大，需耐心等）。

看到這行代表就緒：

```
INFO:     Application startup complete.
```

---

## 五個最小驗證

依序做，每一步都能單獨確認。

### ① 服務活著

```bash
curl http://localhost:8000/health
```

預期：

```json
{"status":"ok","model_version":"yolov8n-coco","model_loaded":true,"label_count":80}
```

`label_count` 是 80 就代表 COCO 模型載入成功。

### ② 類別清單

```bash
curl http://localhost:8000/labels
```

第一筆應該是 `{"class_id":0,"label":"person"}`。

### ③ 真實影像推論

用一張有人或車的照片：

```bash
curl -X POST http://localhost:8000/infer -F "image=@your-photo.jpg"
```

PowerShell：

```powershell
curl.exe -X POST http://localhost:8000/infer -F "image=@your-photo.jpg"
```

預期回傳 `detections` 陣列，每個框有 `label`、`confidence`、`x`、`y`、`width`、`height`。

**檢查 `image_width` / `image_height` 是不是你照片的實際尺寸** —— 若是 640×640，代表座標轉換寫錯了。

### ④ 錯誤處理

```bash
# 損毀的影像 → 應回 400 而非 500
curl -X POST http://localhost:8000/infer -F "image=@README.md"

# 非法參數 → 400
curl -X POST http://localhost:8000/infer -F "image=@your-photo.jpg" -F "conf_threshold=1.5"
```

400 與 500 的區分很重要：C# 收到 400 不會重試，收到 500 會重試。

### ⑤ Swagger 手動測試

瀏覽器打開 <http://localhost:8000/docs>

三個端點都能直接上傳檔案測試。`/openapi.json` 就是之後 C# 契約測試要比對的來源。

---

## 座標正確性的肉眼驗證

數字對不代表框畫對。用這段確認：

```python
# verify_boxes.py —— 把偵測框畫回影像，肉眼確認位置
import requests
from PIL import Image, ImageDraw

PHOTO = "your-photo.jpg"

with open(PHOTO, "rb") as f:
    result = requests.post("http://localhost:8000/infer", files={"image": f}).json()

image = Image.open(PHOTO)
draw = ImageDraw.Draw(image)

for box in result["detections"]:
    x, y, w, h = box["x"], box["y"], box["width"], box["height"]
    draw.rectangle([x, y, x + w, y + h], outline="red", width=3)
    draw.text((x, max(0, y - 12)), f'{box["label"]} {box["confidence"]:.2f}', fill="red")

image.save("verified.png")
print(f'{len(result["detections"])} 個框，已存成 verified.png')
```

```bash
pip install requests
python verify_boxes.py
```

打開 `verified.png`，框應該準確套在物件上。

**框有出來但位置整體偏移** → 座標轉換有問題。這是這類系統最常見的 bug，而且只有這個方法看得出來。

---

## 自動化測試

```bash
pytest -v
```

12 個測試，涵蓋三個端點的正常與錯誤路徑。第一次執行較慢（要載入模型）。

---

## 容器執行

```bash
docker build -t vd-model .
docker run --rm -p 8000:8000 vd-model
```

image 約 2–3 GB —— PyTorch 本身就這麼大，正常。

---

## 環境變數

| 變數 | 預設 | 說明 |
|---|---|---|
| `MODEL_WEIGHTS` | `yolov8n.pt` | 權重檔名或路徑 |
| `MODEL_VERSION` | `yolov8n-coco` | 寫進辨識紀錄的版本字串 |
| `CONFIDENCE_THRESHOLD` | `0.25` | 預設信心閾值 |
| `MAX_CONCURRENCY` | `1` | 同時推論數上限 |
| `MAX_IMAGE_MB` | `10` | 上傳大小上限 |

第三階段換自訓練模型時，只改 `MODEL_WEIGHTS` 與 `MODEL_VERSION`，程式碼不動。

> 權重檔名依 Ultralytics 版本而異，`yolov8n.pt` 是穩定可用的選擇。若要用更新的模型，先確認該版本支援的檔名。
