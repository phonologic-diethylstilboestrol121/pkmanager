import type { MessageInstance } from 'antd/es/message/interface';
import { emulatorApi, type LaunchLocalResult } from '../api/saveFile';

const detectProtocolSupport = (protoUrl: string) => new Promise<boolean>((resolve) => {
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

const triggerDownload = (content: string, fileName: string, mime: string) => {
  const blob = new Blob([content], { type: mime });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

const buildWindowsScript = (pkg: LaunchLocalResult, backendBase: string, fallbackName?: string) => {
  const escapedSavePath = pkg.emuSavePath?.replace(/"/g, '`"') ?? '';
  const escapedExe = pkg.exePath.replace(/"/g, '`"');
  const escapedRom = (pkg.romPath || '').replace(/"/g, '`"');
  const escapedSaveDir = pkg.saveDir.replace(/"/g, '`"');
  const titleIdLow = (pkg.titleIdLow || '').replace(/"/g, '`"');
  const escapedBackend = backendBase.replace(/"/g, '`"');
  const escapedSaveFileId = pkg.saveFileId.replace(/"/g, '`"');
  const escapedSyncToken = pkg.syncToken.replace(/"/g, '`"');
  const baseName = (pkg.fileName || fallbackName || 'save').replace(/\.[^.]+$/, '');

  const scriptContent = `# pkmanager — 本地启动 Azahar/DeSmuME 脚本
$ErrorActionPreference = "Stop"
trap {
    Write-Host "[ERROR] Unexpected failure: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "按 Enter 退出"
    exit 1
}

function Resolve-AzaharTitleIdLow([string]$titleIdLow, [string]$fallbackRomPath) {
    if ($titleIdLow) { return $titleIdLow.ToUpperInvariant() }
    if ($fallbackRomPath -match 'title[\\\\/]+00040000[\\\\/]+([0-9A-Fa-f]{8})[\\\\/]content') { return $Matches[1].ToUpperInvariant() }
    return ''
}

function Resolve-AzaharSdmcPath([string]$dataDir) {
    $trimmed = $dataDir.TrimEnd('\\', '/')
    if ($trimmed.EndsWith('sdmc', [System.StringComparison]::OrdinalIgnoreCase)) { return $trimmed }
    return Join-Path $trimmed 'sdmc'
}

function Get-BigEndianUInt32([byte[]]$bytes, [int]$offset) {
    return [uint32]((([uint32]$bytes[$offset]) -shl 24) -bor (([uint32]$bytes[$offset + 1]) -shl 16) -bor (([uint32]$bytes[$offset + 2]) -shl 8) -bor ([uint32]$bytes[$offset + 3]))
}

function Get-BigEndianUInt16([byte[]]$bytes, [int]$offset) {
    return [uint16]((([uint16]$bytes[$offset]) -shl 8) -bor ([uint16]$bytes[$offset + 1]))
}

function Quote-ProcessArgument([string]$value) {
    if (-not $value) { return $value }
    return '"' + $value.Replace('"', '\"') + '"'
}

function Get-BytesSha256Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '') }
    finally { $sha.Dispose() }
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
        if ($fallbackRomPath) { return $fallbackRomPath }
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
        $signatureType = Get-BigEndianUInt32 $bytes 0
        $signatureSize = Get-TmdSignatureSize $signatureType
        $bodyStart = [int]([Math]::Ceiling(($signatureSize + 4) / 64.0) * 64)
        $chunkBase = $bodyStart + 0x9C4
        $contentCount = Get-BigEndianUInt16 $bytes ($bodyStart + 0x9E)

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
$romPath = if ($type -eq "azahar") { Resolve-AzaharContentPath $saveDir $titleIdLow $fallbackRomPath } else { $fallbackRomPath }
$launchArgs = if ($romPath) { @(Quote-ProcessArgument $romPath) } else { @() }

$saveParent = Split-Path $emuSavePath -Parent
if (-not (Test-Path $saveParent -ErrorAction SilentlyContinue)) {
    New-Item -ItemType Directory -Force -Path $saveParent -ErrorAction Stop | Out-Null
}

$backupDir = if ($type -eq "azahar") { Join-Path $saveDir "pkmanager_backup" | Join-Path -ChildPath $titleIdLow } else { Join-Path $saveDir "pkmanager_backup" }
$backupFile = if ($type -eq "azahar") { Join-Path $backupDir "main.bak" } else { Join-Path $backupDir "save.dsv.bak" }
$hadExistingSave = Test-Path $emuSavePath -ErrorAction SilentlyContinue
$backupReady = $false
New-Item -ItemType Directory -Force -Path $backupDir -ErrorAction Stop | Out-Null
if ($hadExistingSave) {
    try {
        Copy-Item $emuSavePath $backupFile -Force -ErrorAction Stop
        $backupReady = $true
        Write-Host "[pkmanager] Existing save backed up" -ForegroundColor Green
    } catch {
        Write-Host "[WARN] Backup failed, continuing anyway" -ForegroundColor Yellow
    }
}

$saveBytes = [Convert]::FromBase64String(($saveDataBase64 -replace "\\s", ""))
$expectedHash = Get-BytesSha256Hex $saveBytes
Write-Host "[pkmanager] Save bytes: $($saveBytes.Length)"
[IO.File]::WriteAllBytes($emuSavePath, $saveBytes)
$writtenBytes = [IO.File]::ReadAllBytes($emuSavePath)
$actualHash = Get-BytesSha256Hex $writtenBytes
if ($writtenBytes.Length -ne $saveBytes.Length -or $actualHash -ne $expectedHash) {
    Write-Host "[ERROR] Save verification failed after write." -ForegroundColor Red
    Read-Host "Press Enter to close this window"
    exit 1
}
Write-Host "[pkmanager] Save verify: OK ($actualHash)" -ForegroundColor Green
Write-Host "[pkmanager] Launching: $exePath $($launchArgs -join ' ')" -ForegroundColor Green
$process = Start-Process -FilePath $exePath -ArgumentList $launchArgs -PassThru
if ($null -eq $process) {
    Write-Host "[ERROR] Emulator process failed to start." -ForegroundColor Red
    Read-Host "Press Enter to close this window"
    exit 1
}
$process.WaitForExit()
Write-Host "[pkmanager] Emulator exited with code $($process.ExitCode)"

if ($saveFileId -and $syncToken -and (Test-Path $emuSavePath -ErrorAction SilentlyContinue)) {
    $syncUrl = "$backendBase/api/Emulator/sync-save/$saveFileId?token=$([Uri]::EscapeDataString($syncToken))"
    $syncBytes = [IO.File]::ReadAllBytes($emuSavePath)
    Write-Host "[pkmanager] Syncing save back to backend..."
    try {
        $syncResponse = Invoke-RestMethod -Uri $syncUrl -Method Post -ContentType "application/octet-stream" -Body $syncBytes -TimeoutSec 60
        if ($syncResponse.code -eq 0) {
            Write-Host "[pkmanager] Save synced successfully." -ForegroundColor Green
            if ($backupReady -and (Test-Path $backupFile -ErrorAction SilentlyContinue)) {
                try {
                    Copy-Item $backupFile $emuSavePath -Force -ErrorAction Stop
                    Write-Host "[pkmanager] Restored previous local save." -ForegroundColor Green
                } catch {
                    Write-Host "[WARN] Failed to restore previous local save: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            } elseif ($type -eq "desmume" -and -not $hadExistingSave -and (Test-Path $emuSavePath -ErrorAction SilentlyContinue)) {
                try {
                    Remove-Item $emuSavePath -Force -ErrorAction Stop
                    Write-Host "[pkmanager] Removed injected temporary save (first launch)." -ForegroundColor Green
                } catch {
                    Write-Host "[WARN] Failed to clean injected temporary save: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            } else {
                Write-Host "[pkmanager] No previous local save to restore." -ForegroundColor Yellow
            }
        } else {
            Write-Host "[WARN] Backend sync returned non-zero code: $($syncResponse.message)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "[WARN] Automatic sync failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "[WARN] Missing sync data, skipping automatic sync." -ForegroundColor Yellow
}

Read-Host "Press Enter to close this window"
`;

  return { fileName: `pkmanager_launch_${baseName}.ps1`, scriptContent };
};

const buildPosixScript = (pkg: LaunchLocalResult, backendBase: string, fallbackName?: string) => {
  const escapedSavePath = (pkg.emuSavePath || '').replace(/'/g, "'\\''");
  const escapedExe = pkg.exePath.replace(/'/g, "'\\''");
  const escapedRom = (pkg.romPath || '').replace(/'/g, "'\\''");
  const escapedSaveDir = pkg.saveDir.replace(/'/g, "'\\''");
  const titleIdLow = (pkg.titleIdLow || '').replace(/'/g, "'\\''");
  const escapedBackend = backendBase.replace(/'/g, "'\\''");
  const escapedSaveFileId = pkg.saveFileId.replace(/'/g, "'\\''");
  const escapedSyncToken = pkg.syncToken.replace(/'/g, "'\\''");
  const baseName = (pkg.fileName || fallbackName || 'save').replace(/\.[^.]+$/, '');

  const scriptContent = `#!/bin/bash
set -e

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

if [ "$TYPE" = "azahar" ]; then
  BACKUP_DIR="$SAVE_DIR/pkmanager_backup/$TITLE_ID_LOW"
  BACKUP_FILE="$BACKUP_DIR/main.bak"
else
  BACKUP_DIR="$SAVE_DIR/pkmanager_backup"
  BACKUP_FILE="$BACKUP_DIR/save.dsv.bak"
fi

mkdir -p "$BACKUP_DIR"
HAD_EXISTING_SAVE=0
BACKUP_READY=0
if [ -f "$EMU_SAVE_PATH" ]; then
  HAD_EXISTING_SAVE=1
  if cp "$EMU_SAVE_PATH" "$BACKUP_FILE" 2>/dev/null; then
    BACKUP_READY=1
    echo "[pkmanager] Existing save backed up"
  else
    echo "[WARN] Backup failed, continuing anyway"
  fi
fi

mkdir -p "$(dirname "$EMU_SAVE_PATH")"
echo "$SAVE_DATA_BASE64" | base64 -d > "$EMU_SAVE_PATH"
echo "[pkmanager] Save injected"
echo "[pkmanager] Launching: $EXE_PATH $ROM_PATH"
"$EXE_PATH" "$ROM_PATH"
EXIT_CODE=$?
echo "[pkmanager] Emulator exited with code $EXIT_CODE"

if [ -n "$SAVE_FILE_ID" ] && [ -n "$SYNC_TOKEN" ] && [ -f "$EMU_SAVE_PATH" ]; then
  SYNC_URL="$BACKEND_BASE/api/Emulator/sync-save/$SAVE_FILE_ID?token=$SYNC_TOKEN"
  echo "[pkmanager] Syncing save back to backend..."
  if curl -fsS -X POST -H "Content-Type: application/octet-stream" --data-binary "@$EMU_SAVE_PATH" "$SYNC_URL" >/tmp/pkmanager_sync.log 2>/tmp/pkmanager_sync.err; then
    echo "[pkmanager] Save synced successfully."
    if [ "$BACKUP_READY" = "1" ] && [ -f "$BACKUP_FILE" ]; then
      cp "$BACKUP_FILE" "$EMU_SAVE_PATH" 2>/dev/null && echo "[pkmanager] Restored previous local save." || echo "[WARN] Failed to restore previous local save."
    elif [ "$TYPE" = "desmume" ] && [ "$HAD_EXISTING_SAVE" = "0" ] && [ -f "$EMU_SAVE_PATH" ]; then
      rm -f "$EMU_SAVE_PATH" && echo "[pkmanager] Removed injected temporary save (first launch)." || echo "[WARN] Failed to clean injected temporary save."
    else
      echo "[pkmanager] No previous local save to restore."
    fi
  else
    echo "[WARN] Automatic sync failed"
    cat /tmp/pkmanager_sync.err 2>/dev/null || true
  fi
else
  echo "[WARN] Missing sync data, skipping automatic sync."
fi

echo "[pkmanager] Press Enter to close this window..."
read -r
`;

  return { fileName: `pkmanager_launch_${baseName}.sh`, scriptContent };
};

export const launchLocalSave = async (
  saveFileId: string,
  message: MessageInstance,
  fallbackName?: string,
) => {
  const isWin = navigator.platform?.toLowerCase().includes('win') ?? false;
  const isViteDev = window.location.port === '5173';
  const backendBase = isViteDev ? `http://${window.location.hostname}:5000` : window.location.origin;

  try {
    const tokenRes = await emulatorApi.createLaunchToken(saveFileId);
    const token = tokenRes.data.token;
    const protoUrl = `pkmanager://launch/${token}?backend=${encodeURIComponent(backendBase)}`;
    message.info('如果浏览器提示打开外部应用，请点击允许');
    const supported = await detectProtocolSupport(protoUrl);
    if (supported) {
      message.success('正在启动模拟器...');
      return;
    }
  } catch {
    // fall through to script download
  }

  const res = await emulatorApi.launchLocal(saveFileId);
  const pkg = res.data as LaunchLocalResult;
  if (!pkg.romPath) {
    throw new Error('未找到游戏内容文件路径，请检查模拟器数据目录配置');
  }

  const { fileName, scriptContent } = isWin
    ? buildWindowsScript(pkg, backendBase, fallbackName)
    : buildPosixScript(pkg, backendBase, fallbackName);

  triggerDownload(scriptContent, fileName, isWin ? 'text/plain' : 'text/x-sh');
  message.info(`未检测到一键启动协议，已下载启动脚本（${fileName}）。运行该脚本以注入存档、启动模拟器，并在退出后自动同步。`, 8);
};
