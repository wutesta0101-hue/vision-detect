# API 測試 —— 驗證契約行為，不驗證辨識準確度
#
# 準確度是第三階段評估報告的事。這裡只確認：
#   回應結構正確、錯誤碼分類正確、邊界情況不會炸掉。
#
# 跑法：cd model-service && pytest -v

import io

import pytest
from fastapi.testclient import TestClient
from PIL import Image

from app.main import app


# TestClient 進入 with 區塊時會觸發 lifespan，模型在此載入（第一次較慢）
@pytest.fixture(scope="module")
def client():
    with TestClient(app) as c:
        yield c


# 產生一張純色測試圖，避免測試依賴外部檔案
def make_image(width=640, height=480, fmt="JPEG") -> bytes:
    buffer = io.BytesIO()
    Image.new("RGB", (width, height), (120, 120, 120)).save(buffer, format=fmt)
    return buffer.getvalue()


# ---------- /health ----------

def test_health_ok(client):
    body = client.get("/health").json()
    assert body["status"] == "ok"
    assert body["model_loaded"] is True
    assert body["label_count"] > 0


# ---------- /labels ----------

def test_labels_shape(client):
    body = client.get("/labels").json()
    assert body["count"] == len(body["labels"])
    assert {"class_id", "label"} <= body["labels"][0].keys()


def test_labels_sorted_by_class_id(client):
    ids = [item["class_id"] for item in client.get("/labels").json()["labels"]]
    assert ids == sorted(ids)


# ---------- /infer 正常路徑 ----------

def test_infer_returns_contract_fields(client):
    response = client.post("/infer", files={"image": ("t.jpg", make_image(), "image/jpeg")})
    assert response.status_code == 200

    body = response.json()
    for key in ["model_version", "inference_ms", "image_width", "image_height", "detections"]:
        assert key in body, f"回應缺少欄位：{key}"


def test_infer_reports_original_size(client):
    # 座標的參考基準必須是原圖尺寸，不是模型的輸入尺寸（640×640）
    response = client.post(
        "/infer", files={"image": ("t.jpg", make_image(1024, 768), "image/jpeg")}
    )
    body = response.json()
    assert body["image_width"] == 1024
    assert body["image_height"] == 768


def test_infer_empty_result_is_ok(client):
    # 純色圖通常辨識不到東西 —— 空結果是合法的，不該報錯
    response = client.post("/infer", files={"image": ("t.jpg", make_image(), "image/jpeg")})
    assert response.status_code == 200
    assert isinstance(response.json()["detections"], list)


def test_infer_accepts_png(client):
    response = client.post(
        "/infer", files={"image": ("t.png", make_image(fmt="PNG"), "image/png")}
    )
    assert response.status_code == 200


# ---------- /infer 錯誤路徑（C# 重試策略的依據）----------

def test_invalid_image_returns_400(client):
    # 損毀的檔案 —— 重試沒有意義，必須是 400 而非 500
    response = client.post("/infer", files={"image": ("x.jpg", b"not an image", "image/jpeg")})
    assert response.status_code == 400
    assert response.json()["error"] == "invalid_image"


def test_invalid_threshold_returns_400(client):
    response = client.post(
        "/infer",
        files={"image": ("t.jpg", make_image(), "image/jpeg")},
        data={"conf_threshold": "1.5"},
    )
    assert response.status_code == 400
    assert response.json()["error"] == "invalid_parameter"


def test_missing_image_returns_422(client):
    # 缺必填欄位由 FastAPI 自動擋下
    assert client.post("/infer").status_code == 422


# ---------- 座標契約 ----------

def test_detection_boxes_within_image(client):
    # 若有偵測結果，座標必須落在原圖範圍內。
    # 這條會抓到座標系轉換錯誤（例如誤用模型輸入尺寸的座標）。
    body = client.post(
        "/infer", files={"image": ("t.jpg", make_image(800, 600), "image/jpeg")}
    ).json()

    for box in body["detections"]:
        assert 0 <= box["x"] <= body["image_width"]
        assert 0 <= box["y"] <= body["image_height"]
        assert box["x"] + box["width"] <= body["image_width"] + 1
        assert box["y"] + box["height"] <= body["image_height"] + 1
