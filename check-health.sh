#!/usr/bin/env bash
set -euo pipefail

# ── pkmanager Health Check Script ──────────────────────────────────
# Usage:
#   ./check-health.sh          # Full check (API + diagnostics + smoke tests)
#   ./check-health.sh --quick  # Quick check (API + diagnostics only)

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"

PASS="${GREEN}✓ PASS${NC}"
FAIL="${RED}✗ FAIL${NC}"
WARN="${YELLOW}⚠ WARN${NC}"

API_BASE="${PKM_API_URL:-http://localhost:5000}"
QUICK_MODE=false

if [[ "${1:-}" == "--quick" ]]; then
  QUICK_MODE=true
fi

echo ""
echo -e "${BLUE}══════════════════════════════════════════════════════${NC}"
echo -e "${BLUE}   pkmanager Health Check — $(date '+%Y-%m-%d %H:%M:%S')${NC}"
echo -e "${BLUE}   API: $API_BASE${NC}"
echo -e "${BLUE}══════════════════════════════════════════════════════${NC}"
echo ""

FAILURES=0

# ── 1. API Health ──────────────────────────────────────────────────

echo -e "${BLUE}[1/5]${NC} API 可达性检查..."
HEALTH_RESP=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/health" 2>/dev/null || echo "000")
if [[ "$HEALTH_RESP" == "200" ]]; then
  echo -e "  $PASS  API 可达 (HTTP $HEALTH_RESP)"
  HEALTH_BODY=$(curl -s "$API_BASE/api/health" 2>/dev/null || echo "{}")
  echo "  $(echo "$HEALTH_BODY" | python3 -m json.tool 2>/dev/null || echo "$HEALTH_BODY")"
else
  echo -e "  $FAIL  API 不可达 (HTTP $HEALTH_RESP)"
  FAILURES=$((FAILURES + 1))
fi

# ── 2. Backend Error Report ────────────────────────────────────────

echo ""
echo -e "${BLUE}[2/5]${NC} 后端异常日志 (最近24小时)..."
BE_RESP=$(curl -s "$API_BASE/api/diagnostics/backend-errors?hours=24" 2>/dev/null || echo "{}")
BE_TOTAL=$(echo "$BE_RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('data',{}).get('totalErrors',0))" 2>/dev/null || echo "?")
BE_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/diagnostics/backend-errors?hours=24" 2>/dev/null || echo "000")

if [[ "$BE_CODE" == "200" ]]; then
  if [[ "$BE_TOTAL" == "0" ]]; then
    echo -e "  $PASS  无后端异常"
  elif [[ "$BE_TOTAL" =~ ^[0-9]+$ ]] && [[ "$BE_TOTAL" -gt 0 ]]; then
    echo -e "  $FAIL  最近24小时有 $BE_TOTAL 条后端异常"
    echo "  $(echo "$BE_RESP" | python3 -m json.tool 2>/dev/null | head -20)"
    FAILURES=$((FAILURES + 1))
  else
    echo -e "  $PASS  后端诊断端点正常"
  fi
else
  echo -e "  $WARN  后端诊断端点返回 HTTP $BE_CODE"
fi

# ── 3. Client Error Report ─────────────────────────────────────────

echo ""
echo -e "${BLUE}[3/5]${NC} 客户端错误报告 (最近24小时)..."
REPORT_RESP=$(curl -s "$API_BASE/api/diagnostics/report?hours=24" 2>/dev/null || echo "{}")
TOTAL_ERRS=$(echo "$REPORT_RESP" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('data',{}).get('totalErrors',0))" 2>/dev/null || echo "?")
REPORT_CODE=$(curl -s -o /dev/null -w "%{http_code}" "$API_BASE/api/diagnostics/report?hours=24" 2>/dev/null || echo "000")

if [[ "$REPORT_CODE" == "200" ]]; then
  if [[ "$TOTAL_ERRS" == "0" ]]; then
    echo -e "  $PASS  无客户端错误"
  elif [[ "$TOTAL_ERRS" =~ ^[0-9]+$ ]] && [[ "$TOTAL_ERRS" -gt 0 ]]; then
    echo -e "  $WARN  最近24小时有 $TOTAL_ERRS 条客户端错误"
    echo "  $(echo "$REPORT_RESP" | python3 -m json.tool 2>/dev/null | head -20)"
  else
    echo -e "  $PASS  诊断端点正常"
  fi
else
  echo -e "  $WARN  诊断端点返回 HTTP $REPORT_CODE (可能尚未部署)"
fi

# ── 4. PostgreSQL Connection ───────────────────────────────────────

echo ""
echo -e "${BLUE}[4/5]${NC} 数据库连接检查..."
# Check via backend health (which should include DB check)
# For now, check if PostgreSQL socket exists
PG_SOCKET="${PGDATA:-$PROJECT_DIR/data/pgdata}/run"
if [[ -d "$PG_SOCKET" ]]; then
  echo -e "  $PASS  PostgreSQL 数据目录存在: $PG_SOCKET"
  if command -v psql &>/dev/null; then
    if psql -h "$PG_SOCKET" -U pkadmin -d pkmanager -c "SELECT 1" &>/dev/null 2>&1; then
      echo -e "  $PASS  数据库连接正常"
    else
      echo -e "  $WARN  数据库连接失败 (可能未启动或凭据变更)"
    fi
  else
    echo -e "  $WARN  psql 命令不可用，跳过连接测试"
  fi
else
  echo -e "  $WARN  PostgreSQL 数据目录不存在: $PG_SOCKET"
fi

# ── 5. Playwright Smoke Tests ──────────────────────────────────────

if $QUICK_MODE; then
  echo ""
  echo -e "${BLUE}[5/5]${NC} Playwright 冒烟测试 — ${YELLOW}跳过 (--quick模式)${NC}"
else
  echo ""
  echo -e "${BLUE}[5/5]${NC} Playwright 冒烟测试..."
  CLIENT_DIR="$(dirname "$0")/client"
  if [[ ! -d "$CLIENT_DIR" ]]; then
    echo -e "  $WARN  client/ 目录不存在，跳过"
  elif [[ ! -f "$CLIENT_DIR/node_modules/.bin/playwright" ]]; then
    echo -e "  $WARN  Playwright 未安装。运行: cd client && npm install && npx playwright install chromium"
  else
    cd "$CLIENT_DIR"
    if npx playwright test --reporter=line 2>&1; then
      echo -e "  $PASS  冒烟测试通过"
    else
      echo -e "  $WARN  冒烟测试有失败项 (检查上方输出)"
    fi
    cd - > /dev/null
  fi
fi

# ── Summary ────────────────────────────────────────────────────────

echo ""
echo -e "${BLUE}══════════════════════════════════════════════════════${NC}"
if [[ $FAILURES -eq 0 ]]; then
  echo -e "  ${GREEN}健康检查完成 — 核心服务正常${NC}"
else
  echo -e "  ${RED}健康检查完成 — $FAILURES 项失败${NC}"
fi
echo -e "${BLUE}══════════════════════════════════════════════════════${NC}"
echo ""

exit $FAILURES
