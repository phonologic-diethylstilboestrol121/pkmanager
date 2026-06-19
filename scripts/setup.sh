#!/usr/bin/env bash
# ================================================================
# 宝可梦全世代管理平台 — 一键部署脚本
# 用法:
#   git clone https://github.com/.../pkmanager.git
#   cd pkmanager
#   ./scripts/setup.sh
#
# 脚本全自动完成：依赖安装 → 配置初始化 → 证书 → 数据库 →
#   PKHeX 编译 → 后端构建 → 前端依赖 → 静态数据种子 → 启动
# ================================================================
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
SCRIPTS_DIR="$PROJECT_DIR/scripts"
CERTS_DIR="$PROJECT_DIR/certs"
SERVER_DIR="$PROJECT_DIR/server/PkManager.Server"
CLIENT_DIR="$PROJECT_DIR/client"
PGDATA="$PROJECT_DIR/data/pgdata"
PG_LOG="$PGDATA/logfile"
SDK_DIR="$PROJECT_DIR/sdk"
FEED_DIR="$PROJECT_DIR/server/artifacts/nuget"
CONFIG_FILE="$PROJECT_DIR/config"
CONFIG_TMPL="$PROJECT_DIR/config.dst"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# ── 进度计数 ──────────────────────────────────────────
STEP=0
TOTAL=10

step() {
    STEP=$((STEP + 1))
    echo ""
    echo -e "${CYAN}${BOLD}═══ [${STEP}/${TOTAL}] $1 ═══${NC}"
}

ok()   { echo -e "  ${GREEN}✅${NC} $1"; }
warn() { echo -e "  ${YELLOW}⚠️  $1${NC}"; }
err()  { echo -e "  ${RED}❌ $1${NC}"; }
info() { echo -e "  ${CYAN}ℹ${NC}  $1"; }

die() {
    echo ""
    echo -e "${RED}${BOLD}部署中断: $1${NC}"
    echo -e "修复后重新运行 ./scripts/setup.sh 即可继续。"
    exit 1
}

# ── 加载 config（优先 config，回退 config.dst） ────────
load_config() {
    if [[ -f "$CONFIG_FILE" ]]; then
        set -a; source "$CONFIG_FILE" 2>/dev/null || true; set +a
    elif [[ -f "$CONFIG_TMPL" ]]; then
        set -a; source "$CONFIG_TMPL" 2>/dev/null || true; set +a
    fi
    # 默认值
    DB_HOST="${DB_HOST:-localhost}"
    DB_PORT="${DB_PORT:-5432}"
    DB_NAME="${DB_NAME:-pkmanager}"
    DB_USER="${DB_USER:-pkadmin}"
    DB_PASSWORD="${DB_PASSWORD:-change-me}"
    CERT_PFX_PASSWORD="${CERT_PFX_PASSWORD:-change-me}"
    SERVER_HTTP_PORT="${SERVER_HTTP_PORT:-5000}"
    SERVER_HTTPS_PORT="${SERVER_HTTPS_PORT:-5001}"
}

load_config

# ================================================================
# Phase 1: 依赖检查 & 安装
# ================================================================
step "检查系统依赖"

detect_os() {
    if [[ -f /etc/os-release ]]; then
        . /etc/os-release
        OS_ID="${ID}"
    elif [[ "$(uname)" == "Darwin" ]]; then
        OS_ID="macos"
    else
        OS_ID="unknown"
    fi
}
detect_os

info "检测到系统: ${OS_ID}"

# ── 通用安装函数 ──────────────────────────────────────
install_pkg() {
    local pkg=$1
    case "$OS_ID" in
        ubuntu|debian)
            sudo apt-get update -qq && sudo apt-get install -y -qq "$pkg" ;;
        centos|rhel|fedora|rocky|almalinux)
            sudo dnf install -y -q "$pkg" 2>/dev/null || sudo yum install -y -q "$pkg" ;;
        macos)
            brew install "$pkg" 2>/dev/null || die "请先安装 Homebrew: https://brew.sh" ;;
        *)
            die "不支持的操作系统，请手动安装: $pkg" ;;
    esac
}

# ── git ────────────────────────────────────────────────
if command -v git &>/dev/null; then
    ok "git: $(git --version | head -1)"
else
    warn "git 未安装，正在安装..."
    install_pkg git
    ok "git 安装完成"
fi

# ── OpenSSL ────────────────────────────────────────────
if command -v openssl &>/dev/null; then
    ok "OpenSSL: $(openssl version | head -1)"
else
    warn "OpenSSL 未安装，正在安装..."
    install_pkg openssl
    ok "OpenSSL 安装完成"
fi

# ── .NET SDK ───────────────────────────────────────────
NET_SDK_NEEDED="10.0"
if command -v dotnet &>/dev/null; then
    NET_VER=$(dotnet --version 2>/dev/null || echo "0")
    ok ".NET SDK: ${NET_VER}"
    if [[ ! "$NET_VER" =~ ^10\. ]]; then
        warn "需要 .NET SDK ${NET_SDK_NEEDED}.x，当前为 ${NET_VER}"
        warn "请手动安装 .NET SDK 10.0: https://dotnet.microsoft.com/download"
    fi
else
    warn ".NET SDK 未安装"
    case "$OS_ID" in
        ubuntu|debian)
            info "正在安装 .NET SDK 10.0..."
            # 添加 Microsoft 源
            if [[ "$OS_ID" == "ubuntu" && "$(lsb_release -rs 2>/dev/null || echo '')" == "24.04" ]]; then
                wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/ms-prod.deb
                sudo dpkg -i /tmp/ms-prod.deb
                sudo apt-get update -qq
                sudo apt-get install -y -qq dotnet-sdk-10.0
            elif [[ "$OS_ID" == "ubuntu" && "$(lsb_release -rs 2>/dev/null || echo '')" == "22.04" ]]; then
                wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ms-prod.deb
                sudo dpkg -i /tmp/ms-prod.deb
                sudo apt-get update -qq
                sudo apt-get install -y -qq dotnet-sdk-10.0
            else
                die "请手动安装 .NET SDK 10.0: https://dotnet.microsoft.com/download"
            fi
            ok ".NET SDK 安装完成"
            ;;
        macos)
            brew install dotnet-sdk 2>/dev/null || die "请手动安装 .NET SDK 10.0: https://dotnet.microsoft.com/download"
            ok ".NET SDK 安装完成"
            ;;
        *)  die "请手动安装 .NET SDK 10.0: https://dotnet.microsoft.com/download" ;;
    esac
fi

# ── Node.js ────────────────────────────────────────────
if command -v node &>/dev/null; then
    NODE_VER=$(node --version)
    ok "Node.js: ${NODE_VER}"
else
    warn "Node.js 未安装，正在安装..."
    case "$OS_ID" in
        ubuntu|debian)
            curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - > /dev/null 2>&1
            sudo apt-get install -y -qq nodejs
            ;;
        macos)
            brew install node ;;
        *)
            die "请手动安装 Node.js: https://nodejs.org" ;;
    esac
    ok "Node.js 安装完成: $(node --version)"
fi

# ── PostgreSQL ─────────────────────────────────────────
PG_INSTALLED=false
if command -v pg_ctl &>/dev/null; then
    PG_INSTALLED=true
    PG_VER=$("$PG_CTL" --version 2>/dev/null | grep -oP '\d+' | head -1 || echo "?")
    ok "PostgreSQL: pg_ctl 已就绪 (version ${PG_VER})"
elif command -v pg_isready &>/dev/null; then
    PG_INSTALLED=true
    ok "PostgreSQL: 已安装"
else
    warn "PostgreSQL 未安装，正在安装..."
    case "$OS_ID" in
        ubuntu|debian)
            install_pkg postgresql postgresql-client
            ;;
        macos)
            brew install postgresql@14 ;;
        *)
            die "请手动安装 PostgreSQL 14+: https://www.postgresql.org/download/" ;;
    esac
    ok "PostgreSQL 安装完成"
    PG_INSTALLED=true
fi

# ── 定位 pg_ctl ────────────────────────────────────────
PG_CTL=""
for candidate in \
    "/usr/lib/postgresql/${PG_VER:-14}/bin/pg_ctl" \
    "/usr/lib/postgresql/14/bin/pg_ctl" \
    ; do
    if [[ -x "$candidate" ]]; then PG_CTL="$candidate"; break; fi
done
if [[ -z "$PG_CTL" ]] && command -v pg_ctl &>/dev/null; then
    PG_CTL="pg_ctl"
fi
if [[ -z "$PG_CTL" ]]; then
    die "未找到 pg_ctl。请确认 PostgreSQL 已正确安装。"
fi

ok "所有依赖就绪"

# ================================================================
# Phase 2: 配置文件初始化
# ================================================================
step "初始化配置文件"

if [[ -f "$CONFIG_FILE" ]]; then
    ok "config 文件已存在，跳过"
else
    info "从 config.dst 创建 config..."
    cp "$CONFIG_TMPL" "$CONFIG_FILE"

    # 自动生成 JWT_SECRET（至少 64 字符）
    JWT_SECRET=$(openssl rand -base64 64)
    sed -i "s/^JWT_SECRET=.*/JWT_SECRET=${JWT_SECRET}/" "$CONFIG_FILE"

    # 自动生成证书密码
    CERT_PW=$(openssl rand -base64 16)
    sed -i "s/^CERT_PFX_PASSWORD=.*/CERT_PFX_PASSWORD=${CERT_PW}/" "$CONFIG_FILE"

    # 数据库密码保持 config.dst 默认值 (change-me → pkadmin123)
    sed -i "s/^DB_PASSWORD=.*/DB_PASSWORD=pkadmin123/" "$CONFIG_FILE"

    ok "config 已创建（JWT_SECRET 已自动生成，DB_PASSWORD 已设为默认值）"
fi

# 重新加载 config
load_config

# ================================================================
# Phase 3: TLS 证书
# ================================================================
step "生成 TLS 证书"

if [[ -f "$CERTS_DIR/cert.pfx" ]]; then
    ok "证书已存在，跳过"
else
    "$SCRIPTS_DIR/generate-certs.sh"
    ok "证书生成完成"
fi

# ================================================================
# Phase 4: 数据库初始化
# ================================================================
step "初始化 PostgreSQL 数据库"

# ── 4.1 启动 PostgreSQL ────────────────────────────────
PG_RUNNING=false
if pg_isready -h "$DB_HOST" -p "$DB_PORT" &>/dev/null; then
    PG_RUNNING=true
    ok "PostgreSQL 已在运行 (${DB_HOST}:${DB_PORT})"
else
    # 尝试从 data/pgdata 启动
    if [[ -d "$PGDATA" ]]; then
        info "从 data/pgdata 启动 PostgreSQL..."
        "$PG_CTL" -D "$PGDATA" -l "$PG_LOG" start &>/dev/null && sleep 1 || true
        if pg_isready -h "$DB_HOST" -p "$DB_PORT" &>/dev/null; then
            PG_RUNNING=true
            ok "PostgreSQL 启动成功 (data/pgdata)"
        fi
    fi

    # 尝试 initdb
    if [[ "$PG_RUNNING" != true ]]; then
        info "初始化 data/pgdata..."
        mkdir -p "$PGDATA"
        # initdb 不指定 locale 以避免 ICU 依赖问题
        initdb -D "$PGDATA" --no-locale --encoding=UTF8 &>/dev/null 2>&1 || {
            warn "initdb 失败，尝试系统 PostgreSQL 服务..."
            case "$OS_ID" in
                ubuntu|debian)
                    sudo pg_ctlcluster 14 main start &>/dev/null 2>&1 || \
                    sudo pg_ctlcluster "$(pg_lsclusters -h 2>/dev/null | head -1 | awk '{print $1}')" "$(pg_lsclusters -h 2>/dev/null | head -1 | awk '{print $2}')" start &>/dev/null 2>&1 || \
                    sudo systemctl start postgresql &>/dev/null 2>&1 || true
                    sleep 1
                    ;;
                macos)
                    brew services start postgresql@14 &>/dev/null 2>&1 || true
                    sleep 1
                    ;;
            esac
            # 回退：使用系统 PG 服务
            DB_HOST="localhost"
            if pg_isready -h "$DB_HOST" -p "$DB_PORT" &>/dev/null; then
                PG_RUNNING=true
                ok "系统 PostgreSQL 服务已启动"
            fi
        }

        if [[ "$PG_RUNNING" != true ]]; then
            # initdb 成功后配置并启动
            # 允许 TCP localhost 连接
            cat >> "$PGDATA/postgresql.conf" <<'PGCONF'

# ── pkmanager setup ──
listen_addresses = 'localhost'
port = 5432
PGCONF
            "$PG_CTL" -D "$PGDATA" -l "$PG_LOG" start &>/dev/null && sleep 1 || true
            if pg_isready -h "localhost" -p "5432" &>/dev/null; then
                PG_RUNNING=true
                DB_HOST="localhost"
                DB_PORT="5432"
                ok "PostgreSQL 启动成功 (data/pgdata, localhost:5432)"
            fi
        fi
    fi
fi

if [[ "$PG_RUNNING" != true ]]; then
    die "无法启动 PostgreSQL。请确认已安装 PostgreSQL 14+ 并手动启动后重试。"
fi

# ── 4.2 创建数据库用户 ─────────────────────────────────
info "创建数据库用户与数据库..."

create_user_and_db() {
    # 尝试以当前用户连接（若已有 pkadmin 角色）
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres -c "SELECT 1" &>/dev/null 2>&1; then
        ok "用户 ${DB_USER} 已存在"
    else
        # 用 postgres 超级用户创建
        if sudo -u postgres psql -c "SELECT 1" &>/dev/null 2>&1; then
            sudo -u postgres psql -c "CREATE ROLE ${DB_USER} WITH LOGIN PASSWORD '${DB_PASSWORD}' CREATEDB;" 2>/dev/null || true
            ok "用户 ${DB_USER} 已创建"
        elif psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -c "SELECT 1" &>/dev/null 2>&1; then
            psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -c "CREATE ROLE ${DB_USER} WITH LOGIN PASSWORD '${DB_PASSWORD}' CREATEDB;" 2>/dev/null || true
            ok "用户 ${DB_USER} 已创建"
        else
            warn "无法创建用户 ${DB_USER}，请手动创建后重试。"
            warn "  sudo -u postgres psql -c \"CREATE ROLE ${DB_USER} WITH LOGIN PASSWORD '${DB_PASSWORD}' CREATEDB;\""
            return 1
        fi
    fi

    # 创建数据库
    if psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "SELECT 1" &>/dev/null 2>&1; then
        ok "数据库 ${DB_NAME} 已存在"
    else
        createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME" 2>/dev/null || \
        psql -h "$DB_HOST" -p "$DB_PORT" -U postgres -c "CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};" 2>/dev/null || \
        { warn "无法创建数据库 ${DB_NAME}"; return 1; }
        ok "数据库 ${DB_NAME} 已创建"
    fi
}

create_user_and_db || die "数据库用户/数据库创建失败，请检查 PostgreSQL 配置。"

# ── 4.3 初始化表结构 ───────────────────────────────────
info "初始化表结构..."
INIT_SQL="$SERVER_DIR/Data/init.sql"
if [[ -f "$INIT_SQL" ]]; then
    psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -f "$INIT_SQL" -q 2>&1 | tail -5 || true
    ok "init.sql 已执行"
else
    die "未找到 init.sql: $INIT_SQL"
fi

# 保存最终 DB 连接信息到 config（确保一致）
sed -i "s/^DB_HOST=.*/DB_HOST=${DB_HOST}/" "$CONFIG_FILE"
sed -i "s/^DB_PORT=.*/DB_PORT=${DB_PORT}/" "$CONFIG_FILE"

# ================================================================
# Phase 5: PKHeX.Core 编译
# ================================================================
step "编译 PKHeX.Core"

PKHEX_PROPS="$SERVER_DIR/Directory.Build.props"
PKHEX_VER=$(sed -n 's:.*<PKHeXCoreVersion>\(.*\)</PKHeXCoreVersion>.*:\1:p' "$PKHEX_PROPS" | head -n 1)
PKHEX_PKG="$FEED_DIR/PKHeX.Core.${PKHEX_VER}.nupkg"

if [[ -f "$PKHEX_PKG" ]]; then
    ok "PKHeX.Core ${PKHEX_VER} 已编译，跳过"
else
    # 克隆 PKHeX 源码
    if [[ -d "$SDK_DIR/PKHeX" ]]; then
        info "PKHeX 源码已存在，更新..."
        cd "$SDK_DIR/PKHeX"
        git pull --ff-only 2>/dev/null || warn "PKHeX 更新失败，使用现有源码继续"
        cd "$PROJECT_DIR"
    else
        info "克隆 PKHeX 源码..."
        mkdir -p "$SDK_DIR"
        git clone --depth 1 https://github.com/kwsch/PKHeX.git "$SDK_DIR/PKHeX" 2>&1 | tail -1 || \
            die "PKHeX 克隆失败，请检查网络连接。"
    fi

    # 编译
    info "编译 PKHeX.Core (Release)..."
    "$SCRIPTS_DIR/update-pkhex-core-package.sh"
    ok "PKHeX.Core ${PKHEX_VER} 编译完成"
fi

# ================================================================
# Phase 6: 后端构建
# ================================================================
step "构建后端 (.NET)"

cd "$SERVER_DIR"
info "dotnet restore..."
dotnet restore --verbosity quiet 2>&1 | tail -3 || die "dotnet restore 失败"

info "dotnet build..."
dotnet build --verbosity quiet 2>&1 | tail -3 || die "dotnet build 失败。请检查 .NET SDK 版本是否为 10.0.300+"

ok "后端构建成功"
cd "$PROJECT_DIR"

# ================================================================
# Phase 7: 前端依赖
# ================================================================
step "安装前端依赖"

cd "$CLIENT_DIR"
if [[ -d "node_modules" ]]; then
    ok "node_modules 已存在，跳过"
else
    info "npm install..."
    npm install --silent 2>&1 | tail -5 || die "npm install 失败"
    ok "前端依赖安装完成"
fi
cd "$PROJECT_DIR"

# ================================================================
# Phase 8: 种子静态数据
# ================================================================
step "种子静态数据 (res_* 表)"

# seed-static-data.sh 默认连接本地 PG Unix socket
# 改为通过 TCP 连接（与 DB_HOST/DB_PORT 一致）
info "导入宝可梦名称、招式、特性等静态数据 (all languages)..."
PGHOST="$DB_HOST" PGPORT="$DB_PORT" PGUSER="$DB_USER" PGDATABASE="$DB_NAME" \
    bash "$SCRIPTS_DIR/seed-static-data.sh" all 2>&1 | grep -E "INFO|导入|完成|跳过|已有" || true
ok "静态数据种子完成"

# ================================================================
# Phase 9: 启动服务
# ================================================================
step "启动开发环境"

info "启动所有服务..."
"$SCRIPTS_DIR/start-dev.sh"

# ================================================================
# Phase 10: 完成
# ================================================================
step "部署完成"

echo ""
echo -e "${GREEN}${BOLD}╔══════════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}${BOLD}║  🎉 宝可梦全世代管理平台 — 部署成功！                    ║${NC}"
echo -e "${GREEN}${BOLD}╠══════════════════════════════════════════════════════════╣${NC}"
echo -e "${GREEN}${BOLD}║                                                          ║${NC}"
echo -e "${GREEN}${BOLD}║  🌐 前端:     https://localhost:${VITE_DEV_PORT:-5173}                        ║${NC}"
echo -e "${GREEN}${BOLD}║  📖 API 文档: http://localhost:${SERVER_HTTP_PORT:-5000}/swagger                ║${NC}"
echo -e "${GREEN}${BOLD}║  🗄️  数据库:   psql -h ${DB_HOST} -p ${DB_PORT} -U ${DB_USER} -d ${DB_NAME}     ║${NC}"
echo -e "${GREEN}${BOLD}╠══════════════════════════════════════════════════════════╣${NC}"
echo -e "${GREEN}${BOLD}║                                                          ║${NC}"
echo -e "${GREEN}${BOLD}║  📝 后续步骤:                                            ║${NC}"
echo -e "${GREEN}${BOLD}║  1. 将游戏 ROM 文件放入 roms/ 目录                        ║${NC}"
echo -e "${GREEN}${BOLD}║  2. 编辑 config 文件，配置各游戏对应的 ROM 文件            ║${NC}"
echo -e "${GREEN}${BOLD}║  3. 管理员登录后在网页中导入已配置的 ROM                   ║${NC}"
echo -e "${GREEN}${BOLD}║  4. 在设置页面配置本地模拟器路径 (可选)                    ║${NC}"
echo -e "${GREEN}${BOLD}║                                                          ║${NC}"
echo -e "${GREEN}${BOLD}║  💡 首次访问时浏览器会提示证书不受信任（自签证书），        ║${NC}"
echo -e "${GREEN}${BOLD}║     点击「高级」→「继续访问」即可。                        ║${NC}"
echo -e "${GREEN}${BOLD}╚══════════════════════════════════════════════════════════╝${NC}"
echo ""
