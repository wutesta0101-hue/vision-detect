# 回應結構 —— 對應 CONTRACT.md
#
# 這些類別同時扮演三個角色：
#   1. 序列化格式（FastAPI 依此輸出 JSON）
#   2. API 文件（/docs 與 /openapi.json 由此自動產生）
#   3. 跨語言契約（C# 端的 DTO 必須與此一致）
#
# 改動任何欄位名稱前，記得同步 C# 側與 CONTRACT.md。

from pydantic import BaseModel, Field


# 單一偵測框。座標為原圖像素、左上角 + 寬高。
class Detection(BaseModel):
    label: str = Field(..., description="類別名稱，例如 'person'")
    class_id: int = Field(..., description="類別索引，對應模型的類別順序")
    confidence: float = Field(..., ge=0, le=1)
    x: int = Field(..., description="左上角 X（原圖像素）")
    y: int = Field(..., description="左上角 Y（原圖像素）")
    width: int
    height: int


# POST /infer 的回應
class InferResponse(BaseModel):
    model_version: str
    inference_ms: int = Field(..., description="純推論耗時，不含網路與解碼")
    image_width: int = Field(..., description="座標的參考基準")
    image_height: int
    detections: list[Detection]


# GET /labels 清單中的單筆
class LabelItem(BaseModel):
    class_id: int
    label: str


# GET /labels 的回應
class LabelsResponse(BaseModel):
    model_version: str
    count: int
    labels: list[LabelItem]


# GET /health 的回應
class HealthResponse(BaseModel):
    status: str = Field(..., description="'ok' 或 'loading'")
    model_version: str
    model_loaded: bool
    label_count: int


# 所有錯誤共用此結構，C# 依 error 欄位決定要不要重試
class ErrorResponse(BaseModel):
    error: str
    detail: str
