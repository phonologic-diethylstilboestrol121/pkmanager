using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Entity;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmulatorController : ControllerBase
{
    private static readonly TimeSpan SyncTokenLifetime = TimeSpan.FromHours(12);
    private readonly NpgsqlConnection _db;
    private readonly SaveFileService _saveFileService;
    private readonly ParseService _parseService;
    private readonly UserContext _userContext;
    private readonly string _baseSaveDir;

    private readonly SettingsService _settingsService;

    public EmulatorController(NpgsqlConnection db, SaveFileService saveFileService, ParseService parseService, UserContext userContext, IWebHostEnvironment env, SettingsService settingsService)
    {
        _db = db; _saveFileService = saveFileService; _parseService = parseService; _userContext = userContext;
        _baseSaveDir = Path.Combine(env.ContentRootPath, "data", "saves");
        _settingsService = settingsService;
    }

    /// <summary>列出可用 ROM</summary>
    [HttpGet("roms")]
    public async Task<ActionResult<ApiResponse<List<RomDto>>>> ListRoms()
    {
        if (_userContext.UserId == null) return Unauthorized(ApiResponse<List<RomDto>>.Error(401, "未登录"));
        var roms = await _db.QueryAsync<RomFileEntity>("SELECT id, game_id, display_name, generation, file_size FROM rom_files ORDER BY display_name");
        return Ok(ApiResponse<List<RomDto>>.Ok(roms.Select(r => new RomDto
        {
            Id = r.Id, GameId = r.GameId, DisplayName = r.DisplayName, Generation = r.Generation, FileSize = r.FileSize
        }).ToList()));
    }

    /// <summary>下载 ROM 二进制（全部从文件系统服务）</summary>
    [HttpGet("roms/{gameId}")]
    public async Task<IActionResult> DownloadRom(string gameId)
    {
        var rom = await _db.QueryFirstOrDefaultAsync<RomFileEntity>("SELECT * FROM rom_files WHERE game_id = @Id", new { Id = gameId });
        if (rom == null) return NotFound();

        if (!string.IsNullOrEmpty(rom.LocalPath) && System.IO.File.Exists(rom.LocalPath))
        {
            var stream = new FileStream(rom.LocalPath, FileMode.Open, FileAccess.Read);
            var ext = Path.GetExtension(rom.LocalPath);
            return File(stream, "application/octet-stream", $"{gameId}{ext}");
        }

        return NotFound("ROM file missing");
    }

    /// <summary>批量导入 ROM（从服务器本地文件）</summary>
    [HttpPost("roms/import-local")]
    public async Task<ActionResult<ApiResponse<object>>> ImportLocal()
    {
        if (_userContext.UserId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        var romDir = "/home/fmangela/pkmanager/roms";
        if (!Directory.Exists(romDir)) return BadRequest(ApiResponse<object>.Error(400, "ROM目录不存在"));

        var romMap = new Dictionary<string, (string gameId, string displayName, int generation)>(StringComparer.OrdinalIgnoreCase) {
            // GBA (Gen3)
            {"红宝石", ("pkm_ruby", "宝可梦 红宝石", 3)}, {"蓝宝石", ("pkm_sapphire", "宝可梦 蓝宝石", 3)},
            {"绿宝石", ("pkm_emerald", "宝可梦 绿宝石", 3)}, {"火红", ("pkm_firered", "宝可梦 火红", 3)},
            {"叶绿", ("pkm_leafgreen", "宝可梦 叶绿", 3)},
            // NDS (Gen4)
            {"钻石", ("pkm_diamond", "宝可梦 钻石", 4)}, {"珍珠", ("pkm_pearl", "宝可梦 珍珠", 4)},
            {"白金", ("pkm_platinum", "宝可梦 白金", 4)}, {"金心", ("pkm_heartgold", "宝可梦 心金", 4)},
            {"魂银", ("pkm_soulsilver", "宝可梦 魂银", 4)},
            // NDS (Gen5)
            {"黑2", ("pkm_black2", "宝可梦 黑2", 5)}, {"白2", ("pkm_white2", "宝可梦 白2", 5)},
            {"黑", ("pkm_black", "宝可梦 黑", 5)}, {"白", ("pkm_white", "宝可梦 白", 5)},
        };

        var imported = new List<string>();

        // 全部 ROM 统一走文件系统路径（GBA .gba + NDS .nds）
        foreach (var ext in new[] { "*.gba", "*.nds" }) {
        foreach (var file in Directory.GetFiles(romDir, ext)) {
            var name = Path.GetFileNameWithoutExtension(file);
            var match = romMap.FirstOrDefault(kv => name.Contains(kv.Key));
            if (match.Key == null) continue;
            var (gid, dname, gen) = match.Value;

            var fileSize = new FileInfo(file).Length;
            var existing = await _db.QueryFirstOrDefaultAsync<RomFileEntity>("SELECT id FROM rom_files WHERE game_id=@Id", new { Id = gid });
            if (existing != null)
                await _db.ExecuteAsync("UPDATE rom_files SET file_size=@S, local_path=@P WHERE game_id=@I", new { I = gid, S = fileSize, P = file });
            else
                await _db.ExecuteAsync("INSERT INTO rom_files (game_id,display_name,generation,file_size,local_path,rom_data) VALUES (@I,@N,@G,@S,@P,@D)",
                    new { I = gid, N = dname, G = gen, S = fileSize, P = file, D = Array.Empty<byte>() });
            imported.Add($"{dname} ({fileSize} bytes)");
        }
        }
        return Ok(ApiResponse<object>.Ok(new { imported }, string.Join(", ", imported)));
    }

    /// <summary>上传 ROM（管理员用）</summary>
    [HttpPost("roms/upload")]
    [RequestSizeLimit(64 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> UploadRom(IFormFile file, [FromForm] string gameId, [FromForm] string displayName, [FromForm] int generation = 3)
    {
        if (_userContext.UserId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        if (file == null || file.Length == 0) return BadRequest(ApiResponse<object>.Error(400, "请选择ROM文件"));
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var data = ms.ToArray();
        var existing = await _db.QueryFirstOrDefaultAsync<RomFileEntity>("SELECT id FROM rom_files WHERE game_id = @Id", new { Id = gameId });
        if (existing != null)
            await _db.ExecuteAsync("UPDATE rom_files SET rom_data=@Data, file_size=@Size WHERE game_id=@Id", new { Id = gameId, Data = data, Size = data.Length });
        else
            await _db.ExecuteAsync("INSERT INTO rom_files (game_id, display_name, generation, rom_data, file_size) VALUES (@Id,@Name,@Gen,@Data,@Size)",
                new { Id = gameId, Name = displayName, Gen = generation, Data = data, Size = data.Length });
        return Ok(ApiResponse<object>.Ok(new { }, "ROM上传成功"));
    }

    /// <summary>
    /// 同步存档 — 手动/关闭时调用。
    /// 已有存档: saveFileId + saveDataBase64 → 更新。
    /// 新游戏首次同步: saveFileId=Guid.Empty + gameId="pkm_diamond" + saveDataBase64 → 创建记录。
    /// </summary>
    [HttpPost("sync-save")]
    public async Task<ActionResult<ApiResponse<object>>> SyncSave([FromBody] SyncSaveRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        if (string.IsNullOrEmpty(request.SaveDataBase64))
            return BadRequest(ApiResponse<object>.Error(400, "缺少存档数据"));

        var data = Convert.FromBase64String(request.SaveDataBase64);

        // ── 新游戏首次同步: 自动创建存档记录 ──
        Guid saveFileId;
        if (request.SaveFileId == Guid.Empty && !string.IsNullOrEmpty(request.GameId))
        {
            var result = await _saveFileService.CreateNewGame(userId.Value, request.GameId);
            saveFileId = result.SaveFileId;
        }
        else if (request.SaveFileId != Guid.Empty)
        {
            saveFileId = request.SaveFileId;
        }
        else
        {
            return BadRequest(ApiResponse<object>.Error(400, "缺少 saveFileId 或 gameId"));
        }

        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid",
            new { Id = saveFileId, Uid = userId.Value });
        if (saveFile == null) return NotFound();

        // 写入前自动备份（当前存档有数据时才备份）
        var currentData = ReadSaveBytesSafe(saveFile);
        if (currentData is { Length: > 0 })
        {
            await _saveFileService.CreateBackup(saveFileId, userId.Value, "同步前自动备份");
        }

        // 写入文件系统
        var savePath = saveFile.SavePath;
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = Path.Combine(_baseSaveDir, userId.Value.ToString(), saveFileId.ToString(), "save.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            await _db.ExecuteAsync("UPDATE save_files SET save_path=@P WHERE id=@Id",
                new { P = savePath, Id = saveFileId });
        }
        await System.IO.File.WriteAllBytesAsync(savePath, data);

        // 解析存档更新元数据
        string? trainerName = null;
        int? pokemonCount = null;
        try
        {
            var parsed = _parseService.ParseSaveFile(data, saveFile.Filename);
            trainerName = parsed.TrainerName;
            pokemonCount = parsed.PokemonCount;
            await _db.ExecuteAsync(@"
                UPDATE save_files SET
                    file_size = @Size, is_modified = TRUE, updated_at = NOW(),
                    trainer_name = @TN, trainer_id = @TID, secret_id = @SID,
                    play_time = @PT, box_count = @BC, pokemon_count = @PC,
                    generation = @G, game_version = @GV
                WHERE id = @Id",
                new
                {
                    Id = saveFileId, Size = (long)data.Length,
                    TN = parsed.TrainerName, TID = parsed.TrainerId, SID = parsed.SecretId,
                    PT = parsed.PlayTime, BC = parsed.BoxCount, PC = parsed.PokemonCount,
                    G = parsed.Generation, GV = GameVersionNormalizer.NormalizeOrKeepExisting(parsed.GameVersion, saveFile.GameVersion)
                });
        }
        catch
        {
            await _db.ExecuteAsync(
                "UPDATE save_files SET file_size=@Size, is_modified=TRUE, updated_at=NOW() WHERE id=@Id",
                new { Id = saveFileId, Size = (long)data.Length });
        }

        return Ok(ApiResponse<object>.Ok(new { saveFileId, trainerName, pokemonCount }, "存档已同步"));
    }

    /// <summary>
    /// 同步存档（二进制）— beforeunload 时通过 sendBeacon 发送，无 keepalive 64KB 限制
    /// </summary>
    [HttpPost("sync-save/{saveFileId:guid}")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> SyncSaveBinary(
        Guid saveFileId, [FromQuery] string token)
    {
        // 验证 token（sendBeacon 无法设置自定义请求头，token 通过 query string 传递）
        if (string.IsNullOrEmpty(token)) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var userId = _userContext.UserId; // 优先使用 JWT 中间件解析的结果
        var usedSyncToken = false;
        if (_syncTokens.TryGetValue(token, out var syncEntry))
        {
            if (syncEntry.ExpiresAt < DateTime.UtcNow)
            {
                _syncTokens.TryRemove(token, out _);
                return Unauthorized(ApiResponse<object>.Error(401, "同步 token 已过期"));
            }
            if (syncEntry.SaveFileId != saveFileId)
                return Unauthorized(ApiResponse<object>.Error(401, "同步 token 不匹配"));
            userId = syncEntry.UserId;
            usedSyncToken = true;
        }
        else if (userId == null)
        {
            // JWT 中间件未解析（sendBeacon 可能不发送 Authorization header），手动解析 token
            try
            {
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                var uidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == "userId");
                if (uidClaim == null || !Guid.TryParse(uidClaim.Value, out var uid))
                    return Unauthorized(ApiResponse<object>.Error(401, "Token 无效"));
                userId = uid;
            }
            catch { return Unauthorized(ApiResponse<object>.Error(401, "Token 无效")); }
        }

        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid",
            new { Id = saveFileId, Uid = userId.Value });
        if (saveFile == null) return NotFound();

        // 读取二进制 body
        byte[] data;
        using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms);
            data = ms.ToArray();
        }
        if (data.Length == 0) return BadRequest(ApiResponse<object>.Error(400, "存档数据为空"));

        // 写入前自动备份
        var currentData = ReadSaveBytesSafe(saveFile);
        if (currentData is { Length: > 0 })
        {
            await _saveFileService.CreateBackup(saveFileId, userId.Value, "同步前自动备份");
        }

        // 写入文件系统
        var savePath = saveFile.SavePath;
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = Path.Combine(_baseSaveDir, userId.Value.ToString(), saveFileId.ToString(), "save.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            await _db.ExecuteAsync("UPDATE save_files SET save_path=@P WHERE id=@Id",
                new { P = savePath, Id = saveFileId });
        }
        await System.IO.File.WriteAllBytesAsync(savePath, data);

        // 解析存档更新元数据
        try
        {
            var parsed = _parseService.ParseSaveFile(data, saveFile.Filename);
            await _db.ExecuteAsync(@"
                UPDATE save_files SET
                    file_size = @Size, is_modified = TRUE, updated_at = NOW(),
                    trainer_name = @TN, trainer_id = @TID, secret_id = @SID,
                    play_time = @PT, box_count = @BC, pokemon_count = @PC,
                    generation = @G, game_version = @GV
                WHERE id = @Id",
                new
                {
                    Id = saveFileId, Size = (long)data.Length,
                    TN = parsed.TrainerName, TID = parsed.TrainerId, SID = parsed.SecretId,
                    PT = parsed.PlayTime, BC = parsed.BoxCount, PC = parsed.PokemonCount,
                    G = parsed.Generation, GV = GameVersionNormalizer.NormalizeOrKeepExisting(parsed.GameVersion, saveFile.GameVersion)
                });
        }
        catch
        {
            await _db.ExecuteAsync(
                "UPDATE save_files SET file_size=@Size, is_modified=TRUE, updated_at=NOW() WHERE id=@Id",
                new { Id = saveFileId, Size = (long)data.Length });
        }

        if (usedSyncToken)
            _syncTokens.TryRemove(token, out _);

        return Ok(ApiResponse<object>.Ok(new { }, "存档已同步"));
    }

    /// <summary>
    /// 同步存档（二进制，新游戏首次同步）— beforeunload + sendBeacon 使用
    /// </summary>
    [HttpPost("sync-save/new/{gameId}")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<object>>> SyncSaveBinaryNew(
        string gameId, [FromQuery] string token)
    {
        if (string.IsNullOrEmpty(token)) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var userId = _userContext.UserId;
        if (userId == null)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwt = handler.ReadJwtToken(token);
                var uidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == "userId");
                if (uidClaim == null || !Guid.TryParse(uidClaim.Value, out var uid))
                    return Unauthorized(ApiResponse<object>.Error(401, "Token 无效"));
                userId = uid;
            }
            catch { return Unauthorized(ApiResponse<object>.Error(401, "Token 无效")); }
        }

        byte[] data;
        using (var ms = new MemoryStream())
        {
            await Request.Body.CopyToAsync(ms);
            data = ms.ToArray();
        }
        if (data.Length == 0) return BadRequest(ApiResponse<object>.Error(400, "存档数据为空"));

        var created = await _saveFileService.CreateNewGame(userId.Value, gameId);
        var saveFileId = created.SaveFileId;

        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid",
            new { Id = saveFileId, Uid = userId.Value });
        if (saveFile == null) return NotFound();

        var savePath = saveFile.SavePath;
        if (string.IsNullOrEmpty(savePath))
        {
            savePath = Path.Combine(_baseSaveDir, userId.Value.ToString(), saveFileId.ToString(), "save.sav");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
            await _db.ExecuteAsync("UPDATE save_files SET save_path=@P WHERE id=@Id",
                new { P = savePath, Id = saveFileId });
        }
        await System.IO.File.WriteAllBytesAsync(savePath, data);

        string? trainerName = null;
        int? pokemonCount = null;
        try
        {
            var parsed = _parseService.ParseSaveFile(data, $"{gameId}.sav");
            trainerName = parsed.TrainerName;
            pokemonCount = parsed.PokemonCount;
            await _db.ExecuteAsync(@"
                UPDATE save_files SET
                    file_size = @Size, is_modified = TRUE, updated_at = NOW(),
                    trainer_name = @TN, trainer_id = @TID, secret_id = @SID,
                    play_time = @PT, box_count = @BC, pokemon_count = @PC,
                    generation = @G, game_version = @GV
                WHERE id = @Id",
                new
                {
                    Id = saveFileId, Size = (long)data.Length,
                    TN = parsed.TrainerName, TID = parsed.TrainerId, SID = parsed.SecretId,
                    PT = parsed.PlayTime, BC = parsed.BoxCount, PC = parsed.PokemonCount,
                    G = parsed.Generation, GV = GameVersionNormalizer.NormalizeOrKeepExisting(parsed.GameVersion, saveFile.GameVersion)
                });
        }
        catch
        {
            await _db.ExecuteAsync(
                "UPDATE save_files SET file_size=@Size, is_modified=TRUE, updated_at=NOW() WHERE id=@Id",
                new { Id = saveFileId, Size = (long)data.Length });
        }

        return Ok(ApiResponse<object>.Ok(new { saveFileId, trainerName, pokemonCount }, "存档已同步"));
    }

    /// <summary>读取当前存档二进制（仅检查是否存在，供同步流程使用）</summary>
    private static byte[]? ReadSaveBytesSafe(Models.Entity.SaveFile entity)
    {
        if (!string.IsNullOrEmpty(entity.SavePath) && System.IO.File.Exists(entity.SavePath))
            return System.IO.File.ReadAllBytes(entity.SavePath);
        if (entity.RawSaveData is { Length: > 0 })
            return entity.RawSaveData;
        return null;
    }

    /// <summary>保存即时存档状态</summary>
    [HttpPost("{saveFileId:guid}/savestate/{slot:int}")]
    public async Task<ActionResult<ApiResponse<object>>> SaveState(Guid saveFileId, int slot, [FromBody] byte[] stateData)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        await _db.ExecuteAsync("INSERT INTO emulator_save_states (save_file_id, slot, state_data) VALUES (@Sf,@Sl,@Dt) ON CONFLICT (save_file_id, slot) DO UPDATE SET state_data=@Dt, created_at=NOW()",
            new { Sf = saveFileId, Sl = slot, Dt = stateData });
        return Ok(ApiResponse<object>.Ok(new { }, $"即时存档 #{slot} 已保存"));
    }

    /// <summary>加载即时存档状态</summary>
    [HttpGet("{saveFileId:guid}/savestate/{slot:int}")]
    public async Task<IActionResult> LoadState(Guid saveFileId, int slot)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized();
        var st = await _db.QueryFirstOrDefaultAsync<EmulatorSaveStateEntity>("SELECT * FROM emulator_save_states WHERE save_file_id=@Sf AND slot=@Sl",
            new { Sf = saveFileId, Sl = slot });
        if (st == null) return NotFound();
        return File(st.StateData, "application/octet-stream");
    }

    // ── 本地模拟器启动框架 ──────────────────────────────────

    /// <summary>预校验：检查模拟器配置是否就绪（仅检查路径已配置，不做服务器端文件存在校验，因路径指向浏览器所在机器）</summary>
    [HttpPost("check-local")]
    public async Task<ActionResult<ApiResponse<object>>> CheckLocal([FromBody] CheckLocalRequest req)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var deviceId = GetDeviceId();
        var emuSettings = await _settingsService.GetEmulatorSettings(userId.Value, deviceId);
        var gen = req.Generation;

        var result = new Dictionary<string, object> { ["generation"] = gen };

        if (gen >= 6)
        {
            // ── 3DS Azahar ──
            if (!emuSettings.TryGetValue("azahar.exe_path", out var exeRaw) || string.IsNullOrWhiteSpace(exeRaw))
            {
                result["azaharReady"] = false;
                result["error"] = "未配置 Azahar 路径，请前往设置页配置";
                return Ok(ApiResponse<object>.Ok(result));
            }
            var exe = NormalizeExePath(exeRaw);
            result["exePath"] = exe;
            result["exeConfigured"] = true; // 路径已配置（不校验服务器端文件是否存在）

            var dataDir = NormalizeDirPath(emuSettings.GetValueOrDefault("azahar.data_dir") ?? "");
            if (string.IsNullOrWhiteSpace(dataDir))
                dataDir = GetDefaultAzaharDataDir();
            result["dataDir"] = dataDir;
            result["azaharReady"] = true;
        }
        else
        {
            // ── NDS DeSmuME ──
            if (!emuSettings.TryGetValue("desmume.exe_path", out var exeRaw) || string.IsNullOrWhiteSpace(exeRaw))
            {
                result["desmumeReady"] = false;
                result["error"] = "未配置 DeSmuME 路径，请前往设置页配置";
                return Ok(ApiResponse<object>.Ok(result));
            }
            var exe = NormalizeExePath(exeRaw);
            result["exePath"] = exe;
            result["exeConfigured"] = true;

            var rom = await _db.QueryFirstOrDefaultAsync<Models.Entity.RomFileEntity>(
                "SELECT * FROM rom_files WHERE generation=@Gen AND local_path IS NOT NULL LIMIT 1", new { Gen = gen });
            result["romFound"] = rom != null;
            result["romPath"] = rom?.LocalPath ?? "";
            if (rom == null)
            {
                result["desmumeReady"] = false;
                result["error"] = "未找到 ROM 文件，请先导入";
                return Ok(ApiResponse<object>.Ok(result));
            }
            result["desmumeReady"] = true;
        }

        return Ok(ApiResponse<object>.Ok(result, "ready"));
    }

    /// <summary>
    /// 准备本地启动包 — 返回存档二进制 + 路径信息，由浏览器所在机器自行调起模拟器。
    /// 服务器不做 Process.Start（可能和浏览器不在同一台机器）。
    /// </summary>
    [HttpPost("launch-local/{saveFileId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> LaunchLocal(Guid saveFileId)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var save = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid", new { Id = saveFileId, Uid = userId });
        if (save == null) return NotFound(ApiResponse<object>.Error(404, "存档不存在"));

        var gen = save.Generation;
        var gameVersion = save.GameVersion ?? 0;
        var deviceId = GetDeviceId();
        var emuSettings = await _settingsService.GetEmulatorSettings(userId.Value, deviceId);

        // 读取 pkmanager 存档二进制
        var pkSavePath = Path.Combine(_baseSaveDir, userId.ToString()!, saveFileId.ToString(), "save.sav");
        if (!System.IO.File.Exists(pkSavePath))
            return BadRequest(ApiResponse<object>.Error(400, "存档文件不存在"));

        var saveData = await System.IO.File.ReadAllBytesAsync(pkSavePath);
        var saveDataBase64 = Convert.ToBase64String(saveData);
        var syncToken = CreateSyncToken(saveFileId, userId.Value);

        await _saveFileService.CreateBackup(saveFileId, userId.Value, "启动本地模拟器前");

        string emulatorType;
        string exePath;
        string saveDir;
        string? romPath = null;   // ROM / .app 文件路径
        string? emuSavePath = null; // 模拟器存档应写入的位置
        string? launchArgs = null;  // 模拟器命令行参数

        if (gen >= 6)
        {
            // ── 3DS Azahar ──
            emulatorType = "azahar";
            if (!emuSettings.TryGetValue("azahar.exe_path", out var azExeRaw) || string.IsNullOrWhiteSpace(azExeRaw))
                return BadRequest(ApiResponse<object>.Error(400, "未配置 Azahar 路径，请前往 /settings 设置"));
            exePath = NormalizeExePath(azExeRaw);
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("azahar.data_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultAzaharDataDir();

            // 内容文件路径（.app，用于 Azahar 直接启动）— 仅构造路径，不做服务器端校验
            romPath = ConstructAzaharContentPath(saveDir, gameVersion);
            emuSavePath = GetAzaharSavePath(saveDir, gameVersion);
            launchArgs = $"\"{romPath}\"";
        }
        else
        {
            // ── NDS DeSmuME ──
            emulatorType = "desmume";
            if (!emuSettings.TryGetValue("desmume.exe_path", out var dsExeRaw) || string.IsNullOrWhiteSpace(dsExeRaw))
                return BadRequest(ApiResponse<object>.Error(400, "未配置 DeSmuME 路径，请前往 /settings 设置"));
            exePath = NormalizeExePath(dsExeRaw);
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("desmume.save_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultDeSmuMESaveDir();

            var rom = await _db.QueryFirstOrDefaultAsync<Models.Entity.RomFileEntity>(
                "SELECT * FROM rom_files WHERE generation=@Gen AND local_path IS NOT NULL LIMIT 1",
                new { Gen = gen });
            if (rom == null) return BadRequest(ApiResponse<object>.Error(400, "未找到对应 NDS ROM，请先导入"));
            romPath = rom.LocalPath ?? rom.GameId;

            var romFileName = Path.GetFileNameWithoutExtension(romPath);
            emuSavePath = Path.Combine(saveDir, $"{romFileName}.dsv");
            launchArgs = $"\"{romPath}\"";
        }

        // 返回启动包给前端：存档数据 + 所有路径信息
        return Ok(ApiResponse<object>.Ok(new
        {
            type = emulatorType,
            generation = gen,
            gameVersion,
            titleIdLow = gen >= 6 ? GetTitleIdLow(gameVersion) : null,
            // 模拟器可执行文件路径（用户配置的，在浏览器所在机器上）
            exePath,
            saveDir,
            // ROM / .app 内容文件路径
            romPath,
            gameInstalled = romPath != null,
            // 模拟器存档应写入的路径
            emuSavePath,
            // 启动命令行参数
            launchArgs,
            // pkmanager 存档二进制 (base64)，前端写入模拟器存档目录
            saveDataBase64,
            fileName = save.Filename,
            saveFileId,
            syncToken,
        }, "启动包已就绪，请由浏览器端调起模拟器"));
    }

    // ── 临时启动 Token（供本地协议处理器使用）───────────────

    /// <summary>临时 token → 启动包 映射（5分钟过期）</summary>
    private static readonly ConcurrentDictionary<string, (LaunchPackage Package, DateTime ExpiresAt)> _launchTokens = new();
    private static readonly ConcurrentDictionary<string, (Guid SaveFileId, Guid UserId, DateTime ExpiresAt)> _syncTokens = new();

    /// <summary>创建临时启动 token（供本地协议处理器免认证获取启动包）</summary>
    [HttpPost("launch-token/{saveFileId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> CreateLaunchToken(Guid saveFileId)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        // 复用 LaunchLocal 的逻辑构建启动包
        var save = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid", new { Id = saveFileId, Uid = userId });
        if (save == null) return NotFound();

        var gen = save.Generation;
        var gameVersion = save.GameVersion ?? 0;
        var deviceId = GetDeviceId();
        var emuSettings = await _settingsService.GetEmulatorSettings(userId.Value, deviceId);

        var pkSavePath = Path.Combine(_baseSaveDir, userId.ToString()!, saveFileId.ToString(), "save.sav");
        if (!System.IO.File.Exists(pkSavePath))
            return BadRequest(ApiResponse<object>.Error(400, "存档文件不存在"));

        var saveData = await System.IO.File.ReadAllBytesAsync(pkSavePath);
        var saveDataBase64 = Convert.ToBase64String(saveData);
        var syncToken = CreateSyncToken(saveFileId, userId.Value);

        string exePath, saveDir, romPath, emuSavePath, launchArgs, emulatorType;

        if (gen >= 6)
        {
            emulatorType = "azahar";
            if (!emuSettings.TryGetValue("azahar.exe_path", out var azExeRaw) || string.IsNullOrWhiteSpace(azExeRaw))
                return BadRequest(ApiResponse<object>.Error(400, "未配置 Azahar 路径"));
            exePath = NormalizeExePath(azExeRaw);
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("azahar.data_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir)) saveDir = GetDefaultAzaharDataDir();
            romPath = ConstructAzaharContentPath(saveDir, gameVersion);
            emuSavePath = GetAzaharSavePath(saveDir, gameVersion);
            launchArgs = $"\"{romPath}\"";
        }
        else
        {
            emulatorType = "desmume";
            if (!emuSettings.TryGetValue("desmume.exe_path", out var dsExeRaw) || string.IsNullOrWhiteSpace(dsExeRaw))
                return BadRequest(ApiResponse<object>.Error(400, "未配置 DeSmuME 路径"));
            exePath = NormalizeExePath(dsExeRaw);
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("desmume.save_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir)) saveDir = GetDefaultDeSmuMESaveDir();
            var rom = await _db.QueryFirstOrDefaultAsync<Models.Entity.RomFileEntity>(
                "SELECT * FROM rom_files WHERE generation=@Gen AND local_path IS NOT NULL LIMIT 1", new { Gen = gen });
            if (rom == null) return BadRequest(ApiResponse<object>.Error(400, "未找到 ROM"));
            romPath = rom.LocalPath ?? rom.GameId;
            var romFileName = Path.GetFileNameWithoutExtension(romPath);
            emuSavePath = Path.Combine(saveDir, $"{romFileName}.dsv");
            launchArgs = $"\"{romPath}\"";
        }

        var token = Guid.NewGuid().ToString("N");
        _launchTokens[token] = (new LaunchPackage
        {
            ExePath = exePath, SaveDir = saveDir, RomPath = romPath,
            EmuSavePath = emuSavePath, LaunchArgs = launchArgs, Type = emulatorType,
            SaveDataBase64 = saveDataBase64, Generation = gen, GameVersion = gameVersion,
            TitleIdLow = GetTitleIdLow(gameVersion), FileName = save.Filename,
            SaveFileId = saveFileId, SyncToken = syncToken,
        }, DateTime.UtcNow.AddMinutes(5));

        // 清理过期 token
        foreach (var kv in _launchTokens.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).ToList())
            _launchTokens.TryRemove(kv.Key, out _);

        var backendBase = $"{Request.Scheme}://{Request.Host}";
        return Ok(ApiResponse<object>.Ok(new
        {
            token,
            protocolUrl = $"pkmanager://launch/{token}?backend={Uri.EscapeDataString(backendBase)}",
        }, "token 已创建，5分钟内有效"));
    }

    /// <summary>通过临时 token 获取启动包（免认证，一次性使用）</summary>
    [HttpGet("launch-package/{token}")]
    public ActionResult<ApiResponse<object>> GetLaunchPackage(string token)
    {
        if (!_launchTokens.TryRemove(token, out var entry))
            return NotFound(ApiResponse<object>.Error(404, "token 无效或已过期"));

        if (entry.ExpiresAt < DateTime.UtcNow)
            return StatusCode(410, ApiResponse<object>.Error(410, "token 已过期"));

        var p = entry.Package;
        return Ok(ApiResponse<object>.Ok(new
        {
            p.Type, p.Generation, p.GameVersion, p.ExePath, p.SaveDir,
            p.RomPath, p.EmuSavePath, p.LaunchArgs, p.SaveDataBase64,
            p.TitleIdLow, p.FileName, p.SaveFileId, p.SyncToken,
        }));
    }

    private static string CreateSyncToken(Guid saveFileId, Guid userId)
    {
        var token = Guid.NewGuid().ToString("N");
        _syncTokens[token] = (saveFileId, userId, DateTime.UtcNow.Add(SyncTokenLifetime));
        foreach (var kv in _syncTokens.Where(kv => kv.Value.ExpiresAt < DateTime.UtcNow).ToList())
            _syncTokens.TryRemove(kv.Key, out _);
        return token;
    }

    // ── 临时 Token 数据模型 ────────────────────────────────

    public class LaunchPackage
    {
        public string ExePath { get; set; } = "";
        public string SaveDir { get; set; } = "";
        public string RomPath { get; set; } = "";
        public string EmuSavePath { get; set; } = "";
        public string LaunchArgs { get; set; } = "";
        public string Type { get; set; } = "";
        public string SaveDataBase64 { get; set; } = "";
        public int Generation { get; set; }
        public int GameVersion { get; set; }
        public string TitleIdLow { get; set; } = "";
        public string FileName { get; set; } = "";
        public Guid SaveFileId { get; set; }
        public string SyncToken { get; set; } = "";
    }

    /// <summary>从本地模拟器同步回存档</summary>
    [HttpPost("sync-from-local/{saveFileId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> SyncFromLocal(Guid saveFileId)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var deviceId = GetDeviceId();
        var emuSettings = await _settingsService.GetEmulatorSettings(userId.Value, deviceId);

        var save = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid", new { Id = saveFileId, Uid = userId });
        if (save == null) return NotFound();

        var gen = save.Generation;
        var gameVersion = save.GameVersion ?? 0;
        string? saveDir;
        if (gen >= 6)
        {
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("azahar.data_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultAzaharDataDir();
        }
        else
        {
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("desmume.save_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultDeSmuMESaveDir();
        }
        if (string.IsNullOrWhiteSpace(saveDir))
            return BadRequest(ApiResponse<object>.Error(400, "模拟器存档目录未配置"));

        // Find the emulator save path
        string emuSavePath;
        if (gen >= 6)
        {
            // 3DS Azahar: title ID path (no ROM needed, CIAs are installed)
            emuSavePath = GetAzaharSavePath(saveDir, gameVersion);
        }
        else
        {
            // NDS DeSmuME: need ROM filename for .dsv
            var rom = await _db.QueryFirstOrDefaultAsync<Models.Entity.RomFileEntity>(
                "SELECT * FROM rom_files WHERE generation=@Gen LIMIT 1",
                new { Gen = gen });
            var romFileName = Path.GetFileNameWithoutExtension(rom?.LocalPath ?? rom?.GameId ?? "");
            emuSavePath = Path.Combine(saveDir, $"{romFileName}.dsv");
        }

        if (!System.IO.File.Exists(emuSavePath))
            return NotFound(ApiResponse<object>.Error(404, "模拟器存档文件不存在，请先在游戏中保存"));

        // Read back
        var pkSavePath = Path.Combine(_baseSaveDir, userId.ToString()!, saveFileId.ToString(), "save.sav");
        var data = await System.IO.File.ReadAllBytesAsync(emuSavePath);
        await System.IO.File.WriteAllBytesAsync(pkSavePath, data);

        // Update DB
        await _db.ExecuteAsync(
            "UPDATE save_files SET raw_save_data=@Data, file_size=@Size, is_modified=TRUE, updated_at=NOW() WHERE id=@Id",
            new { Data = data, Size = data.Length, Id = saveFileId });

        // Create backup (after sync)
        await _saveFileService.CreateBackup(saveFileId, userId.Value, "从本地模拟器同步");

        // 恢复本地备份（把 AZAHAR/DeSmuME 的原始存档放回去）
        var backupDir = Path.Combine(saveDir, "pkmanager_backup");
        if (gen >= 6) backupDir = Path.Combine(backupDir, GetTitleIdLow(gameVersion));
        var backupPath = Path.Combine(backupDir, gen >= 6 ? "main.bak" : "save.dsv.bak");
        bool restored = false;
        if (System.IO.File.Exists(backupPath))
        {
            System.IO.File.Copy(backupPath, emuSavePath, overwrite: true);
            restored = true;
        }
        else if (gen < 6)
        {
            // NDS: no backup means first launch — just delete the .dsv we injected
            // (Azahar: the main file we injected stays as the user's new save)
        }

        // 清理 pid.lock
        var pidLockPath = Path.Combine(saveDir, "pkmanager_backup", "pid.lock");
        if (System.IO.File.Exists(pidLockPath)) System.IO.File.Delete(pidLockPath);

        return Ok(ApiResponse<object>.Ok(new { synced = true, restored }, "存档已同步"));
    }

    /// <summary>检查本地模拟器进程是否还在运行</summary>
    [HttpGet("local-status/{saveFileId:guid}")]
    public ActionResult<ApiResponse<object>> LocalStatus(Guid saveFileId)
    {
        var kv = _runningProcesses.FirstOrDefault(kv => kv.Value.SaveFileId == saveFileId);
        if (kv.Value == default)
            return Ok(ApiResponse<object>.Ok(new { running = false }));

        try
        {
            var proc = Process.GetProcessById(kv.Key);
            return Ok(ApiResponse<object>.Ok(new { running = !proc.HasExited, pid = kv.Key }));
        }
        catch
        {
            return Ok(ApiResponse<object>.Ok(new { running = false }));
        }
    }

    /// <summary>急救恢复：将本地模拟器存档恢复为 pkmanager 备份的版本</summary>
    [HttpPost("emergency-restore/{saveFileId:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> EmergencyRestore(Guid saveFileId)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        var save = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid", new { Id = saveFileId, Uid = userId });
        if (save == null) return NotFound();

        var gen = save.Generation;
        var gameVersion = save.GameVersion ?? 0;
        var deviceId = GetDeviceId();
        var emuSettings = await _settingsService.GetEmulatorSettings(userId.Value, deviceId);

        string? saveDir;
        if (gen >= 6)
        {
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("azahar.data_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultAzaharDataDir();
        }
        else
        {
            saveDir = NormalizeDirPath(emuSettings.GetValueOrDefault("desmume.save_dir") ?? "");
            if (string.IsNullOrWhiteSpace(saveDir))
                saveDir = GetDefaultDeSmuMESaveDir();
        }
        if (string.IsNullOrWhiteSpace(saveDir))
            return BadRequest(ApiResponse<object>.Error(400, "模拟器目录未配置"));

        var backupDir = Path.Combine(saveDir, "pkmanager_backup");
        if (gen >= 6) backupDir = Path.Combine(backupDir, GetTitleIdLow(gameVersion));
        var backupPath = Path.Combine(backupDir, gen >= 6 ? "main.bak" : "save.dsv.bak");

        if (!System.IO.File.Exists(backupPath))
            return NotFound(ApiResponse<object>.Error(404, "没有找到备份文件。可能尚未启动过本地模拟器"));

        // 恢复
        var emuSavePath = gen >= 6
            ? GetAzaharSavePath(saveDir, gameVersion)
            : Path.Combine(saveDir, $"{Path.GetFileNameWithoutExtension(save.Filename)}.dsv");

        System.IO.File.Copy(backupPath, emuSavePath, overwrite: true);

        return Ok(ApiResponse<object>.Ok(new { restored = true, backupPath, targetPath = emuSavePath }, "本地存档已从备份恢复"));
    }

    // ── GBA 模拟器 AI 控制接口 ────────────────────────────

    /// <summary>发送控制命令（AI/脚本 → 浏览器）</summary>
    [HttpPost("control/send")]
    public ActionResult<ApiResponse<object>> SendCommand([FromBody] ControlCommand cmd)
    {
        var queue = GetCommandQueue(cmd.SaveFileId);
        var commandId = cmd.Id ?? Guid.NewGuid().ToString("N")[..8];
        var entry = new CommandEntry
        {
            Id = commandId,
            Action = cmd.Action,
            Params = cmd.Params ?? "{}",
            CreatedAt = DateTime.UtcNow,
        };
        queue.Pending.Enqueue(entry);
        return Ok(ApiResponse<object>.Ok(new { accepted = true, commandId }));
    }

    /// <summary>轮询待执行命令（浏览器调用）</summary>
    [HttpGet("control/poll/{saveFileId:guid}")]
    public ActionResult<ApiResponse<object>> PollCommands(Guid saveFileId)
    {
        var queue = GetCommandQueue(saveFileId);
        var pending = new List<CommandEntry>();
        while (queue.Pending.TryDequeue(out var e)) pending.Add(e);

        var results = new List<CommandResultEntry>();
        while (queue.Results.TryDequeue(out var r)) results.Add(r);

        return Ok(ApiResponse<object>.Ok(new { pending, results }));
    }

    /// <summary>提交命令执行结果（浏览器调用）</summary>
    [HttpPost("control/result")]
    public ActionResult<ApiResponse<object>> SubmitResult([FromBody] CommandResultEntry result)
    {
        var queue = GetCommandQueue(result.SaveFileId);
        // Store last 100 results for sync queries
        queue.ResultHistory.Enqueue(result);
        while (queue.ResultHistory.Count > 100) queue.ResultHistory.TryDequeue(out _);
        return Ok(ApiResponse<object>.Ok(new { logged = true }));
    }

    /// <summary>同步执行命令（阻塞等待浏览器完成）</summary>
    [HttpPost("control/execute")]
    public async Task<ActionResult<ApiResponse<object>>> ExecuteSync([FromBody] ControlCommand cmd)
    {
        var queue = GetCommandQueue(cmd.SaveFileId);
        var commandId = cmd.Id ?? Guid.NewGuid().ToString("N")[..8];
        queue.Pending.Enqueue(new CommandEntry
        {
            Id = commandId,
            Action = cmd.Action,
            Params = cmd.Params ?? "{}",
            CreatedAt = DateTime.UtcNow,
        });

        var timeout = cmd.Timeout > 0 ? cmd.Timeout : 10000;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);

        while (DateTime.UtcNow < deadline)
        {
            // Check if result arrived
            var found = queue.ResultHistory.FirstOrDefault(r => r.CommandId == commandId);
            if (found != null)
                return Ok(ApiResponse<object>.Ok(new { found.Ok, found.Data, found.Error, found.ElapsedMs }));

            await Task.Delay(200);
        }

        return StatusCode(504, ApiResponse<object>.Error(504, $"命令超时 ({timeout}ms)"));
    }

    // ── Command queue storage ─────────────────────────────

    private static readonly ConcurrentDictionary<Guid, CommandQueue> _commandQueues = new();

    private CommandQueue GetCommandQueue(Guid saveFileId)
    {
        return _commandQueues.GetOrAdd(saveFileId, _ => new CommandQueue());
    }

    // ── Helpers ──────────────────────────────────────────

    private static readonly ConcurrentDictionary<int, (Guid SaveFileId, Guid UserId, string EmuSavePath)> _runningProcesses = new();

    /// <summary>检测当前操作系统是否为 Windows</summary>
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>获取 Azahar 默认用户数据目录（按 OS 适配）</summary>
    private static string GetDefaultAzaharDataDir()
    {
        if (IsWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "azahar-emu");
        else
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "azahar-emu");
    }

    /// <summary>获取 DeSmuME 默认存档目录（按 OS 适配）</summary>
    private static string GetDefaultDeSmuMESaveDir()
    {
        if (IsWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeSmuME");
        else
            return Path.Combine(
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
                "desmume");
    }

    /// <summary>规范化用户输入路径（将 Windows 反斜杠统一转为正斜杠，方便跨平台处理）</summary>
    private static string NormalizeExePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return rawPath;
        // 去除首尾空格和引号
        var trimmed = rawPath.Trim().Trim('"', '\'');
        // Windows: 统一反斜杠为正斜杠（.NET 内部 API 兼容两者）
        if (IsWindows) return trimmed.Replace('/', '\\');
        return trimmed;
    }

    /// <summary>规范化用户输入目录路径</summary>
    private static string NormalizeDirPath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return rawPath;
        var trimmed = rawPath.Trim().Trim('"', '\'');
        if (IsWindows) return trimmed.Replace('/', '\\');
        return trimmed;
    }

    private Guid GetDeviceId()
    {
        var header = Request.Headers["X-Device-Id"].FirstOrDefault();
        return Guid.TryParse(header, out var id) ? id : Guid.NewGuid();
    }

    private static readonly Dictionary<int, string> _3dsTitleIds = new()
    {
        { 24, "00055D00" }, { 25, "00055E00" }, // X, Y
        { 26, "0011C400" }, { 27, "0011C500" }, // OR, AS
        { 30, "00164800" }, { 31, "00175E00" }, // S, M
        { 32, "001B5000" }, { 33, "001B5100" }, // US, UM
    };

    private static string GetTitleIdLow(int gameVersion) =>
        _3dsTitleIds.GetValueOrDefault(gameVersion, "00055D00");

    /// <summary>
    /// 解析 Azahar sdmc 目录路径。
    /// 用户配置的 dataDir 可能包含 sdmc 也可能不包含，这里统一处理：
    /// - 如果 dataDir 以 sdmc 结尾 → 直接返回（用户配的就是 sdmc 目录）
    /// - 否则 → 返回 dataDir/sdmc（用户配的是 sdmc 的父目录）
    /// </summary>
    private static string ResolveAzaharSdmcPath(string dataDir)
    {
        var trimmed = dataDir.TrimEnd('/', '\\');
        if (trimmed.EndsWith("sdmc", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return Path.Combine(trimmed, "sdmc");
    }

    /// <summary>构建 Azahar 存档路径（不校验服务器端文件是否存在，路径指向浏览器所在机器）</summary>
    private static string GetAzaharSavePath(string dataDir, int gameVersion)
    {
        var sdmc = ResolveAzaharSdmcPath(dataDir);
        var tidLow = GetTitleIdLow(gameVersion);
        return Path.Combine(sdmc, "Nintendo 3DS",
            "00000000000000000000000000000000",
            "00000000000000000000000000000000",
            "title", "00040000", tidLow, "data", "00000001", "main");
    }

    /// <summary>构建 Azahar 内容文件路径（.app，用于直接启动游戏）。不做服务器端文件校验。</summary>
    private static string ConstructAzaharContentPath(string dataDir, int gameVersion)
    {
        var sdmc = ResolveAzaharSdmcPath(dataDir);
        var tidLow = GetTitleIdLow(gameVersion);
        // 优先返回 00000000.app（主程序内容文件）
        return Path.Combine(sdmc, "Nintendo 3DS",
            "00000000000000000000000000000000", "00000000000000000000000000000000",
            "title", "00040000", tidLow, "content", "00000000.app");
    }
}

public class CheckLocalRequest { public int Generation { get; set; } public int? GameVersion { get; set; } public string? GameId { get; set; } }
public class SyncSaveRequest { public Guid SaveFileId { get; set; } public string? GameId { get; set; } public string? SaveDataBase64 { get; set; } }
public class RomDto { public Guid Id { get; set; } public string GameId { get; set; } = ""; public string DisplayName { get; set; } = ""; public int Generation { get; set; } public long FileSize { get; set; } }

// ── GBA 控制接口模型 ─────────────────────────────────────

public class ControlCommand
{
    public Guid SaveFileId { get; set; }
    public string? Id { get; set; }
    public string Action { get; set; } = "";
    public string? Params { get; set; }
    public int Timeout { get; set; }
}

public class CommandEntry
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public string Params { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class CommandResultEntry
{
    public Guid SaveFileId { get; set; }
    public string CommandId { get; set; } = "";
    public string Action { get; set; } = "";
    public bool Ok { get; set; }
    public string? Data { get; set; }
    public string? Error { get; set; }
    public int ElapsedMs { get; set; }
}

public class CommandQueue
{
    public ConcurrentQueue<CommandEntry> Pending { get; } = new();
    public ConcurrentQueue<CommandResultEntry> Results { get; } = new();
    public ConcurrentQueue<CommandResultEntry> ResultHistory { get; } = new();
}
