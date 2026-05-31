# GBA 在线模拟器 — 技术方案设计

> **版本**: v1.0  
> **日期**: 2026-05-31  
> **模拟器核心**: mGBA (WebAssembly)  
> **目标**: 在网页端运行 GBA 宝可梦游戏，与存档管理系统深度集成

---

## 目录

1. [概述与目标](#1-概述与目标)
2. [系统架构](#2-系统架构)
3. [模拟器核心选型](#3-模拟器核心选型)
4. [ROM 管理](#4-rom-管理)
5. [存档联动](#5-存档联动)
6. [前端模拟器页面](#6-前端模拟器页面)
7. [键盘与手柄输入](#7-键盘与手柄输入)
8. [高级功能](#8-高级功能)
9. [数据库设计](#9-数据库设计)
10. [API 接口设计](#10-api-接口设计)
11. [文件结构](#11-文件结构)
12. [待办事项](#12-待办事项)

---

## 1. 概述与目标

在 pkmanager 平台中嵌入 GBA 在线模拟器，实现：

1. **网页端游玩 GBA 宝可梦游戏** — 基于 mGBA WebAssembly 核心
2. **ROM 集中管理** — 所有账号共用 ROM 库（仅宝可梦系列）
3. **存档自动联动** — 游戏内保存自动同步回存档管理系统
4. **画面缩放** — 支持 1× / 2× / 4× 切换
5. **加速功能** — 可切换加速模式
6. **金手指支持** — CodeBreaker / GameShark 格式
7. **即时存档** — 保存/恢复模拟器状态快照
8. **键盘 + 手柄** — 键盘默认映射 + Web Gamepad API

### 当前 ROM 库

| game_id | 游戏名称 | 文件大小 |
|---------|---------|---------|
| `pkm_ruby` | 宝可梦 红宝石 | 8.5 MB |
| `pkm_sapphire` | 宝可梦 蓝宝石 | 8.5 MB |
| `pkm_emerald` | 宝可梦 绿宝石 | 32 MB |
| `pkm_firered` | 宝可梦 火红 | 16 MB |
| `pkm_leafgreen` | 宝可梦 叶绿 | 16 MB |

---

## 2. 系统架构

```
┌─ 浏览器 (用户) ──────────────────────────────────────────┐
│                                                           │
│  ┌─ EmulatorPage (/play/:saveFileId) ──────────────────┐  │
│  │                                                      │  │
│  │  ┌─ Canvas (WebGL 2.0) ────────────────────────┐    │  │
│  │  │  mGBA WASM Core                              │    │  │
│  │  │  ┌──────────┐  ┌──────────┐  ┌──────────┐  │    │  │
│  │  │  │ ROM(.gba)│  │SRAM(.sav)│  │ Savestate│  │    │  │
│  │  │  └──────────┘  └──────────┘  └──────────┘  │    │  │
│  │  └─────────────────────────────────────────────┘    │  │
│  │                                                      │  │
│  │  ┌─ 控制栏 ────────────────────────────────────┐    │  │
│  │  │ [▶/⏸] [⏩加速] [1×/2×/4×] [💾即时存档] [↩读档]│    │  │
│  │  │ [60 FPS]                                      │    │  │
│  │  └───────────────────────────────────────────────┘    │  │
│  │                                                      │  │
│  │  键盘 ⌨: Z=A X=B A=L S=R Enter=Start Back=Select    │  │
│  │  手柄 🎮: Web Gamepad API                            │  │
│  └──────────────────────────────────────────────────────┘  │
│                                                           │
│  存档同步流程:                                             │
│  ┌─────────┐  每30秒自动  ┌──────────┐   POST /sync-save   │
│  │ SRAM    │ ──────────→ │ 浏览器    │ ─────────────────→ │
│  │ (游戏内) │             │ JS 层     │                    │
│  └─────────┘             └──────────┘                    │
└───────────────────────────────────────────────────────────┘
                                     │
                                     ▼
                          ┌──────────────────┐
                          │   后端 API        │
                          │                  │
                          │ GET /roms/{id}   │ ← 下载 ROM
                          │ POST /sync-save  │ ← 同步存档
                          │ POST /savestate  │ ← 即时存档
                          │ GET /savestate   │ ← 读档
                          │ GET /raw         │ ← 原始存档
                          └──────┬───────────┘
                                 │
                          ┌──────▼───────────┐
                          │   PostgreSQL      │
                          │                  │
                          │ rom_files        │ ← ROM 二进制
                          │ save_files       │ ← raw_save_data
                          │ emulator_save_   │ ← 即时存档
                          │   states         │
                          └──────────────────┘
```

---

## 3. 模拟器核心选型

### 选型对比

| 模拟器 | GBA 精度 | WASM 支持 | 性能 | 功能 |
|--------|---------|-----------|------|------|
| **mGBA** | ⭐⭐⭐⭐⭐ | ✅ 官方 Emscripten 端口 | 高 | 完整 |
| VBA-M | ⭐⭐⭐ | 可编译(非官方) | 中 | 基础 |
| SkyEmu | ⭐⭐⭐⭐ | ❌ | — | 先进 |
| IodineGBA | ⭐⭐⭐⭐ | ✅ Rust→WASM | 很高 | 纯 GBA |

### 选择 mGBA

- **最高精度** — 公认最准确的 GBA 模拟器
- **官方 WASM 构建** — mGBA 维护 Emscripten 目标
- **完整 API** — `mCoreLoadFile`, `mCoreRunFrame`, `mCoreGetSaveData` 等
- **JS 桥接** — 通过 Emscripten Module 直接控制
- **支持加速、金手指、即时存档/读档**

### mGBA WASM 文件部署

```
client/public/emulator/
├── mgba.wasm          # WebAssembly 二进制 (~3-5MB)
├── mgba.js            # Emscripten JS 胶水代码
└── mgba.worker.js     # (可选) Web Worker
```

来源: https://mgba.io/downloads.html → WebAssembly 版本

### API 封装 (client/src/lib/mgba.ts)

```typescript
interface MGBAEmulator {
  loadRom(data: Uint8Array): boolean;       // 加载 ROM
  runFrame(): void;                          // 运行一帧
  getScreen(): Uint8Array;                   // 获取画面 (RGBA 240×160×4)
  pressButton(button: number): void;         // 按下按键
  releaseButton(button: number): void;       // 释放按键
  getSaveData(): Uint8Array | null;          // 获取 SRAM
  loadSaveData(data: Uint8Array): void;      // 加载 SRAM
  setFastForward(enabled: boolean): void;    // 加速模式
  saveState(): Uint8Array;                   // 即时存档
  loadState(data: Uint8Array): boolean;      // 即时读档
  reset(): void;                             // 重置
}
```

### 按键常量

```typescript
MGBA_BUTTON = { A:0, B:1, SELECT:2, START:3, RIGHT:4, LEFT:5, UP:6, DOWN:7, R:8, L:9 }
```

---

## 4. ROM 管理

### 设计原则

- ROM **集中存储**在服务端 PostgreSQL `rom_files` 表
- **所有用户共享**同一套 ROM（宝可梦游戏仅有 5 个）
- ROM 不暴露下载链接（仅供模拟器 WASM 加载）
- 按 `game_id` 唯一标识

### ROM 上传

```
POST /api/Emulator/roms/upload
Content-Type: multipart/form-data

file: 口袋妖怪绿宝石汉化.gba
gameId: pkm_emerald
displayName: 宝可梦 绿宝石
generation: 3
```

### ROM 下载 (模拟器加载)

```
GET /api/Emulator/roms/pkm_emerald
→ 返回 raw GBA ROM 二进制
```

### GBA 世代 → ROM 映射

| 存档世代 | 默认 ROM | 说明 |
|---------|---------|------|
| Gen 3 | `pkm_emerald` | 绿宝石兼容红/蓝宝石、火红/叶绿存档 |
| Gen 1 (GBA) | `pkm_red` | 待上传 |
| Gen 2 (GBA) | `pkm_crystal` | 待上传 |

---

## 5. 存档联动

### 核心流程

```
存档管理页面 → 点击「游玩」按钮
  → 跳转 /play/{saveFileId}
  → EmulatorPage 加载:
    1. GET /api/SaveFile/{id}/raw     ← 下载存档原始二进制 (.sav)
    2. GET /api/Emulator/roms/{gameId} ← 下载对应 ROM (.gba)
    3. mGBA.loadRom(rom) + mGBA.loadSaveData(save) ← 开始模拟
  → 游戏中存档时:
    mGBA.getSaveData() → 获取 SRAM
    → 每 30 秒自动 POST /api/Emulator/sync-save ← 同步回服务端
    → UPDATE save_files SET raw_save_data = ... ← 写入存档管理
```

### 存档同步策略

| 触发方式 | 频率 | 说明 |
|---------|------|------|
| **自动定时** | 每 30 秒 | 游戏循环中检测 `getSaveData()` 有变化则同步 |
| **暂停时** | 暂停瞬间 | 用户点击暂停 → 立即同步 |
| **页面关闭前** | `beforeunload` 事件 | 防止数据丢失 |

### 存档格式

GBA 存档为原始 SRAM dump（64KB 或 128KB），存储为 `raw_save_data` 二进制。
存档可以直接载入 pkmanager 存档编辑器进行修改（未来支持）。

---

## 6. 前端模拟器页面

### 路由

```
/play/:saveFileId  →  EmulatorPage
```

### 页面布局

```
┌──────────────────────────────────────────────────────────┐
│ [← 返回] 宝可梦 绿宝石.sav  [绿宝石]   1×▼ [⏸暂停] [⏩加速] [💾即时存档] [↩读档] [60FPS] │
├──────────────────────────────────────────────────────────┤
│                                                          │
│                    ┌──────────────────┐                  │
│                    │                  │                  │
│                    │   GBA 游戏画面    │                  │
│                    │   (240×160)      │                  │
│                    │   缩放 1×/2×/4×  │                  │
│                    │                  │                  │
│                    └──────────────────┘                  │
│                                                          │
├──────────────────────────────────────────────────────────┤
│ 键盘: Z=A X=B A=L S=R Enter=Start Back=Select 方向键      │
│ F1=加速 F5=即时存档 F7=即时读档                          │
└──────────────────────────────────────────────────────────┘
```

### 画面缩放

| 倍数 | 分辨率 | 说明 |
|------|--------|------|
| 1× | 240×160 | GBA 原生分辨率 |
| 2× | 480×320 | 默认推荐 |
| 4× | 960×640 | 全屏大画面 |

CSS: `imageRendering: 'pixelated'` 保持像素风格。

### 控制栏按钮

| 按钮 | 功能 | 快捷键 |
|------|------|--------|
| ▶/⏸ 暂停/继续 | 切换模拟运行状态 | — |
| ⏩ 加速 | 解锁帧率限制 (240FPS) | F1 |
| 1×/2×/4× | 画面缩放切换 | — |
| 💾 即时存档 | 保存当前状态快照 | F5 |
| ↩ 即时读档 | 恢复上次状态快照 | F7 |
| FPS 指示器 | 实时帧率显示 | — |

---

## 7. 键盘与手柄输入

### 键盘默认映射

```
GBA 按键    键盘按键
─────────────────────
A           Z
B           X
L           A
R           S
Start       Enter
Select      Backspace
↑↓←→       方向键 (Arrow)
加速        F1
即时存档    F5
即时读档    F7
```

### 手柄支持 (Web Gamepad API)

```typescript
// 标准手柄映射
const GAMEPAD_MAP: Record<number, number> = {
  0: MGBA_BUTTON.A,      // A 按钮
  1: MGBA_BUTTON.B,      // B 按钮
  4: MGBA_BUTTON.L,      // 左肩键
  5: MGBA_BUTTON.R,      // 右肩键
  9: MGBA_BUTTON.START,  // Start
  8: MGBA_BUTTON.SELECT, // Select
  12: MGBA_BUTTON.UP,    // 十字键上
  13: MGBA_BUTTON.DOWN,  // 十字键下
  14: MGBA_BUTTON.LEFT,  // 十字键左
  15: MGBA_BUTTON.RIGHT, // 十字键右
};
```

轮询 `navigator.getGamepads()` 在前端游戏循环中检测手柄输入。

---

## 8. 高级功能

### 8.1 金手指 (Cheats)

mGBA 原生支持 CodeBreaker / GameShark 格式：

```typescript
interface CheatCode {
  name: string;
  code: string;  // "XXXXXXXX YYYYYYYY" 格式
  enabled: boolean;
}
```

前端 UI：金手指侧边栏或弹窗，可搜索/添加/启用/禁用。

### 8.2 即时存档 (Save States)

| 槽位 | 存储位置 |
|------|---------|
| #1-#5 | 服务端 `emulator_save_states` 表 |
| 快照 | 每个槽位保存完整模拟器状态 (~2-5MB) |

即时存档 API:
- `POST /api/Emulator/{saveFileId}/savestate/{slot}` — 保存
- `GET /api/Emulator/{saveFileId}/savestate/{slot}` — 加载

### 8.3 存档兼容性

GBA `.sav` 文件与 pkmanager 存档管理系统完全兼容：
- 上传的 `.sav` 文件可直接识别为 Gen3 存档
- 可在存档编辑器中查看/修改宝可梦数据
- 编辑后可直接载入模拟器继续游玩

### 8.4 未来扩展

- **NDS 模拟器** — 基于 melonDS/DeSmuME WASM（Gen4/5）
- **3DS 模拟器** — 基于 Citra WASM（Gen6/7）— 性能要求高
- **联机对战** — WebRTC 实现 GBA 联机线模拟
- **录屏/回放** — MediaRecorder API + 输入记录

---

## 9. 数据库设计

### rom_files

```sql
CREATE TABLE rom_files (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    game_id TEXT NOT NULL UNIQUE,        -- pkm_emerald, pkm_firered 等
    display_name TEXT NOT NULL,           -- 中文显示名
    generation INT NOT NULL DEFAULT 3,
    rom_data BYTEA NOT NULL,              -- GBA ROM 二进制
    file_size BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

### emulator_save_states

```sql
CREATE TABLE emulator_save_states (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    save_file_id UUID NOT NULL REFERENCES save_files(id) ON DELETE CASCADE,
    slot INT NOT NULL DEFAULT 1,          -- 槽位 1-5
    state_data BYTEA NOT NULL,            -- 模拟器状态快照
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(save_file_id, slot)
);
```

### GBA ROM 数据

| game_id | display_name | 文件大小 |
|---------|-------------|---------|
| pkm_ruby | 宝可梦 红宝石 | 8,465,280 |
| pkm_sapphire | 宝可梦 蓝宝石 | 8,518,208 |
| pkm_emerald | 宝可梦 绿宝石 | 33,554,432 |
| pkm_firered | 宝可梦 火红 | 16,777,216 |
| pkm_leafgreen | 宝可梦 叶绿 | 16,777,216 |

---

## 10. API 接口设计

| 方法 | 路径 | 说明 |
|------|------|------|
| `GET` | `/api/Emulator/roms` | 列出可用 ROM（名称、ID、大小） |
| `GET` | `/api/Emulator/roms/{gameId}` | 下载 ROM 二进制 |
| `POST` | `/api/Emulator/roms/upload` | 上传 ROM（管理员） |
| `POST` | `/api/Emulator/sync-save` | 同步存档（游戏内保存后自动调用） |
| `POST` | `/api/Emulator/{id}/savestate/{slot}` | 保存即时存档 |
| `GET` | `/api/Emulator/{id}/savestate/{slot}` | 加载即时存档 |
| `GET` | `/api/SaveFile/{id}/raw` | 下载原始存档二进制（供模拟器加载） |

---

## 11. 文件结构

```
server/PkManager.Server/
├── Controllers/
│   └── EmulatorController.cs          # ROM管理 + 存档同步 + 即时存档
├── Models/Entity/
│   └── SaveFile.cs                    # RomFileEntity, EmulatorSaveStateEntity

client/
├── src/
│   ├── pages/
│   │   ├── Emulator.tsx               # 模拟器主页面
│   │   └── Saves.tsx                  # 存档管理（「游玩」按钮入口）
│   ├── lib/
│   │   └── mgba.ts                    # mGBA WASM 封装 (MGBAEmulator 接口)
│   └── App.tsx                        # 路由: /play/:saveFileId
├── public/
│   └── emulator/                      # mGBA WASM 文件部署目录
│       ├── mgba.wasm                  # (待下载)
│       └── mgba.js                    # (待下载)

test-data/
└── roms/
    ├── 口袋妖怪红宝石汉化.gba
    ├── 口袋妖怪蓝宝石汉化.gba
    ├── 口袋妖怪绿宝石汉化.gba
    ├── 口袋妖怪火红汉化.gba
    └── 口袋妖怪叶绿汉化.gba
```

---

## 12. 待办事项

| # | 项目 | 状态 | 说明 |
|---|------|------|------|
| 1 | mGBA WASM 部署 | ⬜ | 下载 `mgba.wasm` + `mgba.js` 到 `public/emulator/` |
| 2 | mgba.ts 连接真实 WASM | ⬜ | 替换当前软件渲染器为 mGBA 核心 |
| 3 | 手柄支持 | ⬜ | Web Gamepad API 轮询 |
| 4 | 金手指 UI | ⬜ | 搜索/添加/启用 CodeBreaker 代码 |
| 5 | 即时存档 UI 完善 | ⬜ | 槽位选择器 + 截图预览 |
| 6 | ROM 自动匹配 | ⬜ | 根据存档识别对应 ROM（当前固定绿宝石） |
| 7 | GBA 存档编辑器集成 | ⬜ | 模拟器内可跳转编辑当前宝可梦 |
| 8 | 移动端触屏按键 | ⬜ | 虚拟按键叠加层 |
| 9 | GBA ROM 上传 UI | ⬜ | 管理后台 ROM 上传页面 |
| 10 | 性能优化 | ⬜ | Web Worker 分离模拟线程 |

---

> **相关文档**:
> - `docs/TODOLIST20260531.md` — 项目总体进度
> - `docs/PKHeX完整功能对比与缺口分析报告.md` — PKHeX 编辑功能对照
> - `docs/PKMDS-Blazor分析报告.md` — PKMDS-Blazor 参考
> - `docs/宝可梦全世代管理端-技术方案设计.md` — 原始技术设计
