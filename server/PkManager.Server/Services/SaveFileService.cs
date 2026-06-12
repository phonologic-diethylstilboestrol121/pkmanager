using System.Globalization;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using PKHeX.Core;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Entity;
using PkManager.Server.Models.Response;
using SaveFileEntity = PkManager.Server.Models.Entity.SaveFile;

namespace PkManager.Server.Services;

/// <summary>
/// 存档文件管理服务 — 存档存储于文件系统，DB 只存路径和元数据。
/// 每次写入前自动备份（保留最近 5 份）。
/// </summary>
public class SaveFileService
{
    private readonly string _baseSaveDir;

    private readonly NpgsqlConnection _db;
    private readonly ParseService _parseService;
    private readonly LegalityCacheService _legalityCache;

    public SaveFileService(NpgsqlConnection db, ParseService parseService,
        IWebHostEnvironment env, LegalityCacheService legalityCache)
    {
        _db = db;
        _parseService = parseService;
        _legalityCache = legalityCache;
        _baseSaveDir = Path.Combine(env.ContentRootPath, "data", "saves");
    }

    // ═══ 文件系统辅助 ════════════════════════════════════

    private string GetSaveDir(Guid userId, Guid saveFileId) =>
        Path.Combine(_baseSaveDir, userId.ToString(), saveFileId.ToString());

    private string GetSavePath(Guid userId, Guid saveFileId) =>
        Path.Combine(GetSaveDir(userId, saveFileId), "save.sav");

    private string GetBackupDir(Guid userId, Guid saveFileId) =>
        Path.Combine(GetSaveDir(userId, saveFileId), "backups");

    private static PKHeX.Core.SaveFile ValidateWrittenSave(byte[] data, string? fileName = null)
    {
        try
        {
            return ParseService.OpenSaveFile(data, fileName);
        }
        catch (BusinessException)
        {
            throw new BusinessException("保存后的存档无法重新解析，已中止写入");
        }
    }

    /// <summary>
    /// 读取存档二进制 — 规范路径优先，兼容旧 save_path，并在可行时同步修复 DB。
    /// 规范路径始终由 ContentRootPath/data/saves/{userId}/{saveFileId}/save.sav 派生。
    /// </summary>
    public byte[] ReadSaveBytes(SaveFileEntity entity, Guid userId)
    {
        var canonical = GetSavePath(userId, entity.Id);

        // 1. 规范路径优先
        if (File.Exists(canonical))
        {
            if (!string.Equals(entity.SavePath, canonical, StringComparison.Ordinal))
                RepairSavePath(entity.Id, canonical);
            entity.SavePath = canonical;
            return File.ReadAllBytes(canonical);
        }

        // 2. 旧 entity.SavePath（跨机器迁移兼容 — 文件还未迁移到规范路径）
        if (!string.IsNullOrEmpty(entity.SavePath) && !string.Equals(entity.SavePath, canonical, StringComparison.Ordinal) && File.Exists(entity.SavePath))
        {
            var legacyData = File.ReadAllBytes(entity.SavePath);
            try
            {
                var dir = Path.GetDirectoryName(canonical);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(entity.SavePath, canonical, overwrite: true);
                RepairSavePath(entity.Id, canonical);
                entity.SavePath = canonical;
            }
            catch
            {
                // 迁移失败不影响读取，继续返回旧路径数据。
            }
            return legacyData;
        }

        // 3. DB 回退（旧数据兼容）
        if (entity.RawSaveData is { Length: > 0 })
            return entity.RawSaveData;

        return Array.Empty<byte>();
    }

    /// <summary>
    /// 写入存档到文件系统 — 始终写入规范路径，自动修复 DB 中的过期 save_path。
    /// </summary>
    public async Task WriteSaveBytes(SaveFileEntity entity, Guid userId, byte[] data)
    {
        var canonical = GetSavePath(userId, entity.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        await File.WriteAllBytesAsync(canonical, data);

        if (entity.SavePath != canonical)
        {
            await _db.ExecuteAsync(
                "UPDATE save_files SET save_path = @P WHERE id = @Id",
                new { P = canonical, Id = entity.Id });
            entity.SavePath = canonical;
        }

        await _db.ExecuteAsync(
            "UPDATE save_files SET file_size = @S, is_modified = TRUE, updated_at = NOW() WHERE id = @Id",
            new { S = (long)data.Length, Id = entity.Id });
    }

    /// <summary>规范存档路径（纯计算，不查 DB，无副作用）</summary>
    public string GetCanonicalSavePath(Guid userId, Guid saveFileId)
        => GetSavePath(userId, saveFileId);

    /// <summary>
    /// 一次性迁移：将 DB 中所有过期的绝对 save_path 重写为当前规范路径。
    /// 仅在启动时调用一次。
    /// </summary>
    public async Task MigrateSavePaths()
    {
        var rows = await _db.QueryAsync<SaveFileEntity>(
            "SELECT id, user_id, save_path FROM save_files WHERE save_path IS NOT NULL");
        var updated = 0;
        foreach (var row in rows)
        {
            var canonical = GetSavePath(row.UserId, row.Id);
            if (!string.Equals(row.SavePath, canonical, StringComparison.Ordinal))
            {
                await _db.ExecuteAsync(
                    "UPDATE save_files SET save_path = @P WHERE id = @Id",
                    new { P = canonical, Id = row.Id });
                updated++;
            }
        }
        if (updated > 0)
            System.Diagnostics.Debug.WriteLine($"[SaveFileService] Migrated {updated} stale save_path(s) to canonical.");
    }

    private void RepairSavePath(Guid saveFileId, string canonical)
    {
        try
        {
            _db.Execute(
                "UPDATE save_files SET save_path = @P WHERE id = @Id",
                new { P = canonical, Id = saveFileId });
        }
        catch
        {
            // 修复失败不影响读写主流程
        }
    }

    // ═══ 查询 ═══════════════════════════════════════════

    public async Task<List<SaveFileDto>> GetUserSaves(Guid userId)
    {
        var saves = await _db.QueryAsync<SaveFileEntity>(
            "SELECT * FROM save_files WHERE user_id = @UserId ORDER BY updated_at DESC",
            new { UserId = userId });
        return saves.Select(MapToDto).ToList();
    }

    public async Task<SaveFileDetailDto> GetSaveDetail(Guid saveFileId, Guid userId)
    {
        var saveFile = await LoadSaveFileEntity(saveFileId, userId);
        await _db.ExecuteAsync(
            "UPDATE save_files SET last_accessed_at = NOW() WHERE id = @Id",
            new { Id = saveFileId });

        var rawData = ReadSaveBytes(saveFile, userId);

        SaveFileDetailDto parsed;
        try
        {
            parsed = _parseService.ParseSaveFile(rawData, saveFile.Filename);
            // 优先使用 DB 中已归一化的具体版本号（SyncSave 时通过 NormalizeOrKeepExisting 存入），
            // PKHeX 重新解析可能再次返回复合版本（如 DP=62）
            if (saveFile.GameVersion != null)
            {
                parsed.GameVersion = saveFile.GameVersion.Value;
                parsed.GameVersionName = GetVersionNameSafe(saveFile.GameVersion);
            }
        }
        catch (BusinessException)
        {
            parsed = new SaveFileDetailDto
            {
                Filename = saveFile.Filename,
                FileSize = saveFile.FileSize,
                Generation = saveFile.Generation,
                GameVersion = saveFile.GameVersion,
                GameVersionName = GetVersionNameSafe(saveFile.GameVersion),
                TrainerName = saveFile.TrainerName,
                TrainerId = saveFile.TrainerId,
                SecretId = saveFile.SecretId,
                PlayTime = saveFile.PlayTime,
                BoxCount = saveFile.BoxCount,
                PokemonCount = saveFile.PokemonCount,
                Boxes = new(),
                Party = new()
            };
        }

        parsed.SaveFileId = saveFile.Id;
        parsed.IsModified = saveFile.IsModified;
        parsed.CreatedAt = saveFile.CreatedAt;
        parsed.UpdatedAt = saveFile.UpdatedAt;
        return parsed;
    }

    public async Task<SaveFileDetailDto> CreateNewGame(Guid userId, string gameId)
    {
        var gameInfo = gameId switch
        {
            "pkm_sapphire" => (generation: 3, version: 1, name: "宝可梦 蓝宝石"),
            "pkm_ruby" => (generation: 3, version: 2, name: "宝可梦 红宝石"),
            "pkm_emerald" => (generation: 3, version: 3, name: "宝可梦 绿宝石"),
            "pkm_firered" => (generation: 3, version: 4, name: "宝可梦 火红"),
            "pkm_leafgreen" => (generation: 3, version: 5, name: "宝可梦 叶绿"),
            "pkm_diamond" => (generation: 4, version: 10, name: "宝可梦 钻石"),
            "pkm_pearl" => (generation: 4, version: 11, name: "宝可梦 珍珠"),
            "pkm_platinum" => (generation: 4, version: 12, name: "宝可梦 白金"),
            "pkm_heartgold" => (generation: 4, version: 7, name: "宝可梦 心金"),
            "pkm_soulsilver" => (generation: 4, version: 8, name: "宝可梦 魂银"),
            "pkm_white" => (generation: 5, version: 20, name: "宝可梦 白"),
            "pkm_black" => (generation: 5, version: 21, name: "宝可梦 黑"),
            "pkm_white2" => (generation: 5, version: 22, name: "宝可梦 白2"),
            "pkm_black2" => (generation: 5, version: 23, name: "宝可梦 黑2"),
            _ => throw new BusinessException($"未知的游戏: {gameId}")
        };

        var saveFileId = Guid.NewGuid();
        var filename = $"{gameInfo.name} - {DateTime.Now:yyyy-MM-dd HH:mm}";

        // 统一流程：创建空占位记录，由模拟器首次游戏内保存时通过 sync-save 填充存档数据
        // 不再使用 PKHeX 预创建空白存档（NDS Gen4/5 的 SAV4/SAV5 无公开构造函数会导致崩溃）
        var parsed = new SaveFileDetailDto
        {
            Filename = filename,
            FileSize = 0L,
            Generation = gameInfo.generation,
            GameVersion = gameInfo.version,
            GameVersionName = gameInfo.name,
            Boxes = new(),
            Party = new()
        };

        await _db.ExecuteAsync(@"
            INSERT INTO save_files (id, user_id, filename, file_size, generation, game_version,
                trainer_name, trainer_id, secret_id, play_time, box_count, pokemon_count,
                is_valid_save, raw_save_data)
            VALUES (@Id, @UserId, @Filename, @FileSize, @Generation, @GameVersion,
                @TrainerName, @TrainerId, @SecretId, @PlayTime, @BoxCount, @PokemonCount,
                @IsValidSave, @RawSaveData)",
            new
            {
                Id = saveFileId, UserId = userId, Filename = filename,
                FileSize = 0L, Generation = gameInfo.generation, GameVersion = gameInfo.version,
                TrainerName = (string?)null, TrainerId = (int?)null, SecretId = (int?)null,
                PlayTime = 0, BoxCount = 0, PokemonCount = 0,
                IsValidSave = true, RawSaveData = Array.Empty<byte>()
            });

        parsed.SaveFileId = saveFileId;
        return parsed;
    }

    // ═══ 上传 / 删除 ══════════════════════════════════════

    public async Task<SaveFileDetailDto> UploadSave(Guid userId, byte[] rawData, string filename)
    {
        var parsed = _parseService.ParseSaveFile(rawData, filename);
        var saveFileId = Guid.NewGuid();

        // 写入文件系统
        var savePath = GetSavePath(userId, saveFileId);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        await File.WriteAllBytesAsync(savePath, rawData);

        // 归一化游戏版本号（PKHeX 可能返回复合版本如 DP=62）
        var normalizedVersion = Helpers.GameVersionNormalizer.Normalize(parsed.GameVersion);

        await _db.ExecuteAsync(@"
            INSERT INTO save_files (id, user_id, filename, file_size, generation, game_version,
                trainer_name, trainer_id, secret_id, play_time, box_count, pokemon_count,
                is_valid_save, raw_save_data, save_path)
            VALUES (@Id, @UserId, @Filename, @FileSize, @Generation, @GameVersion,
                @TrainerName, @TrainerId, @SecretId, @PlayTime, @BoxCount, @PokemonCount,
                @IsValidSave, @RawSaveData, @SavePath)",
            new { Id = saveFileId, UserId = userId, parsed.Filename, parsed.FileSize, parsed.Generation,
                GameVersion = normalizedVersion, parsed.TrainerName, parsed.TrainerId, parsed.SecretId,
                parsed.PlayTime, parsed.BoxCount, parsed.PokemonCount,
                IsValidSave = true, RawSaveData = Array.Empty<byte>(), SavePath = savePath });

        parsed.SaveFileId = saveFileId;
        return parsed;
    }

    public async Task DeleteSave(Guid saveFileId, Guid userId)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        // 清理文件系统
        var canonicalDir = Path.GetDirectoryName(GetSavePath(userId, saveFileId));
        if (canonicalDir != null && Directory.Exists(canonicalDir))
        {
            Directory.Delete(canonicalDir, true);
        }
        if (!string.IsNullOrEmpty(sf.SavePath))
        {
            var legacyDir = Path.GetDirectoryName(sf.SavePath);
            if (!string.IsNullOrEmpty(legacyDir) && legacyDir != canonicalDir && Directory.Exists(legacyDir))
                Directory.Delete(legacyDir, true);
        }
        await _db.ExecuteAsync(
            "DELETE FROM save_files WHERE id = @Id AND user_id = @UserId",
            new { Id = saveFileId, UserId = userId });
    }

    // ═══ 修改操作 ═══════════════════════════════════════

    public async Task MoveSlot(Guid saveFileId, Guid userId,
        int fromBox, int fromSlot, int toBox, int toSlot)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        if (fromBox == toBox)
        {
            var boxData = sav.GetBoxData(fromBox);
            (boxData[fromSlot], boxData[toSlot]) = (boxData[toSlot], boxData[fromSlot]);
            sav.SetBoxData(boxData, fromBox);
        }
        else
        {
            var boxA = sav.GetBoxData(fromBox);
            var boxB = sav.GetBoxData(toBox);
            (boxA[fromSlot], boxB[toSlot]) = (boxB[toSlot], boxA[fromSlot]);
            sav.SetBoxData(boxA, fromBox);
            sav.SetBoxData(boxB, toBox);
        }
        await WriteBackSave(sf, userId, sav);
    }

    /// <summary>
    /// 共享 helper: 将 PKM 写入存档箱子槽位（含 GetCompatiblePKM 兼容转换）
    /// </summary>
    /// <param name="allowOverwrite">true=允许覆盖已占用槽位（拖放等显式选择场景）</param>
    /// <returns>写入后的兼容 PKM 对象（null 表示失败）</returns>
    public PKM? WritePkmToBoxSlot(PKHeX.Core.SaveFile sav, int targetBoxIndex, int targetSlotIndex, PKM pkm, bool allowOverwrite = false)
    {
        var boxData = sav.GetBoxData(targetBoxIndex);
        if (targetSlotIndex < 0 || targetSlotIndex >= boxData.Length)
            throw new BusinessException("槽位索引无效", 400);

        if (!allowOverwrite && boxData[targetSlotIndex].Species != 0)
            throw new BusinessException("目标槽位已被占用", 400);

        var compat = sav.GetCompatiblePKM(pkm);
        if (compat == null)
            throw new BusinessException("宝可梦格式与目标存档不兼容", 400);

        boxData[targetSlotIndex] = compat;
        sav.SetBoxData(boxData, targetBoxIndex);
        return compat;
    }

    public async Task MoveFromBank(Guid saveFileId, Guid userId,
        Guid bankPokemonId, int targetBoxIndex, int targetSlotIndex)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var bankPkm = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankPokemonId, UserId = userId })
            ?? throw new BusinessException("银行宝可梦不存在", 404);

        if (string.IsNullOrEmpty(bankPkm.PkmDataBase64))
            throw new BusinessException("该银行记录缺少原始数据", 400);

        var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(bankPkm.PkmDataBase64))
            ?? throw new BusinessException("无法解析宝可梦数据", 400);

        // 共享 helper（allowOverwrite: 拖放是用户显式选择，允许覆盖已占用槽位）
        WritePkmToBoxSlot(sav, targetBoxIndex, targetSlotIndex, pkm, allowOverwrite: true);

        // 先写存档（失败则银行记录不动）
        await WriteBackSave(sf, userId, sav);

        // 写成功后再删银行记录；删除失败宁可留重复不丢数据
        await _db.ExecuteAsync("DELETE FROM bank_pokemon WHERE id = @Id", new { Id = bankPkm.Id });
        _legalityCache.InvalidateBank(userId);
    }

    public async Task SwapBoxes(Guid saveFileId, Guid userId, int boxA, int boxB)
    {
        if (boxA == boxB) return;
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var dataA = sav.GetBoxData(boxA);
        var dataB = sav.GetBoxData(boxB);
        sav.SetBoxData(dataB, boxA);
        sav.SetBoxData(dataA, boxB);
        await WriteBackSave(sf, userId, sav);
    }

    public async Task SortAllBoxes(Guid saveFileId, Guid userId, string? sortBy)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var sortMethod = GetBoxSortMethod(sortBy);

        for (int boxIndex = 0; boxIndex < sav.BoxCount; boxIndex++)
            sav.SortBoxes(boxIndex, boxIndex, sortMethod);

        await WriteBackSave(sf, userId, sav);
    }

    public async Task SortBox(Guid saveFileId, Guid userId, int boxIndex, string? sortBy)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        if ((uint)boxIndex >= sav.BoxCount)
            throw new BusinessException("箱子索引无效", 400);

        sav.SortBoxes(boxIndex, boxIndex, GetBoxSortMethod(sortBy));
        await WriteBackSave(sf, userId, sav);
    }

    public async Task WriteBoxSlot(Guid saveFileId, Guid userId, int boxIndex, int slotIndex, PKM pkm)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var boxData = sav.GetBoxData(boxIndex);
        if (slotIndex < boxData.Length)
        {
            boxData[slotIndex] = sav.GetCompatiblePKM(pkm);
            sav.SetBoxData(boxData, boxIndex);
            await WriteBackSave(sf, userId, sav);
        }
    }

    public async Task ClearBoxSlot(Guid saveFileId, Guid userId, int boxIndex, int slotIndex)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var boxData = sav.GetBoxData(boxIndex);
        if (slotIndex < boxData.Length)
        {
            boxData[slotIndex] = sav.BlankPKM;
            sav.SetBoxData(boxData, boxIndex);
            await WriteBackSave(sf, userId, sav);
        }
    }

    public PKM? ReadBoxSlot(Guid saveFileId, Guid userId, int boxIndex, int slotIndex)
    {
        var saveFile = LoadSaveFileEntityAsync(saveFileId, userId).Result;
        var rawData = ReadSaveBytes(saveFile, userId);
        PKHeX.Core.SaveFile sav;
        try
        {
            sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);
        }
        catch (BusinessException)
        {
            return null;
        }
        var boxData = sav.GetBoxData(boxIndex);
        if (slotIndex >= boxData.Length) return null;
        var pkm = boxData[slotIndex];
        return pkm.Species > 0 && pkm.Valid ? pkm : null;
    }

    public PKM? ReadPartySlot(Guid saveFileId, Guid userId, int slotIndex)
    {
        var saveFile = LoadSaveFileEntityAsync(saveFileId, userId).Result;
        var rawData = ReadSaveBytes(saveFile, userId);
        PKHeX.Core.SaveFile sav;
        try
        {
            sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);
        }
        catch (BusinessException)
        {
            return null;
        }
        if (slotIndex < 0 || slotIndex >= 6) return null;
        var pkm = sav.GetPartySlotAtIndex(slotIndex);
        return pkm != null && pkm.Species > 0 && pkm.Valid ? pkm : null;
    }

    public (int boxIndex, int slotIndex)? FindPokemonSlot(Guid saveFileId, Guid userId, Guid pokemonDbId) => null;

    // ═══ 备份管理（文件系统）══════════════════════════════

    public async Task<List<SaveBackupDto>> ListBackups(Guid saveFileId, Guid userId)
    {
        await LoadSaveFileEntity(saveFileId, userId);
        var backups = await _db.QueryAsync<SaveBackupEntity>(
            "SELECT * FROM save_backups WHERE save_file_id = @Id ORDER BY created_at DESC LIMIT 5",
            new { Id = saveFileId });

        return backups.Select(b =>
        {
            var dto = new SaveBackupDto { Id = b.Id, SaveFileId = b.SaveFileId, Label = b.Label, CreatedAt = b.CreatedAt };
            try
            {
                var data = ReadBackupBytes(b);
                if (data.Length > 0)
                {
                    var sav = ParseService.OpenSaveFile(data, b.BackupPath ?? b.Label);
                    dto.TrainerName = sav.OT;
                    dto.PokemonCount = sav.BoxCount > 0
                        ? Enumerable.Range(0, sav.BoxCount).Sum(box =>
                            sav.GetBoxData(box).Count(pkm => pkm.Species > 0 && pkm.Valid))
                            + Enumerable.Range(0, 6).Count(i => { var p = sav.GetPartySlotAtIndex(i); return p != null && p.Species > 0; })
                        : 0;
                    dto.PlayTime = $"{(int)sav.PlayedHours}h {(int)sav.PlayedMinutes}m";
                    dto.GameVersion = GameInfo.GetVersionName(sav.Version);
                    dto.BoxCount = sav.BoxCount;
                }
            }
            catch { /* keep defaults */ }
            return dto;
        }).ToList();
    }

    /// <summary>创建备份（写入前自动调用）— 文件系统 + 保留最近 5 份</summary>
    public async Task CreateBackup(Guid saveFileId, Guid userId, string? label = null)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(sf, userId);
        if (rawData.Length == 0) return; // 空存档不备份

        var backupId = Guid.NewGuid();
        var backupDir = GetBackupDir(userId, saveFileId);
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, $"{DateTime.Now:yyyyMMdd-HHmmss}_{backupId.ToString()[..8]}.sav");

        await File.WriteAllBytesAsync(backupPath, rawData);

        await _db.ExecuteAsync(
            "INSERT INTO save_backups (id, save_file_id, label, backup_path, raw_save_data) VALUES (@Id, @SfId, @Label, @Path, @Data)",
            new { Id = backupId, SfId = saveFileId, Label = label ?? $"备份 {DateTime.Now:yyyy-MM-dd HH:mm}", Path = backupPath, Data = Array.Empty<byte>() });

        // 保留最近 5 份
        var oldBackups = await _db.QueryAsync<SaveBackupEntity>(
            "SELECT * FROM save_backups WHERE save_file_id = @Id ORDER BY created_at DESC OFFSET 5",
            new { Id = saveFileId });
        foreach (var old in oldBackups)
        {
            if (!string.IsNullOrEmpty(old.BackupPath) && File.Exists(old.BackupPath))
                File.Delete(old.BackupPath);
            await _db.ExecuteAsync("DELETE FROM save_backups WHERE id = @Id", new { old.Id });
        }
    }

    public async Task RestoreBackup(Guid saveFileId, Guid userId, Guid backupId)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        var backup = await _db.QueryFirstOrDefaultAsync<SaveBackupEntity>(
            "SELECT * FROM save_backups WHERE id = @Id AND save_file_id = @SfId",
            new { Id = backupId, SfId = saveFileId })
            ?? throw new BusinessException("备份不存在", 404);

        var data = ReadBackupBytes(backup);
        if (data.Length == 0)
            throw new BusinessException("备份文件不可用");

        // 恢复前先备份当前
        await CreateBackup(saveFileId, userId, "恢复前自动备份");
        await WriteSaveBytes(sf, userId, data);
        _legalityCache.InvalidateSave(saveFileId);
    }

    // ═══ 下载 / 扫描 ════════════════════════════════════

    public async Task<(byte[] data, string filename)> GetDownloadData(Guid saveFileId, Guid userId)
    {
        var saveFile = await LoadSaveFileEntity(saveFileId, userId);
        return (ReadSaveBytes(saveFile, userId), saveFile.Filename);
    }

    public async Task<BatchLegalityReportDto> BatchLegalityScan(
        Guid saveFileId, Guid userId, PokemonEditService pokemonEditService)
    {
        var saveFile = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(saveFile, userId);
        var hash = ComputeContentHash(rawData);

        // 查缓存
        var cached = _legalityCache.GetSaveReport(saveFileId, hash);
        if (cached != null) return cached;

        var sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);
        var report = pokemonEditService.BatchScan(sav);

        _legalityCache.SetSaveReport(saveFileId, report, hash);
        return report;
    }

    private static string ComputeContentHash(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(data));
    }

    // ═══ 背包编辑（Bag Editor）════════════════════════════

    /// <summary>
    /// 获取存档背包 — 返回 capability 驱动的多 Pouch 道具列表
    /// </summary>
    public async Task<BagDto> GetBag(Guid saveFileId, Guid userId)
    {
        var (_, sav) = await LoadSave(saveFileId, userId);
        // PKHeX v24.3.10: Inventory 返回 IReadOnlyList&lt;InventoryPouch&gt;，Items 直接引用原始数据
        var inventory = sav.Inventory;

        // — capability 检测：通过第一个非空 item 的实际类型判断 —
        var capability = new BagCapability
        {
            MaxItemID = sav.MaxItemID,
        };
        // capability 检测：取第一个 pouch 的第一个 item（即使 index=0 也暴露正确接口）。
        // 不用非空 item 过滤，因为空存档/空背包会误判为全部 false。
        var sampleItem = inventory.Pouches
            .SelectMany(p => p.Items)
            .FirstOrDefault();
        if (sampleItem != null)
        {
            capability.HasFavorite = sampleItem is IItemFavorite;
            capability.HasNewFlag = sampleItem is IItemNewFlag;
            capability.HasFreeSpace = sampleItem is IItemFreeSpace;
        }

        // — 映射 Pouch 列表 —
        var pouches = new List<PouchDto>();
        foreach (var pouch in inventory.Pouches)
        {
            var pouchDto = new PouchDto
            {
                Type = pouch.Type.ToString(),
                MaxCount = pouch.MaxCount,
                Items = pouch.Items.Select(item =>
                {
                    var dto = new BagItemDto
                    {
                        Index = item.Index,
                        Count = item.Count,
                    };
                    if (capability.HasFavorite && item is IItemFavorite fav)
                        dto.IsFavorite = fav.IsFavorite;
                    if (capability.HasNewFlag && item is IItemNewFlag nf)
                        dto.IsNew = nf.IsNew;
                    if (capability.HasFreeSpace && item is IItemFreeSpace fs)
                        dto.IsFreeSpace = fs.IsFreeSpace;
                    return dto;
                }).ToList(),
            };
            pouches.Add(pouchDto);
        }

        return new BagDto { Capability = capability, Pouches = pouches };
    }

    /// <summary>
    /// 保存背包变更 — 将 DTO 写回 PKHeX Inventory 并持久化存档。
    /// InventoryPouch.Items 直接引用原始存档数据，修改即修改存档。
    /// </summary>
    public async Task SaveBag(Guid saveFileId, Guid userId, BagDto dto)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var inventory = sav.Inventory;
        var maxItemID = sav.MaxItemID;

        foreach (var pouchDto in dto.Pouches)
        {
            if (!Enum.TryParse<InventoryType>(pouchDto.Type, out var invType))
                continue;

            var pouch = inventory.Pouches.FirstOrDefault(p => p.Type == invType);
            if (pouch == null)
                continue; // 该存档不支持此 Pouch 类型，跳过

            var items = pouch.Items;
            for (int i = 0; i < Math.Min(pouchDto.Items.Count, items.Length); i++)
            {
                var src = pouchDto.Items[i];
                var dest = items[i];

                // 校验道具 ID：0 表示空格，否则必须在有效范围内
                var idx = src.Index;
                if (idx != 0 && (idx < 0 || idx > maxItemID))
                    idx = 0; // 非法道具 ID → 清空槽位
                dest.Index = idx;

                // 数量：只保证非负，不做袋子级上限钳制。
                // PKHeX 按道具限制（如 Z-Ring 在 KeyItems 袋限 2、TM 和 TR 规则不同），
                // 袋子 MaxCount 不能代表所有道具的上限。
                dest.Count = Math.Max(0, src.Count);

                if (src.IsFavorite.HasValue && dest is IItemFavorite fav)
                    fav.IsFavorite = src.IsFavorite.Value;
                if (src.IsNew.HasValue && dest is IItemNewFlag nf)
                    nf.IsNew = src.IsNew.Value;
                if (src.IsFreeSpace.HasValue && dest is IItemFreeSpace fs)
                    fs.IsFreeSpace = src.IsFreeSpace.Value;
            }
        }

        // 关键：把袋子内容序列化回存档原始数据。
        // InventoryPouch.Items 只是在 GetPouch 时从原始字节读出的快照；
        // 修改 Items 只改了内存对象，必须通过 SetPouch → 每个 pouch 写回 sav.Data 对应偏移。
        inventory.CopyTo(sav);

        await WriteBackSave(sf, userId, sav);
    }

    // ═══ 训练家信息（Trainer Info）════════════════════════

    /// <summary>
    /// 获取训练家完整信息 — capability 驱动
    /// </summary>
    public async Task<TrainerInfoDto> GetTrainerInfo(Guid saveFileId, Guid userId)
    {
        var (_, sav) = await LoadSave(saveFileId, userId);

        // — capability 检测 — 使用强类型适配器替代反射
        var cap = new TrainerCapability
        {
            HasCoins = PkhexSaveAdapters.GetCoin(sav) != null,
            HasBP = PkhexSaveAdapters.GetBP(sav) != null,
            HasLeaguePoints = PkhexSaveAdapters.GetLeaguePoints(sav) != null,
            HasBadges = PkhexSaveAdapters.GetBadges(sav) != null,
            HasGameSync = sav is IGameSync,
            HasTrainerCard = sav is SAV8SWSH,
            HasCardNumber = sav is SAV8SWSH,
            MaxStringLengthTrainer = sav.MaxStringLengthTrainer,
            MaxMoney = sav.MaxMoney,
            TrainerIDFormat = (int)sav.TrainerIDDisplayFormat,
        };

        if (cap.HasBadges)
        {
            var (count, names) = GetBadgeInfo(sav);
            cap.BadgeCount = count;
            cap.BadgeNames = names;
        }

        if (cap.HasCoins)
            cap.MaxCoins = sav.MaxCoins;

        var dto = new TrainerInfoDto
        {
            Capability = cap,
            OT = sav.OT,
            TID16 = sav.TID16,
            SID16 = sav.SID16,
            DisplayTID = sav.DisplayTID,
            DisplaySID = sav.DisplaySID,
            Gender = sav.Gender,
            Language = sav.Language,
            LanguageName = GetLanguageName(sav.Language),
            PlayedHours = sav.PlayedHours,
            PlayedMinutes = sav.PlayedMinutes,
            PlayedSeconds = sav.PlayedSeconds,
            Generation = sav.Generation,
            GameVersionName = GameInfo.GetVersionName(sav.Version),
        };

        dto.Money = sav.Money;
        dto.Coins = PkhexSaveAdapters.GetCoin(sav);
        dto.BP = PkhexSaveAdapters.GetBP(sav);
        dto.LeaguePoints = (int?)PkhexSaveAdapters.GetLeaguePoints(sav);
        dto.Badges = PkhexSaveAdapters.GetBadges(sav);
        dto.CardNumber = PkhexSaveAdapters.GetCardNumber(sav);
        dto.GameSyncID = PkhexSaveAdapters.GetGameSyncID(sav);

        return dto;
    }

    /// <summary>
    /// 保存训练家信息
    /// </summary>
    public async Task SaveTrainerInfo(Guid saveFileId, Guid userId, TrainerInfoDto dto)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);

        // 基本字段 — 带钳制校验
        sav.OT = dto.OT ?? "";
        sav.TID16 = Math.Clamp(dto.TID16, (ushort)0, (ushort)65535);
        sav.SID16 = Math.Clamp(dto.SID16, (ushort)0, (ushort)65535);
        sav.Gender = dto.Gender <= 1 ? dto.Gender : (byte)0;
        sav.Language = dto.Language is >= 1 and <= 10 ? dto.Language : 2;
        sav.PlayedHours = Math.Clamp(dto.PlayedHours, 0, 999);
        sav.PlayedMinutes = Math.Clamp(dto.PlayedMinutes, 0, 59);
        sav.PlayedSeconds = Math.Clamp(dto.PlayedSeconds, 0, 59);

        if (dto.Money.HasValue)
            sav.Money = Math.Clamp(dto.Money.Value, 0u, (uint)sav.MaxMoney);

        // — 货币 — 使用强类型适配器
        var cap = dto.Capability;
        if (cap.HasCoins && dto.Coins.HasValue)
            PkhexSaveAdapters.SetCoin(sav, Math.Clamp(dto.Coins.Value, 0, sav.MaxCoins));
        if (cap.HasBP && dto.BP.HasValue)
            PkhexSaveAdapters.SetBP(sav, Math.Clamp(dto.BP.Value, 0, 99999));
        if (cap.HasLeaguePoints && dto.LeaguePoints.HasValue)
            PkhexSaveAdapters.SetLeaguePoints(sav, (uint)Math.Clamp(dto.LeaguePoints.Value, 0, 99999999));

        // — 徽章 —
        if (cap.HasBadges && dto.Badges.HasValue && cap.BadgeCount > 0)
        {
            var maxMask = (1 << cap.BadgeCount) - 1;
            PkhexSaveAdapters.SetBadges(sav, dto.Badges.Value & maxMask);
        }

        // — 训练家卡片 —
        if (cap.HasCardNumber && dto.CardNumber != null)
            PkhexSaveAdapters.SetCardNumber(sav, dto.CardNumber);

        await WriteBackSave(sf, userId, sav);
    }

    // ═══ 图鉴编辑（Pokédex Editor）═════════════════════

    /// <summary>后端集中判定图鉴可见范围与支持状态 — 唯一数据源，前端不自行映射</summary>
    private static (int visibleMax, bool supported, string? reason) GetDexVisibility(
        int generation, int gameVersion)
    {
        // LA (PKHeX version = 47) — 研究任务体系，V1 不支持
        if (gameVersion == 47)
            return (0, false, "Pokémon Legends: Arceus 的研究任务体系暂不支持，请使用 PKHeX 桌面版编辑图鉴");

        // V1 其余存档均支持；visibleMax=0 表示"使用 MaxSpeciesID"
        return (0, true, null);
    }

    /// <summary>
    /// 获取存档图鉴 — 返回 capability 和 seen/caught 条目列表
    /// </summary>
    public async Task<PokedexDto> GetPokedex(Guid saveFileId, Guid userId)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var gameVersion = sf.GameVersion ?? (int)sav.Version;
        var generation = sf.Generation;

        var (visibleMax, isSupported, reason) = GetDexVisibility(generation, gameVersion);

        var entries = new List<PokedexEntryDto>();
        var maxSpecies = sav.MaxSpeciesID;
        for (ushort i = 1; i <= maxSpecies; i++)
        {
            entries.Add(new PokedexEntryDto
            {
                Species = i,
                Seen = sav.GetSeen(i),
                Caught = sav.GetCaught(i),
            });
        }

        return new PokedexDto
        {
            HasPokeDex = sav.HasPokeDex,
            GameVersion = gameVersion,
            Generation = generation,
            VisibleSpeciesMax = visibleMax,
            IsSupported = isSupported,
            UnsupportedReason = reason,
            TotalSpecies = maxSpecies,
            SeenCount = sav.SeenCount,
            CaughtCount = sav.CaughtCount,
            PercentSeen = sav.PercentSeen,
            PercentCaught = sav.PercentCaught,
            Entries = entries,
        };
    }

    /// <summary>
    /// 保存图鉴变更 — 带去重、物种范围校验、caught⇒seen 归一化
    /// </summary>
    public async Task SavePokedex(Guid saveFileId, Guid userId, PokedexDto dto)
    {
        // 空值保护（ASP.NET 自动 400 已禁用，需手动校验）
        if (dto == null)
            throw new BusinessException("请求体不能为空", 400);

        var (sf, sav) = await LoadSave(saveFileId, userId);
        var maxSpecies = sav.MaxSpeciesID;

        if (dto.Entries == null || dto.Entries.Count == 0)
            throw new BusinessException("图鉴条目列表不能为空", 400);

        // 去重：按 species 取最后一条
        var deduped = dto.Entries
            .GroupBy(e => e.Species)
            .ToDictionary(g => g.Key, g => g.Last());

        foreach (var (species, entry) in deduped)
        {
            if (species < 1 || species > maxSpecies)
                throw new BusinessException($"物种编号 {species} 超出该存档范围 (1-{maxSpecies})", 400);

            // 语义归一化：caught ⇒ seen / !seen ⇒ !caught
            if (entry.Caught) entry.Seen = true;
            if (!entry.Seen) entry.Caught = false;

            sav.SetSeen(species, entry.Seen);
            sav.SetCaught(species, entry.Caught);
        }

        await WriteBackSave(sf, userId, sav);
    }

    /// <summary>
    /// 图鉴批量操作 — seenAll / caughtAll / clearAll
    /// </summary>
    private static readonly HashSet<string> AllowedPokedexBatchActions = new(StringComparer.OrdinalIgnoreCase)
        { "seenAll", "caughtAll", "clearAll" };

    public async Task<PokedexDto> BatchPokedex(Guid saveFileId, Guid userId, string action)
    {
        // 手动判空 + 白名单校验（ASP.NET 自动 400 已禁用）
        if (string.IsNullOrWhiteSpace(action))
            throw new BusinessException("缺少批量操作参数", 400);
        var normalized = action.Trim();
        if (!AllowedPokedexBatchActions.Contains(normalized))
            throw new BusinessException($"不支持的批量操作: {action}，可选: seenAll, caughtAll, clearAll", 400);

        var (sf, sav) = await LoadSave(saveFileId, userId);
        var max = sav.MaxSpeciesID;

        switch (normalized.ToLowerInvariant())
        {
            case "seenall":
                for (ushort i = 1; i <= max; i++)
                    sav.SetSeen(i, true);
                break;
            case "caughtall":
                for (ushort i = 1; i <= max; i++)
                {
                    sav.SetSeen(i, true);
                    sav.SetCaught(i, true);
                }
                break;
            case "clearall":
                for (ushort i = 1; i <= max; i++)
                {
                    sav.SetSeen(i, false);
                    sav.SetCaught(i, false);
                }
                break;
        }

        await WriteBackSave(sf, userId, sav);
        return await GetPokedex(saveFileId, userId);
    }

    // ═══ 世代专属工具（Gen Tools）════════════════════════

    /// <summary>
    /// O-Power 元数据 — 将 DTO key ↔ 中文名 ↔ 分类 ↔ PKHeX 枚举绑定在一起。
    /// 读写均由此表驱动，不手写 if/switch。
    /// </summary>
    private sealed record OPowerMeta(
        string Key, string Name, string Category,
        OPower6FieldType? FieldType, OPower6BattleType? BattleType,
        OPower6Index IdxLv1, OPower6Index IdxLv2, OPower6Index IdxLv3,
        OPower6Index? IdxS, OPower6Index? IdxMax);

    private static readonly OPowerMeta[] OPowerMetaTable =
    [
        // ── Field (10) ──
        new("hatching",    "孵化",   "field", OPower6FieldType.Hatching,    null, OPower6Index.Hatching1,    OPower6Index.Hatching2,    OPower6Index.Hatching3,    OPower6Index.HatchingS, OPower6Index.HatchingMAX),
        new("bargain",     "打折",   "field", OPower6FieldType.Bargain,     null, OPower6Index.Bargain1,     OPower6Index.Bargain2,     OPower6Index.Bargain3,     OPower6Index.BargainS,  OPower6Index.BargainMAX),
        new("prizeMoney",  "奖金",   "field", OPower6FieldType.PrizeMoney,  null, OPower6Index.PrizeMoney1,  OPower6Index.PrizeMoney2,  OPower6Index.PrizeMoney3,  OPower6Index.PrizeMoneyS, OPower6Index.PrizeMoneyMAX),
        new("experience",  "经验",   "field", OPower6FieldType.Experience,  null, OPower6Index.Experience1,  OPower6Index.Experience2,  OPower6Index.Experience3,  OPower6Index.ExperienceS, OPower6Index.ExperienceMAX),
        new("capture",     "捕获",   "field", OPower6FieldType.Capture,     null, OPower6Index.Capture1,     OPower6Index.Capture2,     OPower6Index.Capture3,     OPower6Index.CaptureS,   OPower6Index.CaptureMAX),
        new("encounter",   "遭遇",   "field", OPower6FieldType.Encounter,   null, OPower6Index.Encounter1,   OPower6Index.Encounter2,   OPower6Index.Encounter3,   null, null),
        new("stealth",     "潜行",   "field", OPower6FieldType.Stealth,     null, OPower6Index.Stealth1,     OPower6Index.Stealth2,     OPower6Index.Stealth3,     null, null),
        new("hpRestoring", "HP回复", "field", OPower6FieldType.HPRestoring, null, OPower6Index.HPRestoring1, OPower6Index.HPRestoring2, OPower6Index.HPRestoring3, null, null),
        new("ppRestoring", "PP回复", "field", OPower6FieldType.PPRestoring, null, OPower6Index.PPRestoring1, OPower6Index.PPRestoring2, OPower6Index.PPRestoring3, null, null),
        new("befriending", "友好",   "field", OPower6FieldType.Befriending, null, OPower6Index.Befriending1, OPower6Index.Befriending2, OPower6Index.Befriending3, OPower6Index.BefriendingS, OPower6Index.BefriendingMAX),
        // ── Battle (7) ──
        new("attack",      "攻击",   "battle", null, OPower6BattleType.Attack,     OPower6Index.Attack1,       OPower6Index.Attack2,       OPower6Index.Attack3,       null, null),
        new("defense",     "防御",   "battle", null, OPower6BattleType.Defense,    OPower6Index.Defense1,      OPower6Index.Defense2,      OPower6Index.Defense3,      null, null),
        new("spAttack",    "特攻",   "battle", null, OPower6BattleType.Sp_Attack,  OPower6Index.SpecialAttack1,  OPower6Index.SpecialAttack2,  OPower6Index.SpecialAttack3,  null, null),
        new("spDefense",   "特防",   "battle", null, OPower6BattleType.Sp_Defense, OPower6Index.SpecialDefense1, OPower6Index.SpecialDefense2, OPower6Index.SpecialDefense3, null, null),
        new("speed",       "速度",   "battle", null, OPower6BattleType.Speed,      OPower6Index.Speed1,        OPower6Index.Speed2,        OPower6Index.Speed3,        null, null),
        new("critical",    "会心",   "battle", null, OPower6BattleType.Critical,   OPower6Index.Critical1,     OPower6Index.Critical2,     OPower6Index.Critical3,     null, null),
        new("accuracy",    "命中",   "battle", null, OPower6BattleType.Accuracy,   OPower6Index.Accuracy1,     OPower6Index.Accuracy2,     OPower6Index.Accuracy3,     null, null),
    ];

    /// <summary>
    /// 获取存档世代专属工具数据 — RTC（Gen3 RS/Emerald）+ O-Power（Gen6 XY/ORAS）。
    /// </summary>
    public async Task<GenToolsDto> GetGenTools(Guid saveFileId, Guid userId)
    {
        var (_, sav) = await LoadSave(saveFileId, userId);
        var dto = new GenToolsDto();
        var cap = new GenToolsCapability();

        // ── RTC (Gen3 Hoenn) ──
        var (clockInitial, clockElapsed) = PkhexSaveAdapters.GetRTC3(sav);
        cap.HasRtc = clockInitial != null;

        if (cap.HasRtc)
        {
            dto.RtcEntries =
            [
                new Rtc3EntryDto
                {
                    Key = "initial", Label = "初始时钟",
                    Day = clockInitial!.Day, Hour = clockInitial.Hour,
                    Minute = clockInitial.Minute, Second = clockInitial.Second,
                },
                new Rtc3EntryDto
                {
                    Key = "elapsed", Label = "已流逝时钟",
                    Day = clockElapsed!.Day, Hour = clockElapsed.Hour,
                    Minute = clockElapsed.Minute, Second = clockElapsed.Second,
                },
            ];
        }

        // ── O-Power (Gen6 XY/ORAS) ──
        var oPower = PkhexSaveAdapters.GetOPower(sav);
        cap.HasOPowers = oPower != null;

        if (oPower != null)
        {
            var entries = new List<OPowerTypeEntryDto>(OPowerMetaTable.Length);
            foreach (var m in OPowerMetaTable)
            {
                var entry = new OPowerTypeEntryDto
                {
                    Key = m.Key,
                    Name = m.Name,
                    Category = m.Category,
                    Level1 = m.FieldType != null
                        ? oPower.GetLevel1(m.FieldType.Value)
                        : oPower.GetLevel1(m.BattleType!.Value),
                    Level2 = m.FieldType != null
                        ? oPower.GetLevel2(m.FieldType.Value)
                        : oPower.GetLevel2(m.BattleType!.Value),
                    Level1Unlocked = oPower.GetState(m.IdxLv1) == OPowerFlagState.Unlocked,
                    Level2Unlocked = oPower.GetState(m.IdxLv2) == OPowerFlagState.Unlocked,
                    Level3Unlocked = oPower.GetState(m.IdxLv3) == OPowerFlagState.Unlocked,
                    HasLevelS = m.IdxS != null,
                    LevelSUnlocked = m.IdxS != null && oPower.GetState(m.IdxS.Value) == OPowerFlagState.Unlocked,
                    HasLevelMax = m.IdxMax != null,
                    LevelMaxUnlocked = m.IdxMax != null && oPower.GetState(m.IdxMax.Value) == OPowerFlagState.Unlocked,
                };
                entries.Add(entry);
            }
            dto.OPower = new OPowerDto
            {
                Points = oPower.Points,
                EnableUnlocked = oPower.GetState(OPower6Index.Enable) == OPowerFlagState.Unlocked,
                FullRecoveryUnlocked = oPower.GetState(OPower6Index.FullRecovery) == OPowerFlagState.Unlocked,
                Entries = entries,
            };
        }

        dto.Capability = cap;
        return dto;
    }

    /// <summary>
    /// 保存世代专属工具数据 — RTC + O-Power。
    /// </summary>
    public async Task SaveGenTools(Guid saveFileId, Guid userId, GenToolsDto dto)
    {
        // 空值保护（ASP.NET 自动 400 已禁用，需手动校验）
        if (dto == null)
            throw new BusinessException("请求体不能为空", 400);

        var (sf, sav) = await LoadSave(saveFileId, userId);

        // ── RTC (Gen3 Hoenn) ──
        if (dto.RtcEntries is { Count: > 0 })
        {
            var (clockInitial, clockElapsed) = PkhexSaveAdapters.GetRTC3(sav);
            foreach (var entry in dto.RtcEntries)
            {
                RTC3? target = entry.Key switch
                {
                    "initial" => clockInitial,
                    "elapsed" => clockElapsed,
                    _ => null,
                };
                if (target == null) continue;
                target.Day    = Math.Clamp(entry.Day,    0, 65535);
                target.Hour   = Math.Clamp(entry.Hour,   0, 23);
                target.Minute = Math.Clamp(entry.Minute, 0, 59);
                target.Second = Math.Clamp(entry.Second, 0, 59);
            }
        }

        // ── O-Power (Gen6 XY/ORAS) ──
        if (dto.OPower != null)
        {
            var oPower = PkhexSaveAdapters.GetOPower(sav);
            if (oPower != null)
            {
                // Points
                oPower.Points = (byte)Math.Clamp(dto.OPower.Points, 0, 255);
                // Enable + FullRecovery
                oPower.SetState(OPower6Index.Enable,
                    dto.OPower.EnableUnlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                oPower.SetState(OPower6Index.FullRecovery,
                    dto.OPower.FullRecoveryUnlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                // Entries — 仅按已知 key 匹配，未知 key 忽略
                if (dto.OPower.Entries is { Count: > 0 })
                {
                    foreach (var entry in dto.OPower.Entries)
                    {
                        var meta = Array.Find(OPowerMetaTable, m => m.Key == entry.Key);
                        if (meta == null) continue; // 忽略未知 key
                        // Level
                        byte lv1 = (byte)Math.Clamp(entry.Level1, 0, 3);
                        byte lv2 = (byte)Math.Clamp(entry.Level2, 0, 3);
                        if (meta.FieldType != null)
                        {
                            oPower.SetLevel1(meta.FieldType.Value, lv1);
                            oPower.SetLevel2(meta.FieldType.Value, lv2);
                        }
                        else if (meta.BattleType != null)
                        {
                            oPower.SetLevel1(meta.BattleType.Value, lv1);
                            oPower.SetLevel2(meta.BattleType.Value, lv2);
                        }
                        // Flags — 通过 SetState + OPowerFlagState 枚举写入
                        oPower.SetState(meta.IdxLv1,
                            entry.Level1Unlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                        oPower.SetState(meta.IdxLv2,
                            entry.Level2Unlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                        oPower.SetState(meta.IdxLv3,
                            entry.Level3Unlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                        if (meta.IdxS != null)
                            oPower.SetState(meta.IdxS.Value,
                                entry.LevelSUnlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                        if (meta.IdxMax != null)
                            oPower.SetState(meta.IdxMax.Value,
                                entry.LevelMaxUnlocked ? OPowerFlagState.Unlocked : OPowerFlagState.Locked);
                    }
                }
            }
        }

        await WriteBackSave(sf, userId, sav);
    }

    // ── Trainer 辅助 ──────────────────────────────────

    /// <summary>语言 ID → 中文名称</summary>
    private static string? GetLanguageName(int langId) => langId switch
    {
        1 => "日本語",
        2 => "English",
        3 => "Français",
        4 => "Italiano",
        5 => "Deutsch",
        7 => "Español",
        8 => "한국어",
        9 => "简体中文",
        10 => "繁體中文",
        _ => langId > 0 ? $"Language {langId}" : null,
    };

    /// <summary>根据游戏版本返回 (徽章总数, 按 bit 顺序的徽章名称列表)</summary>
    private static (int count, string[] names) GetBadgeInfo(PKHeX.Core.SaveFile sav)
    {
        var version = sav.Version;
        // Kanto (Gen1 RBY, Gen2 GSC second set, Gen3 FRLG, Gen4 HGSS second set)
        string[] kanto = ["灰色徽章", "蓝色徽章", "橙色徽章", "彩虹徽章", "粉色徽章", "金色徽章", "深红徽章", "绿色徽章"];
        // Johto (Gen2 GSC, Gen4 HGSS first set)
        string[] johto = ["飞翼徽章", "昆虫徽章", "普通徽章", "鬼魂徽章", "打击徽章", "矿物徽章", "冰河徽章", "升龙徽章"];
        // Hoenn (Gen3 RS, Gen3 E, Gen6 ORAS)
        string[] hoenn = ["岩石徽章", "拳击徽章", "电力徽章", "火焰徽章", "天平徽章", "羽毛徽章", "心灵徽章", "雨滴徽章"];
        // Sinnoh (Gen4 DP, Gen4 Pt, Gen8 BDSP)
        string[] sinnoh = ["石炭徽章", "森林徽章", "圆石徽章", "沼泽徽章", "遗迹徽章", "矿山徽章", "冰柱徽章", "灯塔徽章"];
        // Kalos (Gen6 XY)
        string[] kalos = ["虫虫徽章", "岩壁徽章", "格斗徽章", "植物徽章", "电压徽章", "妖精徽章", "超能徽章", "冰山徽章"];
        // Galar (Gen8 SwSh)
        string[] galar = ["草之徽章", "水之徽章", "火之徽章", "格斗徽章", "妖精徽章", "岩石徽章", "恶之徽章", "龙之徽章"];

        return version switch
        {
            // Gen1 RBY/G — Kanto only, 8 badges
            GameVersion.RD or GameVersion.GN or GameVersion.BU or GameVersion.YW => (8, kanto),

            // Gen2 GSC — 8 Johto + 8 Kanto = 16
            GameVersion.GD or GameVersion.SI or GameVersion.C => (16, [..johto, ..kanto]),

            // Gen3 RS/E — Hoenn 8
            GameVersion.S or GameVersion.R or GameVersion.E => (8, hoenn),
            // Gen3 FRLG — Kanto 8
            GameVersion.FR or GameVersion.LG => (8, kanto),
            // Gen3 CXD — Colosseum/XD, no traditional badges but HasBadges may be true
            GameVersion.CXD => (0, []),

            // Gen4 DP/Pt — Sinnoh 8
            GameVersion.D or GameVersion.P or GameVersion.Pt => (8, sinnoh),
            // Gen4 HGSS — 8 Johto + 8 Kanto = 16
            GameVersion.HG or GameVersion.SS => (16, [..johto, ..kanto]),

            // Gen6 XY — Kalos 8
            GameVersion.X or GameVersion.Y => (8, kalos),
            // Gen6 ORAS — Hoenn 8
            GameVersion.OR or GameVersion.AS => (8, hoenn),

            // Gen8 SwSh — Galar 8
            GameVersion.SW or GameVersion.SH => (8, galar),
            // Gen8 BDSP — Sinnoh 8
            GameVersion.BD or GameVersion.SP => (8, sinnoh),

            // Default — try to infer from generation
            _ => sav.Generation switch
            {
                1 => (8, kanto),
                3 => (8, hoenn),
                _ => (PkhexSaveAdapters.GetBadges(sav) != null ? 8 : 0, []),
            },
        };
    }

    // ═══ 内部辅助 ═════════════════════════════════════════

    private async Task<SaveFileEntity> LoadSaveFileEntity(Guid saveFileId, Guid userId) =>
        await _db.QueryFirstOrDefaultAsync<SaveFileEntity>(
            "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
            new { Id = saveFileId, UserId = userId })
            ?? throw new BusinessException("存档不存在", 404);

    private Task<SaveFileEntity> LoadSaveFileEntityAsync(Guid saveFileId, Guid userId) =>
        LoadSaveFileEntity(saveFileId, userId);

    internal async Task<(SaveFileEntity, PKHeX.Core.SaveFile)> LoadSave(Guid saveFileId, Guid userId)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(sf, userId);
        var sav = ParseService.OpenSaveFile(rawData, sf.Filename);
        return (sf, sav);
    }

    /// <summary>写入存档：先自动备份，再写入文件系统</summary>
    internal async Task WriteBackSave(SaveFileEntity sf, Guid userId, PKHeX.Core.SaveFile sav)
    {
        // 写入前自动备份
        await CreateBackup(sf.Id, userId, "编辑前自动备份");
        var originalData = ReadSaveBytes(sf, userId);
        var data = ParseService.FinalizeSaveBytes(sav, originalData);
        ValidateWrittenSave(data, sf.Filename);
        await WriteSaveBytes(sf, userId, data);
        _legalityCache.InvalidateSave(sf.Id);
    }

    private byte[] ReadBackupBytes(SaveBackupEntity backup)
    {
        if (!string.IsNullOrEmpty(backup.BackupPath) && File.Exists(backup.BackupPath))
            return File.ReadAllBytes(backup.BackupPath);
        if (backup.RawSaveData is { Length: > 0 })
            return backup.RawSaveData;
        return Array.Empty<byte>();
    }

    private static Func<IEnumerable<PKM>, int, IEnumerable<PKM>> GetBoxSortMethod(string? sortBy)
    {
        var normalized = sortBy?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "species" => (list, _) => list.OrderBySpecies(),
            "level" => (list, _) => list.OrderByDescendingLevel(),
            "shiny" => (list, _) => list.OrderByCustom(pk => pk.IsShiny ? 0 : 1, pk => pk.Species),
            "name" => (list, _) => OrderByName(list),
            _ => throw new BusinessException("不支持的排序方式", 400),
        };
    }

    private static IEnumerable<PKM> OrderByName(IEnumerable<PKM> list)
    {
        var speciesNames = GameInfo.GetStrings("zh").Species;
        var max = speciesNames.Count - 1;
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("zh-CN"), true);

        string GetSortName(PKM pk)
        {
            if (pk.IsNicknamed && !string.IsNullOrWhiteSpace(pk.Nickname))
                return pk.Nickname;
            if (pk.Species > 0 && pk.Species <= max)
                return speciesNames[pk.Species];
            return pk.Nickname ?? string.Empty;
        }

        return list
            .OrderBy(pk => pk.Species == 0)
            .ThenBy(pk => pk.IsEgg)
            .ThenBy(GetSortName, comparer)
            .ThenBy(pk => pk.Species)
            .ThenBy(pk => pk.Form)
            .ThenBy(pk => pk.Gender)
            .ThenBy(pk => pk.IsNicknamed);
    }

    // ═══ 映射 ═════════════════════════════════════════════

    private static SaveFileDto MapToDto(SaveFileEntity entity) => new()
    {
        SaveFileId = entity.Id, Filename = entity.Filename, FileSize = entity.FileSize,
        Generation = entity.Generation, GameVersion = entity.GameVersion,
        GameVersionName = GetVersionNameSafe(entity.GameVersion),
        TrainerName = entity.TrainerName, TrainerId = entity.TrainerId, SecretId = entity.SecretId,
        PlayTime = entity.PlayTime, BoxCount = entity.BoxCount, PokemonCount = entity.PokemonCount,
        IsModified = entity.IsModified, CreatedAt = entity.CreatedAt, UpdatedAt = entity.UpdatedAt
    };

    private static string? GetVersionNameSafe(int? version)
    {
        if (version == null) return null;
        try { return GameInfo.GetVersionName((GameVersion)version.Value); }
        catch { return $"Version {version}"; }
    }
}
