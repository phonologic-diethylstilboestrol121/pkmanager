#!/bin/bash
# ============================================================
# F.1 静态数据种子脚本
# 用途: 从 PKHeX.Core 文本资源导入物种/招式/特性/性格/道具名称到 res_* 表
# 数据源: sdk/PKHeX/PKHeX.Core/Resources/text/ (PKHeX 嵌入式 .txt 文件)
# 用法:
#   ./scripts/seed-static-data.sh                  # 导入 zh-Hans (默认)
#   ./scripts/seed-static-data.sh zh-Hans          # 导入指定语言
#   ./scripts/seed-static-data.sh all              # 导入全部 10 种语言
#   ./scripts/seed-static-data.sh zh-Hans --force  # 强制重新导入
# ============================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PKHEX_TEXT="$PROJECT_DIR/sdk/PKHeX/PKHeX.Core/Resources/text"
DATA_DIR="$PROJECT_DIR/data/pgdata"

# PostgreSQL 连接参数
PGHOST="$DATA_DIR/run"
PGPORT="${PGPORT:-5432}"
PGUSER="${PGUSER:-pkadmin}"
PGDATABASE="${PGDATABASE:-pkmanager}"

# 参数解析
LANG="${1:-zh-Hans}"
FORCE=false

if [[ "${2:-}" == "--force" ]]; then
    FORCE=true
fi

# 全部 10 种语言
ALL_LANGS=(ja en fr it de es es-419 ko zh-Hans zh-Hant)

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log_info()  { echo -e "${GREEN}[INFO]${NC} $1"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# 检测表是否已有数据
check_table_empty() {
    local table=$1 lang=$2
    local count
    count=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -tAc \
        "SELECT COUNT(*) FROM $table WHERE lang = '$lang'")
    [[ "$count" -eq 0 ]]
}

# 导入单个文件到指定表
# 参数: table lang pkhex_subdir filename_pattern
# PKHeX 文本文件无表头，第 1 行即有效数据 (index 0)
# awk '{print NR-1 "\t" $0}' → 行号 1→id=0, 行号 2→id=1
seed_table() {
    local table=$1 lang=$2 subdir=$3 filename=$4
    local file="$PKHEX_TEXT/$subdir/$filename"

    if [[ ! -f "$file" ]]; then
        log_warn "文件不存在，跳过: $file"
        return 1
    fi

    if $FORCE; then
        log_info "清空 $table ($lang) ..."
        psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -q -c \
            "DELETE FROM $table WHERE lang = '$lang'"
    elif ! check_table_empty "$table" "$lang"; then
        log_info "$table ($lang) 已有数据，跳过 (--force 强制重导)"
        return 0
    fi

    log_info "导入: $file → $table ($lang)"
    # NF 过滤末尾空行（PKHeX 文本文件末尾可能有换行符）
    awk -v lang="$lang" 'NF {print NR-1 "\t" lang "\t" $0}' "$file" | \
        psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -q -c \
        "\COPY $table (id, lang, name) FROM STDIN WITH (FORMAT text)"

    local count
    count=$(psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -d "$PGDATABASE" -tAc \
        "SELECT COUNT(*) FROM $table WHERE lang = '$lang'")
    log_info "  → 已导入 $count 条"
}

# 导入单种语言
seed_lang() {
    local lang=$1
    local lang_dir

    # 语言代码 → 目录名映射 (PKHeX 内部使用 "zh" 指代 "zh-Hans")
    case "$lang" in
        zh-Hans) lang_dir="zh-Hans" ;;
        zh-Hant) lang_dir="zh-Hant" ;;
        *)       lang_dir="$lang" ;;
    esac

    echo ""
    log_info "======== 导入语言: $lang ========"

    seed_table "res_species"   "$lang" "other/$lang_dir" "text_Species_${lang_dir}.txt"
    seed_table "res_moves"     "$lang" "other/$lang_dir" "text_Moves_${lang_dir}.txt"
    seed_table "res_abilities" "$lang" "other/$lang_dir" "text_Abilities_${lang_dir}.txt"
    seed_table "res_natures"   "$lang" "other/$lang_dir" "text_Natures_${lang_dir}.txt"
    seed_table "res_items"     "$lang" "items"          "text_Items_${lang_dir}.txt"
}

# ── 主流程 ──────────────────────────────────────────────────

echo "============================================"
echo "  F.1 静态数据种子导入"
echo "  数据源: PKHeX.Core Resources/text/"
echo "  目标库: $PGUSER@$PGHOST:$PGPORT/$PGDATABASE"
echo "============================================"

if [[ "$LANG" == "all" ]]; then
    for lang in "${ALL_LANGS[@]}"; do
        seed_lang "$lang"
    done
else
    seed_lang "$LANG"
fi

echo ""
log_info "导入完成。验证:"
echo "  psql -h $PGHOST -p $PGPORT -U $PGUSER -d $PGDATABASE"
echo "  SELECT lang, COUNT(*) FROM res_species GROUP BY lang ORDER BY lang;"
