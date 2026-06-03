using System.IdentityModel.Tokens.Jwt;
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
    private readonly NpgsqlConnection _db;
    private readonly SaveFileService _saveFileService;
    private readonly ParseService _parseService;
    private readonly UserContext _userContext;
    private readonly string _baseSaveDir;

    public EmulatorController(NpgsqlConnection db, SaveFileService saveFileService, ParseService parseService, UserContext userContext, IWebHostEnvironment env)
    {
        _db = db; _saveFileService = saveFileService; _parseService = parseService; _userContext = userContext;
        _baseSaveDir = Path.Combine(env.ContentRootPath, "data", "saves");
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
        if (userId == null)
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
}

public class SyncSaveRequest { public Guid SaveFileId { get; set; } public string? GameId { get; set; } public string? SaveDataBase64 { get; set; } }
public class RomDto { public Guid Id { get; set; } public string GameId { get; set; } = ""; public string DisplayName { get; set; } = ""; public int Generation { get; set; } public long FileSize { get; set; } }
