#!/usr/bin/env bash
# ================================================================
# 宝可梦全世代管理平台 — 一键启动开发环境
# 用法: ./scripts/start-dev.sh [--stop]
# ================================================================
set -e

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
PGDATA="$PROJECT_DIR/data/pgdata"
PG_LOG="$PGDATA/logfile"
SERVER_DIR="$PROJECT_DIR/server/PkManager.Server"
CLIENT_DIR="$PROJECT_DIR/client"
PKHEX_FEED_DIR="$PROJECT_DIR/server/artifacts/nuget"
PKHEX_PROPS="$PROJECT_DIR/server/PkManager.Server/Directory.Build.props"

# ── 加载根目录 config 文件 ────────────────────────────
CONFIG_FILE="$PROJECT_DIR/config"
if [[ -f "$CONFIG_FILE" ]]; then
  set -a
  source "$CONFIG_FILE"
  set +a
else
  echo "⚠ config 文件不存在，使用默认值（cp config.dst config 创建）"
  DB_HOST="${DB_HOST:-localhost}"
  DB_PORT="${DB_PORT:-5432}"
  DB_NAME="${DB_NAME:-pkmanager}"
  DB_USER="${DB_USER:-pkadmin}"
  DB_PASSWORD="${DB_PASSWORD:-pkadmin123}"
  SERVER_HTTP_PORT="${SERVER_HTTP_PORT:-5000}"
  SERVER_HTTPS_PORT="${SERVER_HTTPS_PORT:-5001}"
fi

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# ── Locate pg_ctl ─────────────────────────────────────
# 按数据目录的 PG_VERSION 匹配标准安装路径 → 回退 14 → 最后 PATH
PG_VERSION=$(cat "$PGDATA/PG_VERSION" 2>/dev/null || echo "14")
PG_CTL=""
for candidate in \
    "/usr/lib/postgresql/${PG_VERSION}/bin/pg_ctl" \
    "/usr/lib/postgresql/14/bin/pg_ctl" \
    ; do
    if [ -x "$candidate" ]; then
        PG_CTL="$candidate"
        break
    fi
done
if [ -z "$PG_CTL" ] && command -v pg_ctl > /dev/null 2>&1; then
    PG_CTL="pg_ctl"
fi
if [ -z "$PG_CTL" ]; then
    echo -e "${RED}[ERROR]${NC} pg_ctl not found (PG version: $PG_VERSION)"
    exit 1
fi

log()  { echo -e "${GREEN}[INFO]${NC}  $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC}  $1"; }
err()  { echo -e "${RED}[ERROR]${NC} $1"; }

# ── 端口释放 ──────────────────────────────────────────
free_port() {
    local port=$1
    local pid=$(fuser $port/tcp 2>/dev/null | cut -d: -f1)
    if [ -n "$pid" ]; then
        warn "   端口 $port 被进程 $pid 占用，正在释放..."
        kill $pid 2>/dev/null
        sleep 1
        if fuser $port/tcp > /dev/null 2>&1; then
            kill -9 $pid 2>/dev/null
            sleep 1
        fi
        log "   端口 $port 已释放"
    fi
}

# ── 停止 ──────────────────────────────────────────────
stop_all() {
    log "正在停止所有服务..."

    # 停止前端 Vite
    if pgrep -f "vite" > /dev/null 2>&1; then
        pkill -f "vite" 2>/dev/null && log "前端 (Vite) 已停止" || true
    fi

    # 停止后端 .NET
    if pgrep -f "PkManager.Server" > /dev/null 2>&1; then
        pkill -f "PkManager.Server" 2>/dev/null && log "后端 (.NET) 已停止" || true
    fi

    # 停止 PostgreSQL
    if pg_isready -h "${DB_HOST:-localhost}" -p "${DB_PORT:-5432}" > /dev/null 2>&1; then
        "$PG_CTL" -D "$PGDATA" stop -m fast 2>/dev/null && log "PostgreSQL 已停止" || true
    fi

    log "所有服务已停止。"
    exit 0
}

# ── 参数处理 ──────────────────────────────────────────
if [ "$1" = "--stop" ] || [ "$1" = "-s" ]; then
    stop_all
fi

PKHEX_CORE_VERSION="$(sed -n 's:.*<PKHeXCoreVersion>\(.*\)</PKHeXCoreVersion>.*:\1:p' "$PKHEX_PROPS" | head -n 1)"
PKHEX_CORE_PACKAGE="$PKHEX_FEED_DIR/PKHeX.Core.$PKHEX_CORE_VERSION.nupkg"

echo ""
echo -e "${CYAN}╔══════════════════════════════════════════════════╗${NC}"
echo -e "${CYAN}║     🐾 宝可梦全世代管理平台 — 开发环境启动       ║${NC}"
echo -e "${CYAN}╚══════════════════════════════════════════════════╝${NC}"
echo ""

# ── 1. PostgreSQL ─────────────────────────────────────
log "1/3 检查 PostgreSQL..."

if pg_isready -h "${DB_HOST:-localhost}" -p "${DB_PORT:-5432}" > /dev/null 2>&1; then
    log "   PostgreSQL 已在运行 ✅"
else
    log "   启动 PostgreSQL..."
    if "$PG_CTL" -D "$PGDATA" -l "$PG_LOG" start > /dev/null 2>&1; then
        sleep 1
        if pg_isready -h "${DB_HOST:-localhost}" -p "${DB_PORT:-5432}" > /dev/null 2>&1; then
            log "   PostgreSQL 启动成功 ✅"
        else
            err "   PostgreSQL 启动失败，请检查日志: $PG_LOG"
            exit 1
        fi
    else
        err "   PostgreSQL 启动失败，请检查日志: $PG_LOG"
        exit 1
    fi
fi

# ── 2. 后端 .NET 8 ────────────────────────────────────
log "2/3 启动后端 API..."

free_port 5000

if [ ! -f "$PKHEX_CORE_PACKAGE" ]; then
    err "   PKHeX.Core 本地包不存在: $PKHEX_CORE_PACKAGE"
    err "   请先运行: ./scripts/update-pkhex-core-package.sh"
    exit 1
fi

if [ ! -d "$SERVER_DIR" ]; then
    err "   后端目录不存在: $SERVER_DIR"
    exit 1
fi

cd "$SERVER_DIR"

# 后台启动，输出到终端
HTTP_PORT="${SERVER_HTTP_PORT:-5000}"
dotnet run --urls "http://0.0.0.0:${HTTP_PORT}" &
BACKEND_PID=$!
sleep 2

# 获取局域网 IP
LAN_IP=$(hostname -I 2>/dev/null | awk '{print $1}')
[ -z "$LAN_IP" ] && LAN_IP=$(ip -4 addr show scope global 2>/dev/null | grep inet | awk '{print $2}' | cut -d/ -f1 | head -1)
[ -z "$LAN_IP" ] && LAN_IP="localhost"

# 等待后端就绪
for i in $(seq 1 15); do
    if curl -s "http://localhost:${HTTP_PORT}/swagger/index.html" > /dev/null 2>&1; then
        log "   后端启动成功 ✅  http://${LAN_IP}:${HTTP_PORT}"
        log "    Swagger UI:     http://${LAN_IP}:${HTTP_PORT}/swagger"
        break
    fi
    if [ $i -eq 15 ]; then
        warn "   后端可能仍在启动中，请稍后检查 http://${LAN_IP}:5000/swagger"
    fi
    sleep 1
done

# ── 3. 前端 Vite ──────────────────────────────────────
log "3/3 启动前端开发服务器..."

free_port 5173

if [ ! -d "$CLIENT_DIR" ]; then
    err "   前端目录不存在: $CLIENT_DIR"
    exit 1
fi

cd "$CLIENT_DIR"

npm run dev &
FRONTEND_PID=$!
sleep 2

for i in $(seq 1 10); do
    if curl -ks https://localhost:5173 > /dev/null 2>&1; then
        log "   前端启动成功 ✅  https://${LAN_IP}:5173"
        break
    fi
    if [ $i -eq 10 ]; then
        warn "   前端可能仍在启动中，请稍后检查 https://${LAN_IP}:${VITE_DEV_PORT:-5173}"
    fi
    sleep 1
done

# ── 完成 ──────────────────────────────────────────────
echo ""
echo -e "${GREEN}╔══════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}║  🎉 开发环境已就绪！                              ║${NC}"
echo -e "${GREEN}╠══════════════════════════════════════════════════╣${NC}"
echo -e "${GREEN}║  📦 前端:   https://${LAN_IP}:${VITE_DEV_PORT:-5173}               ║${NC}"
echo -e "${GREEN}║  🔧 后端:   http://${LAN_IP}:${HTTP_PORT}                ║${NC}"
echo -e "${GREEN}║  📖 API文档: http://${LAN_IP}:${HTTP_PORT}/swagger        ║${NC}"
echo -e "${GREEN}║  🗄️  PG:     psql -h ${DB_HOST:-localhost} -p ${DB_PORT:-5432} -U ${DB_USER:-pkadmin}  ║${NC}"
echo -e "${GREEN}╠══════════════════════════════════════════════════╣${NC}"
echo -e "${GREEN}║  局域网内其他设备可通过上方地址访问              ║${NC}"
echo -e "${GREEN}║  按 Ctrl+C 停止所有服务                          ║${NC}"
echo -e "${GREEN}╚══════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "  数据库账号: ${YELLOW}${DB_USER:-pkadmin}${NC} / ${YELLOW}${DB_PASSWORD:-pkadmin123}${NC}"
echo ""

# ── 捕获 Ctrl+C 优雅退出 ─────────────────────────────
cleanup() {
    echo ""
    log "正在停止服务..."
    kill $BACKEND_PID 2>/dev/null || true
    kill $FRONTEND_PID 2>/dev/null || true
    # 不停止 PostgreSQL，保持数据库运行
    log "前后端已停止，PostgreSQL 保持运行。"
    log "使用 $0 --stop 可停止全部服务。"
    exit 0
}

trap cleanup SIGINT SIGTERM

# 等待子进程
wait
