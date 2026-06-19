using System.IO.Compression;
using System.Text.Json;
using Dapper;
using Npgsql;
using PKHeX.Core;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Entity;
using PkManager.Server.Models.Request;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Services;

/// <summary>
/// 个人宝可梦银行服务 — 增删改查、从存档导入
/// </summary>
public class BankService
{
    private readonly NpgsqlConnection _db;
    private readonly ParseService _parseService;
    private readonly SaveFileService _saveFileService;
    private readonly PokemonEditService _editService;
    private readonly LegalityCacheService _legalityCache;
    private readonly IPkhexStringProvider _pkhexStrings;

    public BankService(NpgsqlConnection db, ParseService parseService, SaveFileService saveFileService,
        PokemonEditService editService, LegalityCacheService legalityCache, IPkhexStringProvider pkhexStrings)
    {
        _db = db;
        _parseService = parseService;
        _saveFileService = saveFileService;
        _editService = editService;
        _legalityCache = legalityCache;
        _pkhexStrings = pkhexStrings;
    }

    /// <summary>
    /// 查询银行列表（分页+筛选+搜索）
    /// </summary>
    public async Task<BankListResult> GetBankList(Guid userId, BankFilter filter)
    {
        var where = "WHERE user_id = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (filter.Generation.HasValue)
        {
            where += " AND generation = @Generation";
            parameters.Add("Generation", filter.Generation.Value);
        }

        if (filter.IsShiny.HasValue)
        {
            where += " AND is_shiny = @IsShiny";
            parameters.Add("IsShiny", filter.IsShiny.Value);
        }

        if (filter.Nature.HasValue)
        {
            where += " AND nature = @Nature";
            parameters.Add("Nature", filter.Nature.Value);
        }

        if (filter.Ability.HasValue)
        {
            where += " AND ability = @Ability";
            parameters.Add("Ability", filter.Ability.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where += " AND (species_name ILIKE @Search OR nickname ILIKE @Search)";
            parameters.Add("Search", $"%{filter.Search}%");
        }

        // 排序（白名单防注入）
        var orderBy = filter.SortBy switch
        {
            "level" => "level",
            "species" => "species",
            _ => "created_at"
        };
        var dir = filter.SortAsc ? "ASC" : "DESC";

        // 计数
        var countSql = $"SELECT COUNT(*) FROM bank_pokemon {where}";
        var total = await _db.ExecuteScalarAsync<int>(countSql, parameters);

        // 分页查询
        var page = filter.Page;
        var pageSize = filter.PageSize;
        var offset = (page - 1) * pageSize;

        var dataSql = $@"
            SELECT id, species, species_name AS SpeciesName, nickname, level,
                   nature_name AS NatureName, ability_name AS AbilityName,
                   generation, game_version AS GameVersion, is_shiny AS IsShiny,
                   is_egg AS IsEgg, is_valid AS IsValid,
                   COALESCE((pokemon_json->>'isAlpha')::boolean, FALSE) AS IsAlpha,
                   COALESCE((pokemon_json->>'canGigantamax')::boolean, FALSE) AS CanGigantamax,
                   NULLIF(pokemon_json->>'heldItemName', '') AS HeldItemName,
                   source, source_save_id AS SourceSaveId, notes,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM bank_pokemon {where}
            ORDER BY {orderBy} {dir}, id ASC
            LIMIT @PageSize OFFSET @Offset";

        parameters.Add("PageSize", pageSize);
        parameters.Add("Offset", offset);

        var items = (await _db.QueryAsync<BankPokemonDto>(dataSql, parameters)).ToList();

        return new BankListResult
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        };
    }

    /// <summary>
    /// 获取银行中单只宝可梦详情
    /// </summary>
    public async Task<PokemonDto?> GetBankDetail(Guid bankPokemonId, Guid userId)
    {
        var record = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankPokemonId, UserId = userId });

        if (record == null) return null;

        // Prefer authoritative PKM binary over JSON snapshot
        if (!string.IsNullOrEmpty(record.PkmDataBase64))
        {
            try
            {
                var pokemon = _parseService.MapToPokemonDto(
                    EntityFormat.GetFromBytes(Convert.FromBase64String(record.PkmDataBase64))
                    ?? throw new InvalidOperationException("PKM parse returned null"));
                pokemon.Id = record.Id;
                return pokemon;
            }
            catch { /* fall back to pokemon_json */ }
        }

        // Fallback: deserialize pokemon_json (legacy / corrupt pkm_data_base64)
        var pokemonJson = JsonSerializer.Deserialize<PokemonDto>(record.PokemonJson ?? "{}");
        if (pokemonJson != null)
        {
            pokemonJson.Id = record.Id;
            // Clear PkmDataBase64 so the frontend correctly shows read-only;
            // the original binary either doesn't exist or failed to parse above.
            pokemonJson.PkmDataBase64 = null;
            pokemonJson.Format = 0;
            pokemonJson.IsValid = false;
        }
        return pokemonJson;
    }

    /// <summary>
    /// 保存银行宝可梦编辑 — 回写 PKM 二进制 + 同步所有冗余列
    /// </summary>
    public async Task<EditResultDto> SaveBankPokemon(Guid bankId, Guid userId, PokemonEditRequest request)
    {
        var record = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankId, UserId = userId })
            ?? throw BusinessException.FromKey("bank.notFound", 404);

        if (string.IsNullOrEmpty(record.PkmDataBase64))
            throw BusinessException.FromKey("bank.rawDataMissingReadonly", 400);

        var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(record.PkmDataBase64))
            ?? throw BusinessException.FromKey("parse.rebuildFailed", 400);

        // Apply edits (reuse PokemonEditService)
        var result = _editService.ApplyEdits(pkm, request);

        // Re-serialize PKM
        var buf = new byte[pkm.SIZE_PARTY]; pkm.WriteDecryptedDataParty(buf);
        var newBase64 = Convert.ToBase64String(buf);

        // Re-generate DTO from edited PKM
        var dto = _parseService.MapToPokemonDto(pkm);
        var pokemonJson = JsonSerializer.Serialize(dto);

        // Sync all redundant columns (list/filter/card summary stays in sync)
        var strings = _pkhexStrings.GetStrings();
        await _db.ExecuteAsync(@"
            UPDATE bank_pokemon SET
                species = @Species, species_name = @SpeciesName, nickname = @Nickname,
                level = @Level, nature = @Nature, nature_name = @NatureName,
                ability = @Ability, ability_name = @AbilityName,
                generation = @Generation, game_version = @GameVersion,
                is_shiny = @IsShiny, is_egg = @IsEgg, is_valid = @IsValid,
                pokemon_json = @PokemonJson::jsonb, pkm_data_base64 = @PkmDataBase64,
                updated_at = NOW()
            WHERE id = @Id AND user_id = @UserId",
            new
            {
                Id = bankId, UserId = userId,
                Species = (int)pkm.Species,
                SpeciesName = strings.Species[pkm.Species],
                Nickname = pkm.Nickname,
                Level = (int)pkm.CurrentLevel,
                Nature = (int)pkm.Nature,
                NatureName = strings.Natures[(int)pkm.Nature],
                Ability = (int)pkm.Ability,
                AbilityName = strings.Ability[pkm.Ability],
                Generation = pkm.Format,
                GameVersion = (int)pkm.Version,
                IsShiny = pkm.IsShiny,
                IsEgg = pkm.IsEgg,
                IsValid = result.Status == LegalityStatus.Legal,
                PokemonJson = pokemonJson,
                PkmDataBase64 = newBase64
            });

        result.UpdatedPokemon = dto;
        _legalityCache.InvalidateBank(userId);
        return result;
    }

    /// <summary>
    /// 单只宝可梦发送到存档（走共享 helper，失败安全顺序）
    /// </summary>
    public async Task MoveSingleToSave(Guid bankId, Guid userId, Guid saveFileId, int targetBoxIndex, int? targetSlotIndex)
    {
        var record = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankId, UserId = userId })
            ?? throw BusinessException.FromKey("bank.notFound", 404);

        if (string.IsNullOrEmpty(record.PkmDataBase64))
            throw BusinessException.FromKey("bank.rawDataMissing", 400);

        var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(record.PkmDataBase64))
            ?? throw BusinessException.FromKey("parse.rebuildFailed", 400);

        var (sf, sav) = await _saveFileService.LoadSave(saveFileId, userId);

        // Auto-find empty slot if not specified
        int slot;
        if (targetSlotIndex.HasValue)
        {
            slot = targetSlotIndex.Value;
        }
        else
        {
            var boxData = sav.GetBoxData(targetBoxIndex);
            slot = -1;
            for (int i = 0; i < boxData.Length; i++)
            {
                if (boxData[i].Species == 0) { slot = i; break; }
            }
            if (slot < 0) throw BusinessException.FromKey("bank.targetBoxFull", 400);
        }

        // Shared helper: GetCompatiblePKM + empty check + write
        _saveFileService.WritePkmToBoxSlot(sav, targetBoxIndex, slot, pkm);

        // Write save first
        await _saveFileService.WriteBackSave(sf, userId, sav);

        // Only delete bank record after successful save write
        await _db.ExecuteAsync("DELETE FROM bank_pokemon WHERE id = @Id", new { Id = bankId });
        _legalityCache.InvalidateBank(userId);
    }

    /// <summary>
    /// 历史数据回填：扫描 generation=0 或 game_version IS NULL 的记录，从 pkm_data_base64 重新解析并回写
    /// </summary>
    public async Task<BackfillResult> Backfill(Guid userId)
    {
        var records = (await _db.QueryAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE user_id = @UserId AND (generation = 0 OR game_version IS NULL)",
            new { UserId = userId })).ToList();

        int fixed_ = 0, skipped = 0, failed = 0;
        var strings = _pkhexStrings.GetStrings();

        foreach (var rec in records)
        {
            if (string.IsNullOrEmpty(rec.PkmDataBase64))
            {
                // Mark as invalid since we can't verify
                await _db.ExecuteAsync(
                    "UPDATE bank_pokemon SET is_valid = FALSE, updated_at = NOW() WHERE id = @Id",
                    new { rec.Id });
                skipped++;
                continue;
            }

            try
            {
                var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(rec.PkmDataBase64));
                if (pkm == null) { failed++; continue; }

                var dto = _parseService.MapToPokemonDto(pkm);
                var pokemonJson = JsonSerializer.Serialize(dto);

                await _db.ExecuteAsync(@"
                    UPDATE bank_pokemon SET
                        generation = @Generation, game_version = @GameVersion,
                        species = @Species, species_name = @SpeciesName, nickname = @Nickname,
                        level = @Level, nature = @Nature, nature_name = @NatureName,
                        ability = @Ability, ability_name = @AbilityName,
                        is_shiny = @IsShiny, is_egg = @IsEgg, is_valid = @IsValid,
                        pokemon_json = @PokemonJson::jsonb, updated_at = NOW()
                    WHERE id = @Id",
                    new
                    {
                        rec.Id,
                        Generation = pkm.Format,
                        GameVersion = (int)pkm.Version,
                        Species = (int)pkm.Species,
                        SpeciesName = strings.Species[pkm.Species],
                        Nickname = pkm.Nickname,
                        Level = (int)pkm.CurrentLevel,
                        Nature = (int)pkm.Nature,
                        NatureName = strings.Natures[(int)pkm.Nature],
                        Ability = (int)pkm.Ability,
                        AbilityName = strings.Ability[pkm.Ability],
                        IsShiny = pkm.IsShiny,
                        IsEgg = pkm.IsEgg,
                        IsValid = rec.IsValid,  // preserve existing, not re-assessed
                        PokemonJson = pokemonJson
                    });
                fixed_++;
            }
            catch
            {
                failed++;
            }
        }

        _legalityCache.InvalidateBank(userId);
        return new BackfillResult { Fixed = fixed_, Skipped = skipped, Failed = failed };
    }

    /// <summary>
    /// 从存档格子存入银行
    /// </summary>
    public async Task<Guid> AddToBank(Guid userId, PokemonDto pokemon, string? pkmDataBase64, Guid? sourceSaveId = null)
    {
        var bankId = Guid.NewGuid();
        var pokemonJson = JsonSerializer.Serialize(pokemon);

        // Derive generation / game_version from PKM binary (authoritative), fall back to PokemonDto.Format
        int generation = pokemon.Format;
        int? gameVersion = null;
        if (!string.IsNullOrEmpty(pkmDataBase64))
        {
            try
            {
                var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(pkmDataBase64));
                if (pkm != null)
                {
                    generation = pkm.Format;
                    gameVersion = (int)pkm.Version;
                }
            }
            catch { /* keep PokemonDto fallback */ }
        }

        await _db.ExecuteAsync(@"
            INSERT INTO bank_pokemon (id, user_id, species, species_name, nickname, level,
                nature, nature_name, ability, ability_name, generation, game_version, is_shiny, is_egg,
                is_valid, pokemon_json, pkm_data_base64, source, source_save_id)
            VALUES (@Id, @UserId, @Species, @SpeciesName, @Nickname, @Level,
                @Nature, @NatureName, @Ability, @AbilityName, @Generation, @GameVersion, @IsShiny, @IsEgg,
                @IsValid, @PokemonJson::jsonb, @PkmDataBase64, @Source, @SourceSaveId)",
            new
            {
                Id = bankId,
                UserId = userId,
                pokemon.Species,
                pokemon.SpeciesName,
                pokemon.Nickname,
                pokemon.Level,
                pokemon.Nature,
                pokemon.NatureName,
                pokemon.Ability,
                pokemon.AbilityName,
                Generation = generation,
                GameVersion = gameVersion,
                pokemon.IsShiny,
                pokemon.IsEgg,
                pokemon.IsValid,
                PokemonJson = pokemonJson,
                PkmDataBase64 = pkmDataBase64,
                Source = sourceSaveId != null ? "save_import" : "manual",
                SourceSaveId = sourceSaveId
            });

        _legalityCache.InvalidateBank(userId);
        return bankId;
    }

    /// <summary>
    /// 从存档存入银行（完整事务：删除存档格子 + 插入银行）
    /// </summary>
    public async Task<(Guid bankId, PokemonDto pokemon)> MoveFromSave(
        Guid userId, Guid saveFileId, int boxIndex, int slotIndex)
    {
        // 读取存档二进制 → 解析盒子 → 获取指定槽位 PKM
        var pkm = _saveFileService.ReadBoxSlot(saveFileId, userId, boxIndex, slotIndex);
        if (pkm == null) throw BusinessException.FromKey("bank.slotEmpty", 400);

        // Map to DTO
        var pokemon = _parseService.MapToPokemonDto(pkm);
        var buf = new byte[pkm.SIZE_PARTY]; pkm.WriteDecryptedDataParty(buf); var pkmDataBase64 = Convert.ToBase64String(buf);

        // Derive generation / game_version from PKM object itself (not save file)
        var generation = pkm.Format;
        var gameVersion = (int)pkm.Version;

        // 插入银行
        var bankId = Guid.NewGuid();
        await _db.ExecuteAsync(@"
            INSERT INTO bank_pokemon (id, user_id, species, species_name, nickname, level,
                nature, nature_name, ability, ability_name, generation, game_version,
                is_shiny, is_egg, is_valid, pokemon_json, pkm_data_base64,
                source, source_save_id)
            VALUES (@Id, @UserId, @Species, @SpeciesName, @Nickname, @Level,
                @Nature, @NatureName, @Ability, @AbilityName, @Generation, @GameVersion,
                @IsShiny, @IsEgg, @IsValid, @PokemonJson::jsonb, @PkmDataBase64,
                @Source, @SourceSaveId)",
            new
            {
                Id = bankId, UserId = userId,
                pokemon.Species, pokemon.SpeciesName,
                pokemon.Nickname, pokemon.Level,
                pokemon.Nature, pokemon.NatureName,
                pokemon.Ability, pokemon.AbilityName,
                Generation = generation,
                GameVersion = gameVersion,
                pokemon.IsShiny, pokemon.IsEgg,
                IsValid = true,
                PokemonJson = JsonSerializer.Serialize(pokemon),
                PkmDataBase64 = pkmDataBase64,
                Source = "save_import",
                SourceSaveId = saveFileId
            });

        // 清空存档槽位
        await _saveFileService.ClearBoxSlot(saveFileId, userId, boxIndex, slotIndex);

        pokemon.Id = bankId;
        _legalityCache.InvalidateBank(userId);
        return (bankId, pokemon);
    }

    /// <summary>
    /// 从银行删除宝可梦
    /// </summary>
    public async Task Delete(Guid bankPokemonId, Guid userId)
    {
        var deleted = await _db.ExecuteAsync(
            "DELETE FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = bankPokemonId, UserId = userId });

        if (deleted == 0)
            throw BusinessException.FromKey("common.pokemonNotFound", 404);
        _legalityCache.InvalidateBank(userId);
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    public async Task<int> BatchDelete(List<Guid> ids, Guid userId)
    {
        var deleted = await _db.ExecuteAsync(
            "DELETE FROM bank_pokemon WHERE id = ANY(@Ids) AND user_id = @UserId",
            new { Ids = ids, UserId = userId });

        _legalityCache.InvalidateBank(userId);
        return deleted;
    }

    /// <summary>
    /// 批量导出为 .zip（.pk* 文件）
    /// </summary>
    public async Task<byte[]> BatchExport(List<Guid> ids, Guid userId)
    {
        var records = (await _db.QueryAsync<BankPokemon>(
            "SELECT id, species_name, nickname, pkm_data_base64 FROM bank_pokemon WHERE id = ANY(@Ids) AND user_id = @UserId",
            new { Ids = ids, UserId = userId })).ToList();

        if (records.Count == 0)
            throw BusinessException.FromKey("bank.exportNotFound", 404);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var rec in records)
            {
                if (string.IsNullOrEmpty(rec.PkmDataBase64)) continue;

                var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(rec.PkmDataBase64));
                if (pkm == null) continue;

                var data = _editService.ExportSinglePkm(pkm);
                var ext = $"pk{Math.Max(1, (int)pkm.Format)}";
                var name = SanitizeFileName(rec.SpeciesName ?? "unknown");
                var nick = string.IsNullOrWhiteSpace(rec.Nickname) ? null : SanitizeFileName(rec.Nickname);
                var label = nick ?? name;
                var shortId = rec.Id.ToString("N")[..8];
                var fileName = $"{label}_{shortId}.{ext}";

                // Deduplicate
                var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);
                using var es = entry.Open();
                es.Write(data, 0, data.Length);
            }
        }

        ms.Position = 0;
        return ms.ToArray();
    }

    /// <summary>
    /// 批量移动到存档（一箱、自动找空位）
    /// </summary>
    public async Task<BatchMoveResult> BatchMoveToSave(List<Guid> ids, Guid saveFileId, int targetBoxIndex, Guid userId)
    {
        if (ids.Count == 0)
            throw BusinessException.FromKey("bank.selectPokemonRequired", 400);

        // 加载存档
        var (sf, sav) = await _saveFileService.LoadSave(saveFileId, userId);
        var boxData = sav.GetBoxData(targetBoxIndex);
        var capacity = boxData.Length;

        // 找空位
        var emptySlots = new List<int>();
        for (int i = 0; i < capacity; i++)
        {
            if (boxData[i].Species == 0)
                emptySlots.Add(i);
        }

        if (emptySlots.Count == 0)
            throw BusinessException.FromKey("bank.targetBoxFull", 400);

        // 读取银行宝可梦
        var records = (await _db.QueryAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = ANY(@Ids) AND user_id = @UserId",
            new { Ids = ids, UserId = userId })).ToList();

        if (records.Count == 0)
            throw BusinessException.FromKey("bank.moveNotFound", 404);

        var recordMap = records.ToDictionary(r => r.Id);

        // 分配到空位（追踪成功/失败）
        var moved = 0;
        var movedIds = new List<Guid>();
        var failedIds = new List<Guid>();
        var slotIndex = 0;

        foreach (var id in ids)
        {
            if (!recordMap.TryGetValue(id, out var rec))
            {
                failedIds.Add(id);
                continue;
            }

            if (slotIndex >= emptySlots.Count)
            {
                failedIds.Add(id);
                continue;
            }

            if (string.IsNullOrEmpty(rec.PkmDataBase64))
            {
                failedIds.Add(rec.Id);
                continue;
            }

            PKM? pkm;
            try
            {
                pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(rec.PkmDataBase64));
            }
            catch
            {
                failedIds.Add(rec.Id);
                continue;
            }

            if (pkm == null)
            {
                failedIds.Add(rec.Id);
                continue;
            }

            // 兼容转换（仿 SaveFileService.MoveFromBank 路径）
            var compat = sav.GetCompatiblePKM(pkm);
            if (compat == null)
            {
                failedIds.Add(rec.Id);
                continue;
            }

            boxData[emptySlots[slotIndex]] = compat;
            movedIds.Add(rec.Id);
            moved++;
            slotIndex++;
        }

        if (moved > 0)
        {
            sav.SetBoxData(boxData, targetBoxIndex);
            await _saveFileService.WriteBackSave(sf, userId, sav);
        }

        // 只删除成功移动的记录
        if (movedIds.Count > 0)
        {
            await _db.ExecuteAsync(
                "DELETE FROM bank_pokemon WHERE id = ANY(@Ids) AND user_id = @UserId",
                new { Ids = movedIds, UserId = userId });
        }

        _legalityCache.InvalidateBank(userId);

        return new BatchMoveResult
        {
            MovedCount = moved,
            FailedCount = failedIds.Count,
            FailedIds = failedIds
        };
    }

    /// <summary>
    /// 文件名安全化
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "pokemon" : sanitized;
    }

    /// <summary>
    /// 银行宝可梦批量合法性扫描（全量，用于缓存 + 内存分页）。
    /// </summary>
    public async Task<BankBatchLegalityReportDto> BatchLegalityScan(Guid userId)
    {
        var report = new BankBatchLegalityReportDto();
        var slots = new List<SlotLegalityDto>();
        var strings = _pkhexStrings.GetStrings();

        var records = (await _db.QueryAsync<BankScanRecord>(
            @"SELECT id, species, species_name AS SpeciesName, nickname, level,
                     is_shiny AS IsShiny, pkm_data_base64 AS PkmDataBase64
              FROM bank_pokemon WHERE user_id = @UserId ORDER BY created_at DESC",
            new { UserId = userId })).ToList();

        report.Total = records.Count;

        foreach (var rec in records)
        {
            if (string.IsNullOrEmpty(rec.PkmDataBase64))
            {
                slots.Add(new SlotLegalityDto
                {
                    SlotId = $"bank:{rec.Id}", BoxIndex = -1, SlotIndex = -1, IsParty = false,
                    Species = rec.Species, SpeciesName = rec.SpeciesName ?? $"#{rec.Species}",
                    Nickname = rec.Nickname, Level = rec.Level, IsShiny = rec.IsShiny,
                    Status = LegalityStatus.Illegal, FirstIssue = "缺少PKM数据"
                });
                continue;
            }

            try
            {
                var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(rec.PkmDataBase64));
                if (pkm == null) continue;

                var la = new LegalityAnalysis(pkm);
                var status = LegalizationService.ComputeLegalityStatus(la);
                slots.Add(new SlotLegalityDto
                {
                    SlotId = $"bank:{rec.Id}", BoxIndex = -1, SlotIndex = -1, IsParty = false,
                    Species = pkm.Species,
                    SpeciesName = GetSafeString(strings.Species, pkm.Species, $"#{pkm.Species}"),
                    Nickname = pkm.Nickname, Level = pkm.CurrentLevel, IsShiny = pkm.IsShiny,
                    Status = status,
                    FirstIssue = status != LegalityStatus.Legal
                        ? LegalizationService.GetFirstIssue(la) : null
                });
            }
            catch
            {
                slots.Add(new SlotLegalityDto
                {
                    SlotId = $"bank:{rec.Id}", BoxIndex = -1, SlotIndex = -1, IsParty = false,
                    Species = rec.Species, SpeciesName = rec.SpeciesName ?? $"#{rec.Species}",
                    Nickname = rec.Nickname, Level = rec.Level, IsShiny = rec.IsShiny,
                    Status = LegalityStatus.Illegal, FirstIssue = "PKM解析失败"
                });
            }
        }

        report.LegalCount = slots.Count(s => s.Status == LegalityStatus.Legal);
        report.FishyCount = slots.Count(s => s.Status == LegalityStatus.Fishy);
        report.IllegalCount = slots.Count(s => s.Status == LegalityStatus.Illegal);
        report.Slots = slots;
        return report;
    }

    /// <summary>Invalidate bank legality cache.</summary>
    public void InvalidateBankLegalityCache(Guid userId) => _legalityCache.InvalidateBank(userId);

    private static string GetSafeString(IReadOnlyList<string> list, int index, string fallback)
    {
        if (index >= 0 && index < list.Count) return list[index];
        return fallback;
    }

    // ═══ 高级搜索 ═══════════════════════════════════════════

    public async Task<PokemonSearchResultDto> SearchBank(Guid userId, PokemonSearchRequest request)
    {
        // ── 第一层: SQL 粗筛（仅冗余列）──
        var where = "WHERE user_id = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (request.SpeciesId.HasValue)
        {
            where += " AND species = @SpeciesId";
            parameters.Add("SpeciesId", request.SpeciesId.Value);
        }
        if (request.IsShiny.HasValue)
        {
            where += " AND is_shiny = @IsShiny";
            parameters.Add("IsShiny", request.IsShiny.Value);
        }
        if (request.IsEgg.HasValue)
        {
            where += " AND is_egg = @IsEgg";
            parameters.Add("IsEgg", request.IsEgg.Value);
        }
        if (request.Nature.HasValue)
        {
            where += " AND nature = @Nature";
            parameters.Add("Nature", request.Nature.Value);
        }
        if (request.Ability.HasValue)
        {
            where += " AND ability = @Ability";
            parameters.Add("Ability", request.Ability.Value);
        }
        if (request.OriginGame.HasValue)
        {
            where += " AND game_version = @GameVersion";
            parameters.Add("GameVersion", request.OriginGame.Value);
        }
        if (request.IsLegal.HasValue)
        {
            where += " AND is_valid = @IsLegal";
            parameters.Add("IsLegal", request.IsLegal.Value);
        }

        // 文本搜索兜底：species_name ILIKE 或 nickname ILIKE
        if (!string.IsNullOrEmpty(request.SearchText))
        {
            where += " AND (species_name ILIKE @SearchText OR nickname ILIKE @SearchText)";
            parameters.Add("SearchText", $"%{request.SearchText}%");
        }

        // ── 查询全部匹配行（不分页，拿全量做内存过滤）──
        var sql = $@"
            SELECT id, species, species_name AS SpeciesName, nickname, level,
                   nature, nature_name AS NatureName,
                   ability, ability_name AS AbilityName,
                   is_shiny AS IsShiny, is_egg AS IsEgg, is_valid AS IsValid,
                   NULLIF(pokemon_json->>'heldItemName', '') AS HeldItemName,
                   COALESCE((pokemon_json->>'heldItem')::int, 0) AS HeldItem,
                   pkm_data_base64 AS PkmDataBase64
            FROM bank_pokemon {where}
            ORDER BY created_at DESC";

        var rows = (await _db.QueryAsync<BankSearchRow>(sql, parameters)).ToList();

        // ── 第二层: C# 内存精确过滤 ──
        var strings = _pkhexStrings.GetStrings();
        var allMatches = new List<PokemonSearchItemDto>();

        foreach (var row in rows)
        {
            // 先做 SQL 层未覆盖的快速字段过滤
            if (request.MinLevel.HasValue && row.Level < request.MinLevel.Value) continue;
            if (request.MaxLevel.HasValue && row.Level > request.MaxLevel.Value) continue;
            if (request.HeldItem.HasValue && row.HeldItem != request.HeldItem.Value) continue;

            // 检测需要深度过滤的条件（字段不在冗余列中，必须反序列化 PKM 才能判断）
            bool needDeepFilter =
                // IV 单项 (12 fields)
                request.MinIV_HP.HasValue || request.MaxIV_HP.HasValue
                || request.MinIV_ATK.HasValue || request.MaxIV_ATK.HasValue
                || request.MinIV_DEF.HasValue || request.MaxIV_DEF.HasValue
                || request.MinIV_SPA.HasValue || request.MaxIV_SPA.HasValue
                || request.MinIV_SPD.HasValue || request.MaxIV_SPD.HasValue
                || request.MinIV_SPE.HasValue || request.MaxIV_SPE.HasValue
                // EV 单项 (12 fields)
                || request.MinEV_HP.HasValue || request.MaxEV_HP.HasValue
                || request.MinEV_ATK.HasValue || request.MaxEV_ATK.HasValue
                || request.MinEV_DEF.HasValue || request.MaxEV_DEF.HasValue
                || request.MinEV_SPA.HasValue || request.MaxEV_SPA.HasValue
                || request.MinEV_SPD.HasValue || request.MaxEV_SPD.HasValue
                || request.MinEV_SPE.HasValue || request.MaxEV_SPE.HasValue
                // IV/EV totals
                || request.MinIVTotal.HasValue || request.MaxIVTotal.HasValue
                || request.MinEVTotal.HasValue || request.MaxEVTotal.HasValue
                // 其他不在冗余列中的字段
                || request.Ball.HasValue || request.Language.HasValue
                || request.Gender.HasValue
                || request.RequiredMoves is { Count: > 0 }
                || request.AnyMoves is { Count: > 0 }
                || !string.IsNullOrEmpty(request.OT_Name)
                || request.TID.HasValue;

            if (needDeepFilter && !string.IsNullOrEmpty(row.PkmDataBase64))
            {
                try
                {
                    var pkm = EntityFormat.GetFromBytes(Convert.FromBase64String(row.PkmDataBase64));
                    if (pkm != null && SaveFileService.MatchesFilter(pkm, request, strings))
                    {
                        allMatches.Add(MapBankRowToSearchItem(row, strings));
                    }
                }
                catch { /* 跳过无法解析的行 */ }
            }
            else if (!needDeepFilter)
            {
                allMatches.Add(MapBankRowToSearchItem(row, strings));
            }
        }

        var total = allMatches.Count;
        var paged = allMatches
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new PokemonSearchResultDto
        {
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
            Items = paged,
        };
    }

    private static PokemonSearchItemDto MapBankRowToSearchItem(BankSearchRow row, GameStrings strings)
    {
        return new PokemonSearchItemDto
        {
            SpeciesId = row.Species,
            SpeciesName = row.SpeciesName ?? $"#{row.Species}",
            Nickname = row.Nickname ?? "",
            Level = row.Level,
            Nature = row.Nature,
            NatureName = row.NatureName ?? "",
            Ability = row.Ability,
            AbilityName = row.AbilityName ?? "",
            HeldItem = row.HeldItem > 0 ? row.HeldItem : null,
            HeldItemName = row.HeldItem > 0 ? row.HeldItemName : null,
            IsShiny = row.IsShiny,
            IsEgg = row.IsEgg,
            IsValid = row.IsValid,
            PkmDataBase64 = row.PkmDataBase64,
            BankId = row.Id.ToString(),
        };
    }

    private class BankSearchRow
    {
        public Guid Id { get; set; }
        public int Species { get; set; }
        public string? SpeciesName { get; set; }
        public string? Nickname { get; set; }
        public int Level { get; set; }
        public int Nature { get; set; }
        public string? NatureName { get; set; }
        public int Ability { get; set; }
        public string? AbilityName { get; set; }
        public bool IsShiny { get; set; }
        public bool IsEgg { get; set; }
        public bool IsValid { get; set; }
        public string? HeldItemName { get; set; }
        public int HeldItem { get; set; }
        public string? PkmDataBase64 { get; set; }
    }
}

// ── 辅助类型 ────────────────────────────────────────────

public class BankFilter
{
    public int? Generation { get; set; }
    public bool? IsShiny { get; set; }
    public int? Nature { get; set; }
    public int? Ability { get; set; }
    public string? SortBy { get; set; }    // "created" | "level" | "species"
    public bool SortAsc { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class BankListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<BankPokemonDto> Items { get; set; } = new();
}

public class BankPokemonDto
{
    public Guid Id { get; set; }
    public int Species { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public int Level { get; set; }
    public string? NatureName { get; set; }
    public string? AbilityName { get; set; }
    public int Generation { get; set; }
    public int? GameVersion { get; set; }
    public bool IsShiny { get; set; }
    public bool IsEgg { get; set; }
    public bool IsValid { get; set; }
    public bool IsAlpha { get; set; }
    public bool CanGigantamax { get; set; }
    public string? HeldItemName { get; set; }
    public string? Source { get; set; }
    public Guid? SourceSaveId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BatchMoveResult
{
    public int MovedCount { get; set; }
    public int FailedCount { get; set; }
    public List<Guid> FailedIds { get; set; } = new();
}

public class BackfillResult
{
    public int Fixed { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

public class MoveToSaveRequest
{
    public Guid SaveFileId { get; set; }
    public int TargetBoxIndex { get; set; }
    public int? TargetSlotIndex { get; set; }
}

/// <summary>
/// 银行批量扫描的原始数据记录。
/// </summary>
public class BankScanRecord
{
    public Guid Id { get; set; }
    public int Species { get; set; }
    public string? SpeciesName { get; set; }
    public string? Nickname { get; set; }
    public int Level { get; set; }
    public bool IsShiny { get; set; }
    public string? PkmDataBase64 { get; set; }
}
