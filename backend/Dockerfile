# 後端 API 容器
#
# 多階段建置：
#   階段 1 (build)   —— SDK 映像，還原套件並編譯
#   階段 2 (runtime) —— 只含執行環境的精簡映像，體積小很多
#
# build context 是 backend/ 資料夾（見 docker-compose.yml 的 context 設定），
# 所以下面的路徑都相對於 backend/。

# ============================================================
# 階段 1：編譯
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# 先只複製 .csproj 並還原套件。
#
# 為什麼分開：Docker 的每一層都有快取，只要 .csproj 沒變，
# restore 這層就會命中快取。改 .cs 程式碼時不用重新下載所有套件。
COPY src/VisionApi.Core/*.csproj              src/VisionApi.Core/
COPY src/VisionApi.Infrastructure/*.csproj    src/VisionApi.Infrastructure/
COPY src/VisionApi.Data/*.csproj              src/VisionApi.Data/
COPY src/VisionApi/*.csproj                   src/VisionApi/

RUN dotnet restore src/VisionApi/VisionApi.csproj

# 再複製其餘原始碼並發佈
COPY src/ src/
RUN dotnet publish src/VisionApi/VisionApi.csproj -c Release -o /app/publish --no-restore

# ============================================================
# 階段 2：執行
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# 健康檢查需要 curl。
# ASP.NET runtime 映像為了精簡，既沒有 curl 也沒有 wget —— 
# 少了這一步，compose 的 healthcheck 會永遠失敗（容器 Up 但 unhealthy）。
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# 影像儲存目錄。compose 會把 volume 掛在這裡。
RUN mkdir -p /app/images

# 容器內固定監聽 8080。對外開哪個 port 由 compose 決定。
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "VisionApi.dll"]
