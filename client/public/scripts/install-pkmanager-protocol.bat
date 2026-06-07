@echo off
setlocal
cd /d "%~dp0"
title pkmanager protocol installer

set "SELF=%~f0"
set "INSTALL_DIR=%APPDATA%\pkmanager"
set "PS1=%INSTALL_DIR%\pkmanager-launcher.ps1"
set "BAT=%INSTALL_DIR%\pkmanager-launcher.bat"
set "EXTRACTOR=%TEMP%\pkmanager-extract-%RANDOM%-%RANDOM%.ps1"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo [pkmanager] Admin permission required. Requesting elevation...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo ============================================
echo   pkmanager protocol installer v5
echo ============================================
echo.

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
if errorlevel 1 goto :install_dir_error

echo [1/4] Writing PowerShell launcher...
> "%EXTRACTOR%" echo $self = $env:SELF
>> "%EXTRACTOR%" echo $outFile = $env:PS1
>> "%EXTRACTOR%" echo $lines = Get-Content -LiteralPath $self -Encoding UTF8
>> "%EXTRACTOR%" echo $start = [Array]::IndexOf($lines, 'REM @@LAUNCHER_START@@')
>> "%EXTRACTOR%" echo $end = [Array]::IndexOf($lines, 'REM @@LAUNCHER_END@@')
>> "%EXTRACTOR%" echo if ($start -lt 0 -or $end -le $start) { throw 'launcher markers not found' }
>> "%EXTRACTOR%" echo $encoding = New-Object System.Text.UTF8Encoding $true
>> "%EXTRACTOR%" echo $writer = New-Object System.IO.StreamWriter($outFile, $false, $encoding)
>> "%EXTRACTOR%" echo try {
>> "%EXTRACTOR%" echo     for ($i = $start + 1; $i -lt $end; $i++) {
>> "%EXTRACTOR%" echo         $line = $lines[$i] -replace '^REM ?', ''
>> "%EXTRACTOR%" echo         $writer.WriteLine($line)
>> "%EXTRACTOR%" echo     }
>> "%EXTRACTOR%" echo } finally {
>> "%EXTRACTOR%" echo     $writer.Dispose()
>> "%EXTRACTOR%" echo }
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%EXTRACTOR%"
set "EXTRACT_RC=%errorlevel%"
del /q "%EXTRACTOR%" >nul 2>&1
if not "%EXTRACT_RC%"=="0" goto :extract_error
if not exist "%PS1%" goto :extract_missing

echo [2/4] Writing bridge batch file...
> "%BAT%" echo @echo off
>> "%BAT%" echo setlocal
>> "%BAT%" echo powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PS1%" "%%~1"
if errorlevel 1 goto :bridge_error
if not exist "%BAT%" goto :bridge_missing

echo [3/4] Registering pkmanager:// protocol...
set "REG_FAILED=0"
reg add "HKCR\pkmanager" /f /ve /d "URL:pkmanager Protocol" >nul 2>&1
if errorlevel 1 set "REG_FAILED=1"
reg add "HKCR\pkmanager" /f /v "URL Protocol" /d "" >nul 2>&1
if errorlevel 1 set "REG_FAILED=1"
reg add "HKCR\pkmanager\shell\open\command" /f /ve /d "\"%BAT%\" \"%%1\"" >nul 2>&1
if errorlevel 1 set "REG_FAILED=1"
if not "%REG_FAILED%"=="0" goto :register_error

echo [4/4] Verifying registration...
reg query "HKCR\pkmanager\shell\open\command" /ve >nul 2>&1
if errorlevel 1 goto :verify_error

echo.
echo ============================================
echo   Installation complete
echo ============================================
echo Launcher PS1: %PS1%
echo Bridge BAT  : %BAT%
echo.
echo Return to pkmanager and click the local-launch button.
echo Press any key to close this window.
pause >nul
exit /b 0

:install_dir_error
echo [ERROR] Failed to create install directory:
echo %INSTALL_DIR%
pause
exit /b 1

:extract_error
echo [ERROR] Failed to extract PowerShell launcher.
pause
exit /b 1

:extract_missing
echo [ERROR] Launcher file was not created:
echo %PS1%
pause
exit /b 1

:bridge_error
echo [ERROR] Failed to create bridge batch file.
pause
exit /b 1

:bridge_missing
echo [ERROR] Bridge batch file was not created:
echo %BAT%
pause
exit /b 1

:register_error
echo [ERROR] Failed to register the pkmanager protocol.
echo Run this installer as Administrator.
pause
exit /b 1

:verify_error
echo [ERROR] Registration verification failed.
pause
exit /b 1

REM @@LAUNCHER_START@@
REM # pkmanager local launcher
REM 
REM param([string]$Url)
REM 
REM $ErrorActionPreference = "Stop"
REM $Host.UI.RawUI.WindowTitle = "pkmanager Launcher"
REM trap {
REM     Write-Host "[ERROR] Unexpected failure: $($_.Exception.Message)" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM function Resolve-AzaharTitleIdLow([string]$titleIdLow, [string]$romPath) {
REM     if ($titleIdLow) {
REM         return $titleIdLow.ToUpperInvariant()
REM     }
REM     if ($romPath -match 'title[\\/]+00040000[\\/]+([0-9A-Fa-f]{8})[\\/]content') {
REM         return $Matches[1].ToUpperInvariant()
REM     }
REM     return ''
REM }
REM 
REM function Resolve-AzaharSdmcPath([string]$dataDir) {
REM     $trimmed = $dataDir.TrimEnd('\', '/')
REM     if ($trimmed.EndsWith('sdmc', [System.StringComparison]::OrdinalIgnoreCase)) {
REM         return $trimmed
REM     }
REM     return Join-Path $trimmed 'sdmc'
REM }
REM 
REM function Normalize-WindowsPath([string]$path) {
REM     if (-not $path) {
REM         return $path
REM     }
REM     if ($path -match '^[A-Za-z]:[\\/]' -or $path -match '^\\\\') {
REM         return ($path -replace '/', '\')
REM     }
REM     return $path
REM }
REM 
REM function Quote-ProcessArgument([string]$value) {
REM     if (-not $value) {
REM         return $value
REM     }
REM     return '"' + $value.Replace('"', '\"') + '"'
REM }
REM 
REM function Get-BytesSha256Hex([byte[]]$bytes) {
REM     $sha = [System.Security.Cryptography.SHA256]::Create()
REM     try {
REM         return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
REM     } finally {
REM         $sha.Dispose()
REM     }
REM }
REM 
REM function Get-BigEndianUInt32([byte[]]$bytes, [int]$offset) {
REM     return [uint32]((([uint32]$bytes[$offset]) -shl 24) -bor (([uint32]$bytes[$offset + 1]) -shl 16) -bor (([uint32]$bytes[$offset + 2]) -shl 8) -bor ([uint32]$bytes[$offset + 3]))
REM }
REM 
REM function Get-BigEndianUInt16([byte[]]$bytes, [int]$offset) {
REM     return [uint16]((([uint16]$bytes[$offset]) -shl 8) -bor ([uint16]$bytes[$offset + 1]))
REM }
REM 
REM function Get-TmdSignatureSize([uint32]$signatureType) {
REM     switch ($signatureType) {
REM         0x10000 { return 0x200 }
REM         0x10003 { return 0x200 }
REM         0x10001 { return 0x100 }
REM         0x10004 { return 0x100 }
REM         0x10002 { return 0x3C }
REM         0x10005 { return 0x3C }
REM         default { throw ("Unknown TMD signature type: 0x{0:X8}" -f $signatureType) }
REM     }
REM }
REM 
REM function Resolve-AzaharContentPath([string]$dataDir, [string]$titleIdLow, [string]$fallbackPath) {
REM     $resolvedTitleIdLow = Resolve-AzaharTitleIdLow $titleIdLow $fallbackPath
REM     if (-not $resolvedTitleIdLow) {
REM         if ($fallbackPath) {
REM             return $fallbackPath
REM         }
REM         throw "Missing title id for Azahar content path resolution."
REM     }
REM 
REM     $contentDir = Join-Path (Resolve-AzaharSdmcPath $dataDir) "Nintendo 3DS"
REM     $contentDir = Join-Path $contentDir "00000000000000000000000000000000"
REM     $contentDir = Join-Path $contentDir "00000000000000000000000000000000"
REM     $contentDir = Join-Path $contentDir "title"
REM     $contentDir = Join-Path $contentDir "00040000"
REM     $contentDir = Join-Path $contentDir $resolvedTitleIdLow
REM     $contentDir = Join-Path $contentDir "content"
REM 
REM     if (-not (Test-Path $contentDir -ErrorAction SilentlyContinue)) {
REM         if ($fallbackPath -and (Test-Path $fallbackPath -ErrorAction SilentlyContinue)) {
REM             Write-Host "[WARN] Azahar content directory is not accessible, using backend fallback path." -ForegroundColor Yellow
REM             return $fallbackPath
REM         }
REM         throw "Azahar content directory is not accessible: $contentDir"
REM     }
REM 
REM     $tmdFile = Get-ChildItem -LiteralPath $contentDir -Filter *.tmd -File -ErrorAction Stop |
REM         Sort-Object Name |
REM         Select-Object -First 1
REM     if (-not $tmdFile) {
REM         if ($fallbackPath -and (Test-Path $fallbackPath -ErrorAction SilentlyContinue)) {
REM             Write-Host "[WARN] No TMD file found, using backend fallback path." -ForegroundColor Yellow
REM             return $fallbackPath
REM         }
REM         throw "No TMD file was found in $contentDir"
REM     }
REM 
REM     try {
REM         $bytes = [IO.File]::ReadAllBytes($tmdFile.FullName)
REM         if ($bytes.Length -lt 4) {
REM             throw "TMD file is too small: $($tmdFile.FullName)"
REM         }
REM 
REM         $signatureType = Get-BigEndianUInt32 $bytes 0
REM         $signatureSize = Get-TmdSignatureSize $signatureType
REM         $bodyStart = [int]([Math]::Ceiling(($signatureSize + 4) / 64.0) * 64)
REM         $chunkBase = $bodyStart + 0x9C4
REM         if ($bytes.Length -lt ($chunkBase + 4)) {
REM             throw "TMD file is truncated: $($tmdFile.FullName)"
REM         }
REM 
REM         $contentCount = Get-BigEndianUInt16 $bytes ($bodyStart + 0x9E)
REM         if ($contentCount -lt 1) {
REM             throw "TMD does not contain launchable content: $($tmdFile.FullName)"
REM         }
REM 
REM         if ($contentCount -gt 1 -and $bytes.Length -ge ($chunkBase + 0x38)) {
REM             $secondContentType = Get-BigEndianUInt16 $bytes ($chunkBase + 0x30 + 6)
REM             if (($secondContentType -band 0x4000) -ne 0) {
REM                 $contentDir = Join-Path $contentDir "00000000"
REM             }
REM         }
REM 
REM         $contentId = Get-BigEndianUInt32 $bytes $chunkBase
REM         return Join-Path $contentDir ("{0:x8}.app" -f $contentId)
REM     } catch {
REM         if ($fallbackPath -and (Test-Path $fallbackPath -ErrorAction SilentlyContinue)) {
REM             Write-Host "[WARN] Failed to parse TMD, using backend fallback path: $($_.Exception.Message)" -ForegroundColor Yellow
REM             return $fallbackPath
REM         }
REM         throw
REM     }
REM }
REM 
REM if (-not $Url) {
REM     Write-Host "[ERROR] Missing protocol URL" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM $token = ""
REM $backendUrl = ""
REM if ($Url -match 'pkmanager://launch/([^?]+)\?backend=(.+)') {
REM     $token = $Matches[1]
REM     $backendUrl = [Uri]::UnescapeDataString($Matches[2])
REM }
REM 
REM if (-not $token -or -not $backendUrl) {
REM     Write-Host "[ERROR] Invalid protocol URL" -ForegroundColor Red
REM     Write-Host "  Received: $Url" -ForegroundColor Yellow
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM Write-Host "[pkmanager] Backend: $backendUrl"
REM Write-Host "[pkmanager] Token: $($token.Substring(0,[Math]::Min(8,$token.Length)))..."
REM 
REM $apiUrl = "$backendUrl/api/Emulator/launch-package/$token"
REM Write-Host "[pkmanager] GET $apiUrl"
REM 
REM try {
REM     if ($backendUrl -match "localhost") {
REM         [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
REM     }
REM     $response = Invoke-RestMethod -Uri $apiUrl -Method Get -TimeoutSec 15
REM } catch {
REM     Write-Host "[ERROR] Failed to reach backend" -ForegroundColor Red
REM     Write-Host "  API: $apiUrl" -ForegroundColor Yellow
REM     Write-Host "  Error: $_" -ForegroundColor Red
REM     Write-Host "  Make sure the pkmanager backend is running and reachable." -ForegroundColor Yellow
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM if ($response.code -ne 0) {
REM     Write-Host "[ERROR] Backend returned: $($response.message)" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM $pkg = $response.data
REM $pkg.exePath = Normalize-WindowsPath $pkg.exePath
REM $pkg.saveDir = Normalize-WindowsPath $pkg.saveDir
REM $pkg.emuSavePath = Normalize-WindowsPath $pkg.emuSavePath
REM $pkg.romPath = Normalize-WindowsPath $pkg.romPath
REM $titleIdLow = Resolve-AzaharTitleIdLow $pkg.titleIdLow $pkg.romPath
REM $romPath = if ($pkg.type -eq "azahar") {
REM     Normalize-WindowsPath (Resolve-AzaharContentPath $pkg.saveDir $titleIdLow $pkg.romPath)
REM } else {
REM     Normalize-WindowsPath $pkg.romPath
REM }
REM $launchArgs = if ($romPath) { @(Quote-ProcessArgument $romPath) } else { @() }
REM 
REM Write-Host "[pkmanager] Launch package received" -ForegroundColor Green
REM Write-Host "  Type: $($pkg.type) Gen$($pkg.generation)"
REM if ($titleIdLow) {
REM     Write-Host "  TID : $titleIdLow"
REM }
REM Write-Host "  EXE : $($pkg.exePath)"
REM Write-Host "  ROM : $romPath"
REM Write-Host "  SAVE: $($pkg.emuSavePath)"
REM if ($pkg.type -eq "azahar" -and $pkg.romPath -and $pkg.romPath -ne $romPath) {
REM     Write-Host "  NOTE: Resolved Azahar content from TMD instead of backend fallback." -ForegroundColor Yellow
REM }
REM if ($pkg.saveFileId) {
REM     Write-Host "  SAVE ID : $($pkg.saveFileId)"
REM }
REM 
REM $exeDrive = Split-Path $pkg.exePath -Qualifier
REM if ($exeDrive -and -not (Test-Path "$exeDrive\" -ErrorAction SilentlyContinue)) {
REM     Write-Host "[ERROR] Drive is not accessible: $exeDrive" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM $romDir = Split-Path $romPath -Parent
REM if ($romDir -and -not (Test-Path $romDir -ErrorAction SilentlyContinue)) {
REM     Write-Host "[ERROR] ROM directory is not accessible: $romDir" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM $saveParent = Split-Path $pkg.emuSavePath -Parent
REM if (-not (Test-Path $saveParent -ErrorAction SilentlyContinue)) {
REM     try {
REM         New-Item -ItemType Directory -Force -Path $saveParent -ErrorAction Stop | Out-Null
REM     } catch {
REM         Write-Host "[ERROR] Failed to create save directory: $saveParent" -ForegroundColor Red
REM         Read-Host "Press Enter to exit"
REM         exit 1
REM     }
REM }
REM 
REM if ($pkg.type -eq "azahar") {
REM     $backupDir = Join-Path $pkg.saveDir "pkmanager_backup" | Join-Path -ChildPath $titleIdLow
REM     $backupFile = Join-Path $backupDir "main.bak"
REM } else {
REM     $backupDir = Join-Path $pkg.saveDir "pkmanager_backup"
REM     $backupFile = Join-Path $backupDir "save.dsv.bak"
REM }
REM $hadExistingSave = Test-Path $pkg.emuSavePath -ErrorAction SilentlyContinue
REM 
REM try {
REM     New-Item -ItemType Directory -Force -Path $backupDir -ErrorAction Stop | Out-Null
REM } catch {
REM     Write-Host "[ERROR] Failed to create backup directory: $backupDir" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM $backupReady = $false
REM if ($hadExistingSave) {
REM     try {
REM         Copy-Item $pkg.emuSavePath $backupFile -Force -ErrorAction Stop
REM         $backupReady = $true
REM         Write-Host "[pkmanager] Existing save backed up" -ForegroundColor Green
REM     } catch {
REM         Write-Host "[WARN] Backup failed, continuing anyway" -ForegroundColor Yellow
REM     }
REM } else {
REM     Write-Host "[pkmanager] No existing save found"
REM }
REM 
REM try {
REM     $saveBytes = [Convert]::FromBase64String(($pkg.saveDataBase64 -replace "\s", ""))
REM     $expectedHash = Get-BytesSha256Hex $saveBytes
REM     Write-Host "[pkmanager] Save bytes: $($saveBytes.Length)"
REM     [IO.File]::WriteAllBytes($pkg.emuSavePath, $saveBytes)
REM     $writtenBytes = [IO.File]::ReadAllBytes($pkg.emuSavePath)
REM     $actualHash = Get-BytesSha256Hex $writtenBytes
REM     if ($writtenBytes.Length -ne $saveBytes.Length -or $actualHash -ne $expectedHash) {
REM         Write-Host "[ERROR] Save verification failed after write." -ForegroundColor Red
REM         Write-Host "  Expected: $($saveBytes.Length) bytes, SHA256=$expectedHash" -ForegroundColor Yellow
REM         Write-Host "  Actual  : $($writtenBytes.Length) bytes, SHA256=$actualHash" -ForegroundColor Yellow
REM         Read-Host "Press Enter to exit"
REM         exit 1
REM     }
REM     Write-Host "[pkmanager] Save verify: OK ($actualHash)" -ForegroundColor Green
REM     Write-Host "[pkmanager] Save injected" -ForegroundColor Green
REM } catch {
REM     Write-Host "[ERROR] Failed to write save: $_" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM if (-not (Test-Path $pkg.exePath -ErrorAction SilentlyContinue)) {
REM     Write-Host "[ERROR] Emulator executable was not found: $($pkg.exePath)" -ForegroundColor Red
REM     Read-Host "Press Enter to exit"
REM     exit 1
REM }
REM 
REM Write-Host "[pkmanager] Launching: $($pkg.exePath) $($launchArgs -join ' ')" -ForegroundColor Green
REM $process = Start-Process -FilePath $pkg.exePath -ArgumentList $launchArgs -PassThru
REM if ($null -eq $process) {
REM     Write-Host "[ERROR] Emulator process failed to start." -ForegroundColor Red
REM     Read-Host "Press Enter to close this window"
REM     exit 1
REM }
REM Write-Host "[pkmanager] Emulator launched (PID: $($process.Id)). Waiting for exit..." -ForegroundColor Green
REM $process.WaitForExit()
REM Write-Host "[pkmanager] Emulator exited with code $($process.ExitCode)"
REM 
REM if (-not $pkg.saveFileId -or -not $pkg.syncToken) {
REM     Write-Host "[WARN] Missing sync token, skipping automatic sync." -ForegroundColor Yellow
REM } elseif (-not (Test-Path $pkg.emuSavePath -ErrorAction SilentlyContinue)) {
REM     Write-Host "[WARN] Save file was not found after emulator exit, skipping sync: $($pkg.emuSavePath)" -ForegroundColor Yellow
REM } else {
REM     $syncUrl = "$backendUrl/api/Emulator/sync-save/$($pkg.saveFileId)?token=$([Uri]::EscapeDataString($pkg.syncToken))"
REM     $syncBytes = [IO.File]::ReadAllBytes($pkg.emuSavePath)
REM     Write-Host "[pkmanager] Syncing save back to backend..."
REM     Write-Host "[pkmanager] Sync bytes: $($syncBytes.Length)"
REM     Write-Host "[pkmanager] POST $syncUrl"
REM     try {
REM         if ($backendUrl -match "localhost") {
REM             [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
REM         }
REM         $syncResponse = Invoke-RestMethod -Uri $syncUrl -Method Post -ContentType "application/octet-stream" -Body $syncBytes -TimeoutSec 60
REM         if ($syncResponse.code -eq 0) {
REM             Write-Host "[pkmanager] Save synced successfully." -ForegroundColor Green
REM             if ($backupReady -and (Test-Path $backupFile -ErrorAction SilentlyContinue)) {
REM                 try {
REM                     Copy-Item $backupFile $pkg.emuSavePath -Force -ErrorAction Stop
REM                     Write-Host "[pkmanager] Restored previous local save." -ForegroundColor Green
REM                 } catch {
REM                     Write-Host "[WARN] Failed to restore previous local save: $($_.Exception.Message)" -ForegroundColor Yellow
REM                 }
REM             } elseif ($pkg.type -eq "desmume" -and -not $hadExistingSave -and (Test-Path $pkg.emuSavePath -ErrorAction SilentlyContinue)) {
REM                 try {
REM                     Remove-Item $pkg.emuSavePath -Force -ErrorAction Stop
REM                     Write-Host "[pkmanager] Removed injected temporary save (first launch)." -ForegroundColor Green
REM                 } catch {
REM                     Write-Host "[WARN] Failed to clean injected temporary save: $($_.Exception.Message)" -ForegroundColor Yellow
REM                 }
REM             } else {
REM                 Write-Host "[pkmanager] No previous local save to restore." -ForegroundColor Yellow
REM             }
REM         } else {
REM             Write-Host "[WARN] Backend sync returned non-zero code: $($syncResponse.message)" -ForegroundColor Yellow
REM         }
REM     } catch {
REM         Write-Host "[WARN] Automatic sync failed: $($_.Exception.Message)" -ForegroundColor Yellow
REM     }
REM }
REM Read-Host "Press Enter to close this window"
REM @@LAUNCHER_END@@
