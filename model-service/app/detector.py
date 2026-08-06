# 推論核心 —— 模型載入與影像辨識
#
# 設計重點：
#   1. 模型是單例：啟動時載入一次。若寫在請求處理函式裡，
#      每次請求都重載權重，延遲會從數百毫秒變成數秒。
#   2. 啟動時暖機：第一次推論明顯較慢（記憶體配置、圖優化），
#      用一張假圖先跑掉，避免第一個真實使用者承受這個延遲。
#   3. 座標轉換在此完成：對外一律是「左上角 + 寬高、原圖像素」。

import asyncio
import io
import time

from PIL import Image, UnidentifiedImageError

from . import config
from .schemas import Detection


# 影像無法解析時拋出，由 main.py 轉成 HTTP 400
class InvalidImageError(Exception):
    pass


class Detector:
    """YOLO 模型的封裝。整個服務只會有一個實例。"""

    def __init__(self) -> None:
        self._model = None          # 尚未載入
        self._labels: dict[int, str] = {}
        # 限制同時推論數，避免多請求爭搶 CPU 反而全部變慢
        self._gate = asyncio.Semaphore(config.get_max_concurrency())

    # 是否已可服務。main.py 用它決定 /health 要回 ok 還是 loading。
    @property
    def is_ready(self) -> bool:
        return self._model is not None

    # 類別清單，供 GET /labels 使用
    @property
    def labels(self) -> dict[int, str]:
        return self._labels

    # 載入權重並暖機。在 FastAPI 的 lifespan 啟動事件呼叫一次。
    def load(self) -> None:
        from ultralytics import YOLO  # 延後 import：這行很慢，且只有這裡需要

        self._model = YOLO(config.get_weights())
        # model.names 是 {索引: 名稱}，直接沿用可避免手打類別造成順序錯位
        self._labels = dict(self._model.names)

        # 暖機：跑一張純黑小圖，把第一次推論的額外開銷吃掉
        self._model.predict(Image.new("RGB", (640, 640)), verbose=False)

    # 對一張影像做推論。
    # 回傳 (偵測結果, 推論耗時毫秒, 原圖寬, 原圖高)
    async def infer(
        self, image_bytes: bytes, conf: float | None = None
    ) -> tuple[list[Detection], int, int, int]:
        image = self._decode(image_bytes)
        threshold = conf if conf is not None else config.get_conf_threshold()

        async with self._gate:
            started = time.perf_counter()
            # Ultralytics 是同步且吃 CPU 的，丟到執行緒避免卡住事件迴圈
            results = await asyncio.to_thread(
                self._model.predict, image, conf=threshold, verbose=False
            )
            elapsed_ms = int((time.perf_counter() - started) * 1000)

        return self._to_detections(results[0]), elapsed_ms, image.width, image.height

    # 位元組解碼成 PIL 影像。解不開就是客戶端的問題，拋 InvalidImageError。
    @staticmethod
    def _decode(image_bytes: bytes) -> Image.Image:
        try:
            image = Image.open(io.BytesIO(image_bytes))
            image.load()               # 真的讀取像素，才能發現截斷的檔案
            return image.convert("RGB")
        except (UnidentifiedImageError, OSError) as exc:
            raise InvalidImageError(str(exc)) from exc

    # 把 Ultralytics 的結果轉成契約定義的格式。
    # boxes.xyxy 已經是原圖像素座標，這裡只需換算成左上角 + 寬高。
    def _to_detections(self, result) -> list[Detection]:
        detections: list[Detection] = []
        for box in result.boxes:
            x1, y1, x2, y2 = (int(v) for v in box.xyxy[0].tolist())
            class_id = int(box.cls[0])
            detections.append(
                Detection(
                    label=self._labels.get(class_id, str(class_id)),
                    class_id=class_id,
                    confidence=round(float(box.conf[0]), 4),
                    x=x1,
                    y=y1,
                    width=x2 - x1,
                    height=y2 - y1,
                )
            )
        return detections


# 全服務共用的單例，由 main.py 在啟動時呼叫 load()
detector = Detector()
