import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography, Table, Button, Upload, Popconfirm, App, Tag, Space, Card,
} from 'antd';
import { UploadOutlined, DeleteOutlined, EyeOutlined, FileAddOutlined, PlayCircleOutlined, DesktopOutlined, SettingOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { saveFileApi, emulatorApi, type SaveFileInfo, type LaunchLocalResult } from '../api/saveFile';
import { GAME_VERSION_DISPLAY, GENERATION_MAP } from '../constants/games';
import GameCover from '../components/GameCover';
import PageContainer from '../components/PageContainer';
import { launchLocalSave } from '../lib/localLaunch';

const { Text } = Typography;

const SavesPage: React.FC = () => {
  const [saves, setSaves] = useState<SaveFileInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [launchStates, setLaunchStates] = useState<Record<string, { pid: number; type: string; status: string }>>({});
  const navigate = useNavigate();
  const { message } = App.useApp();

  // ── 本地模拟器启动为浏览器端脚本下载方式，无需轮询 PID ──

  // ── 启动本地模拟器（浏览器端调起）────────────────────
  const handleLaunchLocal = async (record: SaveFileInfo) => {
    if (launchStates[record.saveFileId]) {
      message.warning('该存档的模拟器已在运行中');
      return;
    }
    const saveFileId = record.saveFileId;
    setLaunchStates(prev => ({ ...prev, [saveFileId]: { pid: 0, type: '', status: 'launching' } }));

    try {
      await launchLocalSave(saveFileId, message, record.filename);
      setLaunchStates(prev => {
        const next = { ...prev };
        delete next[saveFileId];
        return next;
      });
      return;
    } catch (err: any) {
      message.error(err?.message || err?.response?.data?.message || '启动失败');
      setLaunchStates(prev => {
        const next = { ...prev };
        delete next[saveFileId];
        return next;
      });
      return;
    }

    const isWin = navigator.platform?.toLowerCase().includes('win') ?? false;
    const isViteDev = window.location.port === '5173';
    const scriptBackendBase = isViteDev ? `http://${window.location.hostname}:5000` : window.location.origin;

    // ── 优先：尝试 pkmanager:// 协议处理器（一键启动）──
    try {
      const tokenRes = await emulatorApi.createLaunchToken(saveFileId);
      const token = (tokenRes.data as any).token as string;

      // 前端构造协议 URL。Vite 开发模式下走 HTTP 直连后端，避免 PowerShell 自签证书被拒
      const backendBase = encodeURIComponent(
        scriptBackendBase,
      );
      const protoUrl = `pkmanager://launch/${token}?backend=${backendBase}`;

      message.info('如果浏览器提示打开外部应用，请点击允许');

      const supported = await new Promise<boolean>((resolve) => {
        let done = false;

        const finish = (result: boolean) => {
          if (done) return;
          done = true;
          window.clearTimeout(timer);
          window.removeEventListener('blur', onBlur);
          window.removeEventListener('pagehide', onPageHide);
          document.removeEventListener('visibilitychange', onVisibilityChange);
          resolve(result);
        };

        const onBlur = () => finish(true);
        const onPageHide = () => finish(true);
        const onVisibilityChange = () => {
          if (document.visibilityState === 'hidden') finish(true);
        };

        const timer = window.setTimeout(() => finish(false), 2500);

        window.addEventListener('blur', onBlur, { once: true });
        window.addEventListener('pagehide', onPageHide, { once: true });
        document.addEventListener('visibilitychange', onVisibilityChange);

        const link = document.createElement('a');
        link.href = protoUrl;
        link.style.display = 'none';
        link.rel = 'noopener';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
      });

      if (supported) {
        message.success('正在启动模拟器...');
        setLaunchStates(prev => { const n = { ...prev }; delete n[saveFileId]; return n; });
        return;
      }
    } catch {
      // 协议不可用，走脚本下载回退
    }

    // ── 回退：下载启动脚本 ──────────────────────────────
    try {
      const res = await emulatorApi.launchLocal(saveFileId);
      const pkg = res.data as LaunchLocalResult;

      if (!pkg.romPath) {
        message.warning('未找到游戏 ROM/内容文件路径，请检查 Azahar 数据目录配置是否正确');
        setLaunchStates(prev => {
          const next = { ...prev };
          delete next[saveFileId];
          return next;
        });
        return;
      }

      // 构建启动脚本
      let scriptContent: string;
      let fileName: string;

      if (isWin) {
        // Windows: PowerShell 脚本（.ps1）
        // 先备份现有存档，再写入 pkmanager 存档，最后启动模拟器
        const escapedSavePath = pkg.emuSavePath?.replace(/"/g, '`"') ?? '';
        const escapedExe = pkg.exePath.replace(/"/g, '`"');
        const escapedRom = (pkg.romPath || '').replace(/"/g, '`"');
        const escapedSaveDir = pkg.saveDir.replace(/"/g, '`"');
        const titleIdLow = (pkg.titleIdLow || '').replace(/"/g, '`"');
        const escapedBackend = scriptBackendBase.replace(/"/g, '`"');
        const escapedSaveFileId = pkg.saveFileId.replace(/"/g, '`"');
        const escapedSyncToken = pkg.syncToken.replace(/"/g, '`"');

        // 检测路径是否在 SMB 网络盘上（盘符或 UNC 路径）
        const isNetworkPath = (p: string) => /^\\\\|^[A-Za-z]:/.test(p);

        scriptContent = `# pkmanager — 本地启动 Azahar/DeSmuME 脚本
# 由 pkmanager 自动生成，请以 PowerShell 运行此脚本
# 路径指向: ${isNetworkPath(escapedSavePath) ? '网络盘 (SMB/NAS)，请确保网络连接正常' : '本地盘'}

$ErrorActionPreference = "Stop"
trap {
    Write-Host "[ERROR] Unexpected failure: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

function Resolve-AzaharTitleIdLow([string]$titleIdLow, [string]$fallbackRomPath) {
    if ($titleIdLow) {
        return $titleIdLow.ToUpperInvariant()
    }
    if ($fallbackRomPath -match 'title[\\\\/]+00040000[\\\\/]+([0-9A-Fa-f]{8})[\\\\/]content') {
        return $Matches[1].ToUpperInvariant()
    }
    return ''
}

function Resolve-AzaharSdmcPath([string]$dataDir) {
    $trimmed = $dataDir.TrimEnd('\\', '/')
    if ($trimmed.EndsWith('sdmc', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $trimmed
    }
    return Join-Path $trimmed 'sdmc'
}

function Get-BigEndianUInt32([byte[]]$bytes, [int]$offset) {
    return [uint32]((([uint32]$bytes[$offset]) -shl 24) -bor (([uint32]$bytes[$offset + 1]) -shl 16) -bor (([uint32]$bytes[$offset + 2]) -shl 8) -bor ([uint32]$bytes[$offset + 3]))
}

function Get-BigEndianUInt16([byte[]]$bytes, [int]$offset) {
    return [uint16]((([uint16]$bytes[$offset]) -shl 8) -bor ([uint16]$bytes[$offset + 1]))
}

function Quote-ProcessArgument([string]$value) {
    if (-not $value) {
        return $value
    }
    return '"' + $value.Replace('"', '\"') + '"'
}

function Get-BytesSha256Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    } finally {
        $sha.Dispose()
    }
}

function Get-TmdSignatureSize([uint32]$signatureType) {
    switch ($signatureType) {
        0x10000 { return 0x200 }
        0x10003 { return 0x200 }
        0x10001 { return 0x100 }
        0x10004 { return 0x100 }
        0x10002 { return 0x3C }
        0x10005 { return 0x3C }
        default { throw ("未知 TMD 签名类型: 0x{0:X8}" -f $signatureType) }
    }
}

function Resolve-AzaharContentPath([string]$dataDir, [string]$titleIdLow, [string]$fallbackRomPath) {
    $resolvedTitleIdLow = Resolve-AzaharTitleIdLow $titleIdLow $fallbackRomPath
    if (-not $resolvedTitleIdLow) {
        if ($fallbackRomPath) {
            return $fallbackRomPath
        }
        throw "缺少 3DS 标题 ID，无法定位 Azahar 内容文件"
    }

    $contentDir = Join-Path (Resolve-AzaharSdmcPath $dataDir) "Nintendo 3DS"
    $contentDir = Join-Path $contentDir "00000000000000000000000000000000"
    $contentDir = Join-Path $contentDir "00000000000000000000000000000000"
    $contentDir = Join-Path $contentDir "title"
    $contentDir = Join-Path $contentDir "00040000"
    $contentDir = Join-Path $contentDir $resolvedTitleIdLow
    $contentDir = Join-Path $contentDir "content"

    if (-not (Test-Path $contentDir -ErrorAction SilentlyContinue)) {
        if ($fallbackRomPath -and (Test-Path $fallbackRomPath -ErrorAction SilentlyContinue)) {
            Write-Host "[WARN] Azahar 内容目录不可达，回退到后端返回路径" -ForegroundColor Yellow
            return $fallbackRomPath
        }
        throw "Azahar 内容目录不可达: $contentDir"
    }

    $tmdFile = Get-ChildItem -LiteralPath $contentDir -Filter *.tmd -File -ErrorAction Stop |
        Sort-Object Name |
        Select-Object -First 1
    if (-not $tmdFile) {
        if ($fallbackRomPath -and (Test-Path $fallbackRomPath -ErrorAction SilentlyContinue)) {
            Write-Host "[WARN] 未找到 TMD，回退到后端返回路径" -ForegroundColor Yellow
            return $fallbackRomPath
        }
        throw "内容目录下未找到 TMD: $contentDir"
    }

    try {
        $bytes = [IO.File]::ReadAllBytes($tmdFile.FullName)
        if ($bytes.Length -lt 4) {
            throw "TMD 文件损坏或过小: $($tmdFile.FullName)"
        }

        $signatureType = Get-BigEndianUInt32 $bytes 0
        $signatureSize = Get-TmdSignatureSize $signatureType
        $bodyStart = [int]([Math]::Ceiling(($signatureSize + 4) / 64.0) * 64)
        $chunkBase = $bodyStart + 0x9C4
        if ($bytes.Length -lt ($chunkBase + 4)) {
            throw "TMD 文件被截断: $($tmdFile.FullName)"
        }

        $contentCount = Get-BigEndianUInt16 $bytes ($bodyStart + 0x9E)
        if ($contentCount -lt 1) {
            throw "TMD 中没有可启动内容: $($tmdFile.FullName)"
        }

        if ($contentCount -gt 1 -and $bytes.Length -ge ($chunkBase + 0x38)) {
            $secondContentType = Get-BigEndianUInt16 $bytes ($chunkBase + 0x30 + 6)
            if (($secondContentType -band 0x4000) -ne 0) {
                $contentDir = Join-Path $contentDir "00000000"
            }
        }

        $contentId = Get-BigEndianUInt32 $bytes $chunkBase
        return Join-Path $contentDir ("{0:x8}.app" -f $contentId)
    } catch {
        if ($fallbackRomPath -and (Test-Path $fallbackRomPath -ErrorAction SilentlyContinue)) {
            Write-Host "[WARN] 解析 TMD 失败，回退到后端返回路径: $($_.Exception.Message)" -ForegroundColor Yellow
            return $fallbackRomPath
        }
        throw
    }
}

$saveDataBase64 = @"
${pkg.saveDataBase64}
"@

$emuSavePath = "${escapedSavePath}"
$exePath = "${escapedExe}"
$fallbackRomPath = "${escapedRom}"
$saveDir = "${escapedSaveDir}"
$type = "${pkg.type}"
$titleIdLow = "${titleIdLow}"
$backendBase = "${escapedBackend}"
$saveFileId = "${escapedSaveFileId}"
$syncToken = "${escapedSyncToken}"
$romPath = if ($type -eq "azahar") {
    Resolve-AzaharContentPath $saveDir $titleIdLow $fallbackRomPath
} else {
    $fallbackRomPath
}
$launchArgs = if ($romPath) { @(Quote-ProcessArgument $romPath) } else { @() }

# ── 0. 预检：路径可达性 ──────────────────────────────────
Write-Host "[pkmanager] 预检路径可达性..."
if ($titleIdLow) { Write-Host "[pkmanager] 标题 ID: $titleIdLow" }
Write-Host "[pkmanager] 内容路径: $romPath"
if ($type -eq "azahar" -and $fallbackRomPath -and $fallbackRomPath -ne $romPath) {
    Write-Host "[pkmanager] 已根据 TMD 解析真实内容文件，未使用后端猜测路径" -ForegroundColor Yellow
}

# 检测可执行文件所在盘是否在线
$exeDrive = Split-Path $exePath -Qualifier
if ($exeDrive -and (Get-PSDrive -Name $exeDrive.TrimEnd(':') -ErrorAction SilentlyContinue)) {
    if (-not (Test-Path $exeDrive\\ -ErrorAction SilentlyContinue)) {
        Write-Host "[ERROR] 磁盘 $exeDrive 不可访问！可能是网络盘未连接或 SMB 共享断开。" -ForegroundColor Red
        Write-Host "  请检查: 1) VPN/网络连接 2) SMB 共享是否在线 3) 资源管理器是否能打开 $exeDrive" -ForegroundColor Yellow
        Read-Host "按 Enter 退出"
        exit 1
    }
}

# 检测 ROM/内容文件所在目录
$romDir = Split-Path $romPath -Parent
if (-not (Test-Path $romDir -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] ROM/内容文件目录不可达: $romDir" -ForegroundColor Red
    Write-Host "  可能是 SMB 网络盘未挂载，或 Azahar 数据目录配置有误。" -ForegroundColor Yellow
    Write-Host "  请前往 pkmanager 设置页 (齿轮图标) 检查 azahar.data_dir 配置。" -ForegroundColor Yellow
    Read-Host "按 Enter 退出"
    exit 1
}

# 检测存档目录
$saveParent = Split-Path $emuSavePath -Parent
if (-not (Test-Path $saveParent -ErrorAction SilentlyContinue)) {
    Write-Host "[WARN] 存档父目录不存在，尝试创建: $saveParent" -ForegroundColor Yellow
    try {
        New-Item -ItemType Directory -Force -Path $saveParent -ErrorAction Stop | Out-Null
        Write-Host "[pkmanager] 已创建存档目录" -ForegroundColor Green
    } catch {
        Write-Host "[ERROR] 无法创建存档目录: $saveParent" -ForegroundColor Red
        Write-Host "  可能原因: 网络盘权限不足 / 路径不存在 / SMB 共享只读" -ForegroundColor Yellow
        Read-Host "按 Enter 退出"
        exit 1
    }
}

Write-Host "[pkmanager] 路径预检通过" -ForegroundColor Green

# ── 1. 备份现有模拟器存档 ──────────────────────────────────
$backupDir = if ($type -eq "azahar") {
    Join-Path $saveDir "pkmanager_backup" | Join-Path -ChildPath $titleIdLow
} else {
    Join-Path $saveDir "pkmanager_backup"
}
try {
    New-Item -ItemType Directory -Force -Path $backupDir -ErrorAction Stop | Out-Null
} catch {
    Write-Host "[ERROR] 无法创建备份目录: $backupDir (可能是网络盘权限问题)" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

$backupFile = if ($type -eq "azahar") {
    Join-Path $backupDir "main.bak"
} else {
    Join-Path $backupDir "save.dsv.bak"
}
$hadExistingSave = Test-Path $emuSavePath -ErrorAction SilentlyContinue
$backupReady = $false
if ($hadExistingSave) {
    try {
        Copy-Item $emuSavePath $backupFile -Force -ErrorAction Stop
        $backupReady = $true
        $timestamp = Get-Date -Format "o"
        Set-Content (Join-Path $backupDir "injected_at.txt") $timestamp -ErrorAction SilentlyContinue
        Write-Host "[pkmanager] 已备份本地存档 → $backupFile" -ForegroundColor Green
    } catch {
        Write-Host "[WARN] 备份失败（可继续，但原存档将丢失）: $_" -ForegroundColor Yellow
    }
} else {
    Write-Host "[pkmanager] 未检测到现有存档，这是首次启动"
}

# ── 2. 写入 pkmanager 存档到模拟器目录 ─────────────────────
try {
    $saveBytes = [Convert]::FromBase64String(($saveDataBase64 -replace "\\s", ""))
    $expectedHash = Get-BytesSha256Hex $saveBytes
    Write-Host "[pkmanager] 存档字节数: $($saveBytes.Length)"
    [IO.File]::WriteAllBytes($emuSavePath, $saveBytes)
    $writtenBytes = [IO.File]::ReadAllBytes($emuSavePath)
    $actualHash = Get-BytesSha256Hex $writtenBytes
    if ($writtenBytes.Length -ne $saveBytes.Length -or $actualHash -ne $expectedHash) {
        Write-Host "[ERROR] 存档写入后校验失败" -ForegroundColor Red
        Write-Host "  期望: $($saveBytes.Length) 字节, SHA256=$expectedHash" -ForegroundColor Yellow
        Write-Host "  实际: $($writtenBytes.Length) 字节, SHA256=$actualHash" -ForegroundColor Yellow
        Read-Host "按 Enter 退出"
        exit 1
    }
    Write-Host "[pkmanager] 存档校验通过: $actualHash" -ForegroundColor Green
    Write-Host "[pkmanager] 已注入存档 → $emuSavePath" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] 写入存档失败: $_" -ForegroundColor Red
    Write-Host "  可能原因: 目标目录在 SMB 网络盘上且权限不足 / 磁盘已满" -ForegroundColor Yellow
    Read-Host "按 Enter 退出"
    exit 1
}

# ── 3. 启动模拟器 ──────────────────────────────────────────
if (-not (Test-Path $exePath -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] 模拟器可执行文件不存在: $exePath" -ForegroundColor Red
    Write-Host "  如果是网络盘路径，请确认 SMB 共享是否在线。" -ForegroundColor Yellow
    Write-Host "  请前往 pkmanager 设置页检查路径配置。" -ForegroundColor Yellow
    Read-Host "按 Enter 退出"
    exit 1
}

Write-Host "[pkmanager] 启动模拟器: $exePath $($launchArgs -join ' ')" -ForegroundColor Green
$process = Start-Process -FilePath $exePath -ArgumentList $launchArgs -PassThru
if ($null -eq $process) {
    Write-Host "[ERROR] 模拟器进程启动失败" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}
Write-Host "[pkmanager] 模拟器已启动 (PID: $($process.Id))，等待退出..." -ForegroundColor Green
$process.WaitForExit()
Write-Host "[pkmanager] 模拟器已退出，返回码: $($process.ExitCode)"

if (-not $saveFileId -or -not $syncToken) {
    Write-Host "[WARN] 缺少同步 token，跳过自动同步" -ForegroundColor Yellow
} elseif (-not (Test-Path $emuSavePath -ErrorAction SilentlyContinue)) {
    Write-Host "[WARN] 未找到退出后的存档文件，跳过同步: $emuSavePath" -ForegroundColor Yellow
} else {
    $syncUrl = "$backendBase/api/Emulator/sync-save/$saveFileId?token=$([Uri]::EscapeDataString($syncToken))"
    $syncBytes = [IO.File]::ReadAllBytes($emuSavePath)
    Write-Host "[pkmanager] 正在回传存档..."
    Write-Host "[pkmanager] 同步字节数: $($syncBytes.Length)"
    Write-Host "[pkmanager] POST $syncUrl"
    try {
        if ($backendBase -match "localhost") {
            [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        }
        $syncResponse = Invoke-RestMethod -Uri $syncUrl -Method Post -ContentType "application/octet-stream" -Body $syncBytes -TimeoutSec 60
        if ($syncResponse.code -eq 0) {
            Write-Host "[pkmanager] 存档回传成功" -ForegroundColor Green
            if ($backupReady -and (Test-Path $backupFile -ErrorAction SilentlyContinue)) {
                try {
                    Copy-Item $backupFile $emuSavePath -Force -ErrorAction Stop
                    Write-Host "[pkmanager] 已恢复本机旧存档" -ForegroundColor Green
                } catch {
                    Write-Host "[WARN] 恢复本机旧存档失败: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            } elseif ($type -eq "desmume" -and -not $hadExistingSave -and (Test-Path $emuSavePath -ErrorAction SilentlyContinue)) {
                try {
                    Remove-Item $emuSavePath -Force -ErrorAction Stop
                    Write-Host "[pkmanager] 已清理临时注入存档（首次启动）" -ForegroundColor Green
                } catch {
                    Write-Host "[WARN] 清理临时注入存档失败: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            } else {
                Write-Host "[pkmanager] 无需恢复本机旧存档" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[WARN] 后端返回非 0 状态: $($syncResponse.message)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "[WARN] 自动同步失败: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Read-Host "按 Enter 退出"
`;
        fileName = `pkmanager_launch_${record.filename.replace(/\.[^.]+$/, '')}.ps1`;
      } else {
        // Linux/macOS: bash 脚本
        const escapedSavePath = (pkg.emuSavePath || '').replace(/'/g, "'\\''");
        const escapedExe = pkg.exePath.replace(/'/g, "'\\''");
        const escapedRom = (pkg.romPath || '').replace(/'/g, "'\\''");
        const escapedSaveDir = pkg.saveDir.replace(/'/g, "'\\''");
        const titleIdLow = (pkg.titleIdLow || '').replace(/'/g, "'\\''");
        const escapedBackend = scriptBackendBase.replace(/'/g, "'\\''");
        const escapedSaveFileId = pkg.saveFileId.replace(/'/g, "'\\''");
        const escapedSyncToken = pkg.syncToken.replace(/'/g, "'\\''");

        scriptContent = `#!/bin/bash
# pkmanager — 本地启动 Azahar/DeSmuME 脚本
# 如果路径指向 NFS/SMB 挂载点，请确保网络连接正常

SAVE_DATA_BASE64='${pkg.saveDataBase64}'
EMU_SAVE_PATH='${escapedSavePath}'
EXE_PATH='${escapedExe}'
ROM_PATH='${escapedRom}'
SAVE_DIR='${escapedSaveDir}'
TYPE='${pkg.type}'
TITLE_ID_LOW='${titleIdLow}'
BACKEND_BASE='${escapedBackend}'
SAVE_FILE_ID='${escapedSaveFileId}'
SYNC_TOKEN='${escapedSyncToken}'

# ── 0. 预检：路径可达性 ──────────────────────────────────
echo "[pkmanager] 预检路径可达性..."

# 检查可执行文件
if [ ! -f "\$EXE_PATH" ]; then
    echo "[ERROR] 模拟器可执行文件不存在: \$EXE_PATH"
    echo "  如果是 NFS/SMB 挂载路径，请检查: mount | grep \$(dirname \"\$EXE_PATH\")"
    echo "  请前往 pkmanager 设置页检查路径配置。"
    read -r -p "按 Enter 退出"
    exit 1
fi

# 检查 ROM/内容文件所在目录
ROM_DIR="\$(dirname "\$ROM_PATH")"
if [ ! -d "\$ROM_DIR" ]; then
    echo "[ERROR] ROM 目录不可达: \$ROM_DIR"
    echo "  可能是 NFS/SMB 挂载点未连接，或 Azahar 数据目录配置有误。"
    echo "  请检查: mount | grep \$ROM_DIR"
    read -r -p "按 Enter 退出"
    exit 1
fi

echo "[pkmanager] 路径预检通过"

# ── 1. 备份现有模拟器存档 ──────────────────────────────────
if [ "\$TYPE" = "azahar" ]; then
    BACKUP_DIR="\$SAVE_DIR/pkmanager_backup/\$TITLE_ID_LOW"
    BACKUP_FILE="\$BACKUP_DIR/main.bak"
else
    BACKUP_DIR="\$SAVE_DIR/pkmanager_backup"
    BACKUP_FILE="\$BACKUP_DIR/save.dsv.bak"
fi
HAD_EXISTING_SAVE=0
[ -f "\$EMU_SAVE_PATH" ] && HAD_EXISTING_SAVE=1
BACKUP_READY=0
mkdir -p "\$BACKUP_DIR" 2>/dev/null || {
    echo "[ERROR] 无法创建备份目录: \$BACKUP_DIR (可能是网络盘权限问题)"
    read -r -p "按 Enter 退出"
    exit 1
}
if [ -f "\$EMU_SAVE_PATH" ]; then
    cp "\$EMU_SAVE_PATH" "\$BACKUP_FILE" 2>/dev/null && BACKUP_READY=1 && echo "[pkmanager] 已备份本地存档 → \$BACKUP_FILE" || echo "[WARN] 备份失败（可继续）"
    date -Iseconds > "\$BACKUP_DIR/injected_at.txt" 2>/dev/null
else
    echo "[pkmanager] 未检测到现有存档，这是首次启动"
fi

# ── 2. 写入 pkmanager 存档到模拟器目录 ─────────────────────
mkdir -p "\$(dirname "\$EMU_SAVE_PATH")" 2>/dev/null || {
    echo "[ERROR] 无法创建存档父目录: \$(dirname \"\$EMU_SAVE_PATH\")"
    read -r -p "按 Enter 退出"
    exit 1
}
echo "\$SAVE_DATA_BASE64" | base64 -d > "\$EMU_SAVE_PATH" 2>/dev/null || {
    echo "[ERROR] 写入存档失败，可能原因: 网络盘权限不足 / 磁盘已满"
    read -r -p "按 Enter 退出"
    exit 1
}
echo "[pkmanager] 已注入存档 → \$EMU_SAVE_PATH"

# ── 3. 启动模拟器 ──────────────────────────────────────────
echo "[pkmanager] 启动模拟器: \$EXE_PATH \$ROM_PATH"
"\$EXE_PATH" "\$ROM_PATH"
EXIT_CODE=$?
echo "[pkmanager] 模拟器已退出，返回码: \$EXIT_CODE"

if [ -z "\$SAVE_FILE_ID" ] || [ -z "\$SYNC_TOKEN" ]; then
    echo "[WARN] 缺少同步 token，跳过自动同步"
elif [ ! -f "\$EMU_SAVE_PATH" ]; then
    echo "[WARN] 未找到退出后的存档文件，跳过同步: \$EMU_SAVE_PATH"
else
    SYNC_URL="\$BACKEND_BASE/api/Emulator/sync-save/\$SAVE_FILE_ID?token=\$SYNC_TOKEN"
    echo "[pkmanager] 正在回传存档..."
    echo "[pkmanager] 同步字节数: \$(wc -c < "\$EMU_SAVE_PATH")"
    echo "[pkmanager] POST \$SYNC_URL"
    if curl -fsS -X POST -H "Content-Type: application/octet-stream" --data-binary "@\$EMU_SAVE_PATH" "\$SYNC_URL" >/tmp/pkmanager_sync.log 2>/tmp/pkmanager_sync.err; then
        echo "[pkmanager] 存档回传成功"
        if [ "\$BACKUP_READY" = "1" ] && [ -f "\$BACKUP_FILE" ]; then
            cp "\$BACKUP_FILE" "\$EMU_SAVE_PATH" 2>/dev/null && echo "[pkmanager] 已恢复本机旧存档" || echo "[WARN] 恢复本机旧存档失败"
        elif [ "\$TYPE" = "desmume" ] && [ "\$HAD_EXISTING_SAVE" = "0" ] && [ -f "\$EMU_SAVE_PATH" ]; then
            rm -f "\$EMU_SAVE_PATH" && echo "[pkmanager] 已清理临时注入存档（首次启动）" || echo "[WARN] 清理临时注入存档失败"
        else
            echo "[pkmanager] 无需恢复本机旧存档"
        fi
    else
        echo "[WARN] 自动同步失败"
        cat /tmp/pkmanager_sync.err 2>/dev/null
    fi
fi

echo "[pkmanager] 按 Enter 退出此窗口..."
read -r
`;
        fileName = `pkmanager_launch_${record.filename.replace(/\.[^.]+$/, '')}.sh`;
      }

      // 触发浏览器下载脚本
      const blob = new Blob([scriptContent], { type: isWin ? 'text/plain' : 'text/x-sh' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);

      if (isWin) {
        message.info(
          {
            content: (
              <span>
                未检测到一键启动协议。请前往{' '}
                <a href="/settings">设置页</a>
                {' '}下载安装工具（只需安装一次）。当前已下载手动启动脚本。
              </span>
            ),
            duration: 8,
          } as any,
        );
      } else {
        message.info(
          `启动脚本已下载（${fileName}）。运行该脚本以注入存档并启动${pkg.type === 'azahar' ? 'Azahar' : 'DeSmuME'}。`,
          8,
        );
      }

      setLaunchStates(prev => {
        const next = { ...prev };
        delete next[saveFileId];
        return next;
      });
    } catch (err: any) {
      message.error(err.response?.data?.message || '启动失败');
      setLaunchStates(prev => {
        const next = { ...prev };
        delete next[saveFileId];
        return next;
      });
    }
  };

  const fetchSaves = useCallback(async () => {
    setLoading(true);
    try {
      const res = await saveFileApi.list();
      setSaves(res.data);
    } catch {
      message.error('加载存档列表失败');
    } finally {
      setLoading(false);
    }
  }, [message]);

  useEffect(() => {
    fetchSaves();
  }, [fetchSaves]);

  const handleUpload = async (file: File) => {
    setUploading(true);
    try {
      await saveFileApi.upload(file);
      message.success('存档上传并解析成功！');
      fetchSaves();
    } catch (err: any) {
      message.error(err.response?.data?.message || '上传失败，请检查文件格式');
    } finally {
      setUploading(false);
    }
    return false; // Prevent default upload behavior
  };

  const handleDelete = async (id: string) => {
    try {
      await saveFileApi.delete(id);
      message.success('存档已删除');
      fetchSaves();
    } catch {
      message.error('删除失败');
    }
  };

  const formatPlayTime = (seconds: number) => {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return `${h}h ${m}m`;
  };

  const columns: ColumnsType<SaveFileInfo> = [
    {
      title: '文件名',
      dataIndex: 'filename',
      key: 'filename',
      ellipsis: true,
    },
    {
      title: '游戏',
      dataIndex: 'gameVersion',
      key: 'gameVersion',
      width: 110,
      render: (ver: number) => {
        const info = GAME_VERSION_DISPLAY[ver];
        return (
          <Space size={8}>
            <GameCover gameVersion={ver} size="small" showPlatform={false}
              style={{ minWidth: 0, minHeight: 0, padding: 0 }} />
            {info
              ? <Tag color={info.color}>{info.name}</Tag>
              : <Tag>{GENERATION_MAP[ver] || `Gen${ver}`}</Tag>
            }
          </Space>
        );
      },
    },
    {
      title: '训练家',
      dataIndex: 'trainerName',
      key: 'trainerName',
      width: 90,
    },
    {
      title: '宝可梦',
      dataIndex: 'pokemonCount',
      key: 'pokemonCount',
      width: 70,
      align: 'center',
    },
    {
      title: '时间',
      dataIndex: 'playTime',
      key: 'playTime',
      width: 80,
      render: (t: number) => formatPlayTime(t),
    },
    {
      title: '状态',
      dataIndex: 'isModified',
      key: 'isModified',
      width: 80,
      render: (modified: boolean) =>
        modified ? <Tag color="orange">已修改</Tag> : <Tag>原始</Tag>,
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      render: (date: string) => new Date(date).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 180,
      render: (_, record) => (
        <Space>
          {(record.generation === 3 || record.generation === 4 || record.generation === 5) && (
            <Button type="link" size="small" icon={<PlayCircleOutlined />}
              onClick={() => window.open(`/play${record.generation >= 4 ? '-nds' : ''}/${record.saveFileId}`, '_blank')}
              style={{ color: '#52c41a' }}>WASM</Button>
          )}
          {(record.generation >= 4) && (
            (() => {
              const ls = launchStates[record.saveFileId];
              if (ls?.status === 'launching') return <Button type="link" size="small" loading>启动中</Button>;
              if (ls?.status === 'running') return (
                <Button type="link" size="small" icon={<DesktopOutlined />} style={{ color: '#52c41a' }}>
                  {ls.type === 'azahar' ? '3DS' : 'NDS'} 运行中
                </Button>
              );
              if (ls?.status === 'syncing') return <Button type="link" size="small" loading>同步中</Button>;
              return (
                <Button type="link" size="small" icon={<DesktopOutlined />}
                  onClick={() => handleLaunchLocal(record)}>本机</Button>
              );
            })()
          )}
          <Button
            type="link"
            size="small"
            icon={<EyeOutlined />}
            onClick={() => navigate(`/saves/${record.saveFileId}`)}
          >
            查看
          </Button>
          <Popconfirm
            title="确定删除此存档？"
            description="删除后数据不可恢复"
            onConfirm={() => handleDelete(record.saveFileId)}
            okText="确定"
            cancelText="取消"
          >
            <Button type="link" size="small" danger icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <PageContainer
      title="存档管理"
      backTo="/dashboard"
      maxWidth={1200}
      extra={
        <Space size={12} align="center">
          <Button icon={<SettingOutlined />} onClick={() => navigate('/settings')}>模拟器设置</Button>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
            <Upload showUploadList={false} beforeUpload={handleUpload}>
              <Button type="primary" icon={<UploadOutlined />} loading={uploading} size="large">
                上传存档
              </Button>
            </Upload>
            <Text type="secondary" style={{ fontSize: 12 }}>
              支持 `.sav/.dat/.dsv/.gci`，以及 3DS 无扩展名 `main`
            </Text>
          </div>
        </Space>
      }
    >

      <Card>
        <Table
          columns={columns}
          dataSource={saves}
          rowKey="saveFileId"
          loading={loading}
          scroll={{ x: 860 }}
          pagination={{ pageSize: 10 }}
          locale={{
            emptyText: (
              <div style={{ padding: 48 }}>
                <FileAddOutlined style={{ fontSize: 48, color: '#ccc' }} />
                <p style={{ marginTop: 16, color: '#999' }}>
                  暂无存档，点击「上传存档」开始
                </p>
              </div>
            ),
          }}
        />
      </Card>
    </PageContainer>
  );
};

export default SavesPage;
