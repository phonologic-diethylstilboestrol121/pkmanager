#!/usr/bin/env bash
# ================================================================
# 宝可梦全世代管理平台 — 自签 TLS 证书生成脚本
# 用法: ./scripts/generate-certs.sh [--force]
#
# 生成文件:
#   certs/cert.key   — RSA 2048 私钥（Vite HTTPS）
#   certs/cert.crt   — X.509 自签证书（Vite HTTPS）
#   certs/cert.pfx   — PKCS#12 证书+私钥（Kestrel HTTPS）
#
# 密码来源: 项目根目录 config 中的 CERT_PFX_PASSWORD，
#           未配置时使用 "change-me"
# ================================================================
set -e

PROJECT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
CERTS_DIR="$PROJECT_DIR/certs"

# ── 加载根目录 config 文件 ────────────────────────────
CONFIG_FILE="$PROJECT_DIR/config"
if [[ -f "$CONFIG_FILE" ]]; then
  set -a
  source "$CONFIG_FILE"
  set +a
fi

CERT_PFX_PASSWORD="${CERT_PFX_PASSWORD:-change-me}"
FORCE=false
if [[ "${1:-}" == "--force" ]]; then
  FORCE=true
fi

# ── 已有证书处理 ──────────────────────────────────────
if [[ -f "$CERTS_DIR/cert.pfx" && "$FORCE" != true ]]; then
  echo "ℹ 证书已存在: $CERTS_DIR/cert.pfx"
  echo "  如需重新生成，请使用: ./scripts/generate-certs.sh --force"
  exit 0
fi

mkdir -p "$CERTS_DIR"

echo "🔑 生成 RSA 2048 私钥 + 自签证书（有效期 10 年）..."
openssl req -x509 -newkey rsa:2048 -nodes \
  -keyout "$CERTS_DIR/cert.key" \
  -out    "$CERTS_DIR/cert.crt" \
  -days   3650 \
  -subj   "/CN=localhost/O=PKManager Dev" \
  2>/dev/null

echo "📦 打包 PKCS#12 (cert.pfx)..."
openssl pkcs12 -export \
  -in       "$CERTS_DIR/cert.crt" \
  -inkey    "$CERTS_DIR/cert.key" \
  -out      "$CERTS_DIR/cert.pfx" \
  -passout  "pass:$CERT_PFX_PASSWORD" \
  2>/dev/null

chmod 600 "$CERTS_DIR/cert.key" "$CERTS_DIR/cert.pfx"
chmod 644 "$CERTS_DIR/cert.crt"

echo "✅ 证书生成完成"
echo "   cert.key  → $CERTS_DIR/cert.key"
echo "   cert.crt  → $CERTS_DIR/cert.crt"
echo "   cert.pfx  → $CERTS_DIR/cert.pfx  (password: $CERT_PFX_PASSWORD)"
