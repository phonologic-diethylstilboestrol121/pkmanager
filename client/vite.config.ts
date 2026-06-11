import fs from 'fs'
import path from 'path'
import { fileURLToPath } from 'url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// ── 加载根目录 config 文件（KEY=VALUE 格式）───────────────
function loadConfig(): Record<string, string> {
  const configPath = path.resolve(__dirname, '../config')
  const result: Record<string, string> = {}
  try {
    const content = fs.readFileSync(configPath, 'utf-8')
    for (const line of content.split('\n')) {
      const trimmed = line.trim()
      if (!trimmed || trimmed.startsWith('#')) continue
      const eq = trimmed.indexOf('=')
      if (eq > 0) result[trimmed.slice(0, eq).trim()] = trimmed.slice(eq + 1).trim()
    }
  } catch { /* config 不存在时使用默认值 */ }
  return result
}

const cfg = loadConfig()

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: parseInt(cfg.VITE_DEV_PORT || '5173', 10),
    https: {
      key: fs.readFileSync(path.resolve(__dirname, '../certs/cert.key'), 'utf-8'),
      cert: fs.readFileSync(path.resolve(__dirname, '../certs/cert.crt'), 'utf-8'),
    },
    headers: {
      'Cross-Origin-Opener-Policy': 'same-origin',
      'Cross-Origin-Embedder-Policy': 'credentialless',
    },
    proxy: {
      '/api': {
        target: cfg.VITE_API_TARGET || 'https://localhost:5001',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
