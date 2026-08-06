# 模型服務 —— Python 推論容器
#
# 基底用 slim 版（約 130MB），不用完整版（約 1GB）。
# 注意 ultralytics 會帶進 PyTorch，最終 image 仍有 2-3GB，這是無法避免的。

FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# OpenCV 需要的系統函式庫（ultralytics 相依）
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgl1 libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# 先裝套件單獨一層：改程式碼時這層命中快取，不重跑
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app/ ./app/

EXPOSE 8000

# 正式環境不加 --reload
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
