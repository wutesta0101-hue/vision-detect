# 設定 —— 所有可調參數集中在此，從環境變數讀取
#
# 為什麼包成函式而不是模組層級常數：
#   常數在 import 當下就凍結，測試時無法暫時覆寫；
#   函式則是每次呼叫才讀，行為可預期。

import os


# 權重檔名或路徑。第一次執行時 Ultralytics 會自動下載官方預訓練權重。
# 換成自訓練模型時只要改這個環境變數，程式碼不動。
def get_weights() -> str:
    return os.getenv("MODEL_WEIGHTS", "yolov8n.pt")


# 模型版本字串 —— 會寫進每一筆辨識紀錄，用來追溯結果是哪個模型算的
def get_model_version() -> str:
    return os.getenv("MODEL_VERSION", "yolov8n-coco")


# 預設信心閾值，低於此值的偵測結果會被丟棄
def get_conf_threshold() -> float:
    return float(os.getenv("CONFIDENCE_THRESHOLD", "0.25"))


# 同時推論數上限。
# CPU 推論會吃滿多執行緒，同時跑多張不會比較快，只會互相競爭並可能爆記憶體。
def get_max_concurrency() -> int:
    return int(os.getenv("MAX_CONCURRENCY", "1"))


# 上傳影像大小上限（位元組），超過直接回 413
def get_max_image_bytes() -> int:
    return int(os.getenv("MAX_IMAGE_MB", "10")) * 1024 * 1024
