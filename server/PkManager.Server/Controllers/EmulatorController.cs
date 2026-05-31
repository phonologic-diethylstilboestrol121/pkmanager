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

    public EmulatorController(NpgsqlConnection db, SaveFileService saveFileService, ParseService parseService, UserContext userContext)
    {
        _db = db; _saveFileService = saveFileService; _parseService = parseService; _userContext = userContext;
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

    /// <summary>下载 ROM 二进制</summary>
    [HttpGet("roms/{gameId}")]
    public async Task<IActionResult> DownloadRom(string gameId)
    {
        var rom = await _db.QueryFirstOrDefaultAsync<RomFileEntity>("SELECT * FROM rom_files WHERE game_id = @Id", new { Id = gameId });
        if (rom == null) return NotFound();
        return File(rom.RomData, "application/octet-stream", $"{gameId}.gba");
    }

    /// <summary>批量导入 ROM（从服务器本地文件）</summary>
    [HttpPost("roms/import-local")]
    public async Task<ActionResult<ApiResponse<object>>> ImportLocal()
    {
        if (_userContext.UserId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        var romDir = "/home/fmangela/pkmanager/roms";
        if (!Directory.Exists(romDir)) return BadRequest(ApiResponse<object>.Error(400, "ROM目录不存在"));

        var romMap = new Dictionary<string, (string gameId, string displayName)>(StringComparer.OrdinalIgnoreCase) {
            {"红宝石", ("pkm_ruby", "宝可梦 红宝石")}, {"蓝宝石", ("pkm_sapphire", "宝可梦 蓝宝石")},
            {"绿宝石", ("pkm_emerald", "宝可梦 绿宝石")}, {"火红", ("pkm_firered", "宝可梦 火红")},
            {"叶绿", ("pkm_leafgreen", "宝可梦 叶绿")},
        };

        var imported = new List<string>();
        foreach (var file in Directory.GetFiles(romDir, "*.gba")) {
            var name = Path.GetFileNameWithoutExtension(file);
            var match = romMap.FirstOrDefault(kv => name.Contains(kv.Key));
            if (match.Key == null) continue;
            var (gid, dname) = match.Value;

            var data = await System.IO.File.ReadAllBytesAsync(file);
            var existing = await _db.QueryFirstOrDefaultAsync<RomFileEntity>("SELECT id FROM rom_files WHERE game_id=@Id", new { Id = gid });
            if (existing != null)
                await _db.ExecuteAsync("UPDATE rom_files SET rom_data=@D, file_size=@S WHERE game_id=@I", new { I = gid, D = data, S = data.Length });
            else
                await _db.ExecuteAsync("INSERT INTO rom_files (game_id,display_name,generation,rom_data,file_size) VALUES (@I,@N,3,@D,@S)",
                    new { I = gid, N = dname, D = data, S = data.Length });
            imported.Add($"{dname} ({data.Length} bytes)");
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

    /// <summary>同步存档 — 游戏内保存时自动调用</summary>
    [HttpPost("sync-save")]
    public async Task<ActionResult<ApiResponse<object>>> SyncSave([FromBody] SyncSaveRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        if (request.SaveFileId == Guid.Empty || string.IsNullOrEmpty(request.SaveDataBase64))
            return BadRequest(ApiResponse<object>.Error(400, "缺少参数"));

        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>("SELECT * FROM save_files WHERE id=@Id AND user_id=@Uid",
            new { Id = request.SaveFileId, Uid = userId.Value });
        if (saveFile == null) return NotFound();

        var data = Convert.FromBase64String(request.SaveDataBase64);
        await _db.ExecuteAsync("UPDATE save_files SET raw_save_data=@Data, file_size=@Size, is_modified=TRUE, updated_at=NOW() WHERE id=@Id",
            new { Id = request.SaveFileId, Data = data, Size = data.Length });
        return Ok(ApiResponse<object>.Ok(new { }, "存档已同步"));
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

public class SyncSaveRequest { public Guid SaveFileId { get; set; } public string? SaveDataBase64 { get; set; } }
public class RomDto { public Guid Id { get; set; } public string GameId { get; set; } = ""; public string DisplayName { get; set; } = ""; public int Generation { get; set; } public long FileSize { get; set; } }
