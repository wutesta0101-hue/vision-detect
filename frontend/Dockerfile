# 前端容器
#
# 多階段建置：
#   階段 1 (build) —— Node 環境，npm ci 後 vite build 產出 dist/
#   階段 2 (serve) —— Nginx alpine，只複製 dist/ 進來
#
# 最終映像約 60MB（Nginx alpine 約 50MB + dist 幾 MB）。
# 單階段建置的話會包含整個 node_modules，約 500MB。

# ============================================================
# 階段 1：建置
# ============================================================
FROM node:20-alpine AS build

WORKDIR /app

# 先只複製 package 檔案並安裝。
# 改 .vue 程式碼時這一層會命中快取，不用重新安裝套件。
COPY package*.json ./

# npm ci 而非 npm install：
#   嚴格依照 package-lock.json 安裝，速度快且結果可重現。
#   正式建置應該用它 —— install 可能悄悄升級版本。
RUN npm ci

COPY . .

# Vite 會讀取 .env.production 決定 VITE_API_BASE
RUN npm run build

# ============================================================
# 階段 2：提供服務
# ============================================================
FROM nginx:1.27-alpine

# 複製建置產物到 Nginx 的網站根目錄
COPY --from=build /app/dist /usr/share/nginx/html

# 覆寫預設設定，加入 /api 代理與 /hub 的 WebSocket 升級
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

# nginx 映像已內建 CMD（nginx -g "daemon off;"），不需要再寫
