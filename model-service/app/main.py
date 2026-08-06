# FastAPI 入口 —— 三個端點與錯誤處理
#
# 本服務是無狀態的計算資源：不碰資料庫、不記得任何請求。
# 所有狀態管理都在 C# 業務層。
#
# 啟動：uvicorn app.main:app --host 0.0.0.0 --port 8000
# 文件：http://localhost:8000/docs

from contextlib import asynccontextmanager

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import JSONResponse

from . import config
from .detector import InvalidImageError, detector
from .schemas import (
    ErrorResponse,
    HealthResponse,
    InferResponse,
    LabelItem,
    LabelsResponse,
)


# 統一的錯誤回應。error 欄位是 C# 判斷「該不該重試」的依據。
def error(status: int, code: str, detail: str) -> JSONResponse:
    return JSONResponse(
        status_code=status,
        content=ErrorResponse(error=code, detail=detail).model_dump(),
    )


# 啟動與關閉的生命週期。模型在此載入一次，不放在請求處理函式裡。
@asynccontextmanager
async def lifespan(_: FastAPI):
    detector.load()
    yield


app = FastAPI(
    title="vision-detect model service",
    version="1.0.0",
    lifespan=lifespan,
)


# 影像推論。契約詳見 CONTRACT.md。
@app.post("/infer", response_model=InferResponse, tags=["inference"])
async def infer(
    image: UploadFile = File(...),
    conf_threshold: float | None = Form(None),
):
    if not detector.is_ready:
        return error(503, "model_not_ready", "模型尚未載入完成")

    if conf_threshold is not None and not 0 <= conf_threshold <= 1:
        return error(400, "invalid_parameter", "conf_threshold 必須介於 0 與 1")

    data = await image.read()
    if len(data) > config.get_max_image_bytes():
        return error(413, "image_too_large", "影像超過大小上限")

    try:
        detections, ms, width, height = await detector.infer(data, conf_threshold)
    except InvalidImageError as exc:
        return error(400, "invalid_image", f"無法解析影像：{exc}")
    except Exception as exc:  # 未預期錯誤 —— C# 會重試
        return error(500, "inference_error", str(exc))

    return InferResponse(
        model_version=config.get_model_version(),
        inference_ms=ms,
        image_width=width,
        image_height=height,
        detections=detections,
    )


# 類別清單。C# 啟動時取得並快取，供前端篩選器使用。
@app.get("/labels", response_model=LabelsResponse, tags=["metadata"])
async def labels():
    if not detector.is_ready:
        return error(503, "model_not_ready", "模型尚未載入完成")

    items = [LabelItem(class_id=k, label=v) for k, v in sorted(detector.labels.items())]
    return LabelsResponse(
        model_version=config.get_model_version(),
        count=len(items),
        labels=items,
    )


# 健康檢查。Docker healthcheck 與 C# 啟動時都會打這個端點。
@app.get("/health", response_model=HealthResponse, tags=["metadata"])
async def health():
    ready = detector.is_ready
    body = HealthResponse(
        status="ok" if ready else "loading",
        model_version=config.get_model_version(),
        model_loaded=ready,
        label_count=len(detector.labels),
    )
    return JSONResponse(status_code=200 if ready else 503, content=body.model_dump())
