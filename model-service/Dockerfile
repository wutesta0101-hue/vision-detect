# 模型服務容器 —— Python 推論
#
# 基底用 slim 版（約 130MB），不用完整版（約 1GB）。
# 但 ultralytics 會帶進 PyTorch，最終映像仍有 2–3GB —— 這是無法避免的。

FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Ultralytics 需要一個可寫的設定目錄，預設位置在容器內沒有權限
ENV YOLO_CONFIG_DIR=/tmp/ultralytics

# OpenCV 需要的系統函式庫（ultralytics 相依）
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgl1 libglib2.0-0 curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 先裝套件單獨一層：改程式碼時這層命中快取，不重跑
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# 在建置階段就把預訓練權重抓下來。
#
# 為什麼不留到執行時下載：
#   容器每次重建都要重抓，而且在無網路的環境會直接啟動失敗。
#   權重只有幾 MB，包進映像很划算。
#
# 第三階段換自訓練模型時，改用 volume 掛載並設定 MODEL_WEIGHTS 即可，
# 不需要重新建置映像。
RUN mkdir -p /app/weights && cd /app/weights && \
    python -c "from ultralytics import YOLO; YOLO('yolov8n.pt')"

ENV MODEL_WEIGHTS=/app/weights/yolov8n.pt

COPY app/ ./app/

EXPOSE 8000

# 健康檢查：讓 Docker 知道模型是否已載入完成。
# 模型載入需要幾秒，start-period 給它緩衝時間。
HEALTHCHECK --interval=10s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fs http://localhost:8000/health || exit 1

# 正式環境不加 --reload
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
