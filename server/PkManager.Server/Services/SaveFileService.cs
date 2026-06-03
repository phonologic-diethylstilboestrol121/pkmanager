using Dapper;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using PKHeX.Core;
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

    public SaveFileService(NpgsqlConnection db, ParseService parseService, IWebHostEnvironment env)
    {
        _db = db;
        _parseService = parseService;
        _baseSaveDir = Path.Combine(env.ContentRootPath, "data", "saves");
    }

    // ═══ 文件系统辅助 ════════════════════════════════════

    private string GetSaveDir(Guid userId, Guid saveFileId) =>
        Path.Combine(_baseSaveDir, userId.ToString(), saveFileId.ToString());

    private string GetSavePath(Guid userId, Guid saveFileId) =>
        Path.Combine(GetSaveDir(userId, saveFileId), "save.sav");

    private string GetBackupDir(Guid userId, Guid saveFileId) =>
        Path.Combine(GetSaveDir(userId, saveFileId), "backups");

    /// <summary>读取存档二进制：优先文件系统，回退 DB（旧数据兼容）</summary>
    private byte[] ReadSaveBytes(SaveFileEntity entity)
    {
        // 文件系统优先
        if (!string.IsNullOrEmpty(entity.SavePath) && File.Exists(entity.SavePath))
            return File.ReadAllBytes(entity.SavePath);

        // 回退：DB 中的旧数据（迁移过渡期）
        if (entity.RawSaveData is { Length: > 0 })
            return entity.RawSaveData;

        return Array.Empty<byte>();
    }

    /// <summary>写入存档到文件系统，首次写入时更新 DB 路径</summary>
    private async Task WriteSaveBytes(SaveFileEntity entity, Guid userId, byte[] data)
    {
        var savePath = entity.SavePath;
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = GetSavePath(userId, entity.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            await _db.ExecuteAsync(
                "UPDATE save_files SET save_path = @P WHERE id = @Id",
                new { P = savePath, Id = entity.Id });
        }
        await File.WriteAllBytesAsync(savePath, data);
        await _db.ExecuteAsync(
            "UPDATE save_files SET file_size = @S, is_modified = TRUE, updated_at = NOW() WHERE id = @Id",
            new { S = (long)data.Length, Id = entity.Id });
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

        var rawData = ReadSaveBytes(saveFile);

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
        if (!string.IsNullOrEmpty(sf.SavePath))
        {
            var dir = Path.GetDirectoryName(sf.SavePath);
            if (dir != null && Directory.Exists(dir))
                Directory.Delete(dir, true);
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

    public async Task MoveFromBank(Guid saveFileId, Guid userId,
        Guid bankPokemonId, int targetBoxIndex, int targetSlotIndex)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var bankPkm = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankPokemonId, UserId = userId })
            ?? throw new BusinessException("银行宝可梦不存在", 404);

        var boxData = sav.GetBoxData(targetBoxIndex);
        if (!string.IsNullOrEmpty(bankPkm.PkmDataBase64))
        {
            var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(bankPkm.PkmDataBase64));
            if (pkm != null) boxData[targetSlotIndex] = pkm;
        }
        sav.SetBoxData(boxData, targetBoxIndex);
        await _db.ExecuteAsync("DELETE FROM bank_pokemon WHERE id = @Id", new { Id = bankPkm.Id });
        await WriteBackSave(sf, userId, sav);
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

    public async Task WriteBoxSlot(Guid saveFileId, Guid userId, int boxIndex, int slotIndex, PKM pkm)
    {
        var (sf, sav) = await LoadSave(saveFileId, userId);
        var boxData = sav.GetBoxData(boxIndex);
        if (slotIndex < boxData.Length)
        {
            boxData[slotIndex] = pkm;
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
        var rawData = ReadSaveBytes(saveFile);
        var sav = SaveUtil.GetVariantSAV(rawData);
        if (sav == null) return null;
        var boxData = sav.GetBoxData(boxIndex);
        if (slotIndex >= boxData.Length) return null;
        var pkm = boxData[slotIndex];
        return pkm.Species > 0 && pkm.Valid ? pkm : null;
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
                    var sav = SaveUtil.GetVariantSAV(data);
                    if (sav != null)
                    {
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
            }
            catch { /* keep defaults */ }
            return dto;
        }).ToList();
    }

    /// <summary>创建备份（写入前自动调用）— 文件系统 + 保留最近 5 份</summary>
    public async Task CreateBackup(Guid saveFileId, Guid userId, string? label = null)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(sf);
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
    }

    // ═══ 下载 / 扫描 ════════════════════════════════════

    public async Task<(byte[] data, string filename)> GetDownloadData(Guid saveFileId, Guid userId)
    {
        var saveFile = await LoadSaveFileEntity(saveFileId, userId);
        return (ReadSaveBytes(saveFile), saveFile.Filename);
    }

    public async Task<BatchLegalityReportDto> BatchLegalityScan(
        Guid saveFileId, Guid userId, PokemonEditService pokemonEditService)
    {
        var saveFile = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(saveFile);
        var sav = SaveUtil.GetVariantSAV(rawData)
            ?? throw new BusinessException("无法解析存档格式");
        return pokemonEditService.BatchScan(sav);
    }

    // ═══ 内部辅助 ═════════════════════════════════════════

    private async Task<SaveFileEntity> LoadSaveFileEntity(Guid saveFileId, Guid userId) =>
        await _db.QueryFirstOrDefaultAsync<SaveFileEntity>(
            "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
            new { Id = saveFileId, UserId = userId })
            ?? throw new BusinessException("存档不存在", 404);

    private Task<SaveFileEntity> LoadSaveFileEntityAsync(Guid saveFileId, Guid userId) =>
        LoadSaveFileEntity(saveFileId, userId);

    private async Task<(SaveFileEntity, PKHeX.Core.SaveFile)> LoadSave(Guid saveFileId, Guid userId)
    {
        var sf = await LoadSaveFileEntity(saveFileId, userId);
        var rawData = ReadSaveBytes(sf);
        var sav = SaveUtil.GetVariantSAV(rawData)
            ?? throw new BusinessException("无法解析存档格式");
        return (sf, sav);
    }

    /// <summary>写入存档：先自动备份，再写入文件系统</summary>
    private async Task WriteBackSave(SaveFileEntity sf, Guid userId, PKHeX.Core.SaveFile sav)
    {
        // 写入前自动备份
        await CreateBackup(sf.Id, userId, "编辑前自动备份");
        var data = sav.Write();
        await WriteSaveBytes(sf, userId, data);
    }

    private byte[] ReadBackupBytes(SaveBackupEntity backup)
    {
        if (!string.IsNullOrEmpty(backup.BackupPath) && File.Exists(backup.BackupPath))
            return File.ReadAllBytes(backup.BackupPath);
        if (backup.RawSaveData is { Length: > 0 })
            return backup.RawSaveData;
        return Array.Empty<byte>();
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
