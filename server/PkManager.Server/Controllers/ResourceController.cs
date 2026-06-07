using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using PKHeX.Core;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResourceController : ControllerBase
{
    private readonly UserContext _userContext;

    // Cache: species ID → valid ability IDs
    private static readonly ConcurrentDictionary<(ushort Species, byte Form, byte Gen), int[]> _abilityCache = new();
    // Cache: species ID → valid move IDs per generation context
    private static readonly ConcurrentDictionary<(ushort Species, byte Form, byte Gen), int[]> _learnsetCache = new();

    public ResourceController(UserContext userContext)
    {
        _userContext = userContext;
    }

    /// <summary>
    /// 宝可梦物种列表（简体中文）
    /// </summary>
    [HttpGet("species")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Species([FromQuery] int? generation)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        for (int i = 1; i < strings.Species.Count; i++)
        {
            var name = strings.Species[i];
            if (!string.IsNullOrEmpty(name))
                items.Add(new ResourceItem { Id = i, Name = name });
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 招式列表（简体中文）
    /// </summary>
    [HttpGet("moves")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Moves([FromQuery] int? generation)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        for (int i = 1; i < strings.Move.Count; i++)
        {
            var name = strings.Move[i];
            if (!string.IsNullOrEmpty(name))
                items.Add(new ResourceItem { Id = i, Name = name });
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 特性列表（简体中文）
    /// </summary>
    [HttpGet("abilities")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Abilities()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        for (int i = 1; i < strings.Ability.Count; i++)
        {
            var name = strings.Ability[i];
            if (!string.IsNullOrEmpty(name))
                items.Add(new ResourceItem { Id = i, Name = name });
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 性格列表（简体中文）
    /// </summary>
    [HttpGet("natures")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Natures()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        for (int i = 0; i < Math.Min(25, strings.Natures.Count); i++)
        {
            var name = strings.Natures[i];
            if (!string.IsNullOrEmpty(name))
                items.Add(new ResourceItem { Id = i, Name = name });
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 道具列表（简体中文）
    /// </summary>
    [HttpGet("items")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Items()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        for (int i = 0; i < strings.Item.Count; i++)
        {
            var name = strings.Item[i];
            if (!string.IsNullOrEmpty(name))
                items.Add(new ResourceItem { Id = i, Name = name });
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 球种列表（简体中文）
    /// </summary>
    [HttpGet("balls")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Balls()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        // 从 PKHeX.Core 字符串表获取中文球种名称
        var strings = GameInfo.GetStrings("zh");
        var items = new List<ResourceItem>();

        if (strings.balllist != null)
        {
            for (int i = 0; i < strings.balllist.Length; i++)
            {
                var name = strings.balllist[i];
                if (!string.IsNullOrEmpty(name))
                    items.Add(new ResourceItem { Id = i, Name = name });
            }
        }

        return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
    }

    /// <summary>
    /// 游戏版本列表
    /// </summary>
    [HttpGet("games")]
    public ActionResult<ApiResponse<List<ResourceItem>>> Games()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        var games = new List<ResourceItem>
        {
            new() { Id = 0, Name = "未知" },
            new() { Id = 1, Name = "蓝宝石" },
            new() { Id = 2, Name = "红宝石" },
            new() { Id = 3, Name = "绿宝石" },
            new() { Id = 4, Name = "火红" },
            new() { Id = 5, Name = "叶绿" },
            new() { Id = 10, Name = "钻石" },
            new() { Id = 11, Name = "珍珠" },
            new() { Id = 12, Name = "白金" },
            new() { Id = 7, Name = "心金" },
            new() { Id = 8, Name = "魂银" },
            new() { Id = 20, Name = "黑" },
            new() { Id = 21, Name = "白" },
            new() { Id = 22, Name = "黑2" },
            new() { Id = 23, Name = "白2" },
            new() { Id = 24, Name = "X" },
            new() { Id = 25, Name = "Y" },
            new() { Id = 26, Name = "欧米伽红宝石" },
            new() { Id = 27, Name = "阿尔法蓝宝石" },
            new() { Id = 30, Name = "太阳" },
            new() { Id = 31, Name = "月亮" },
            new() { Id = 32, Name = "究极之日" },
            new() { Id = 33, Name = "究极之月" },
        };

        return Ok(ApiResponse<List<ResourceItem>>.Ok(games));
    }

    /// <summary>
    /// 获取 3DS 国家列表（中文，来自 PKHeX 内置数据）
    /// </summary>
    [HttpGet("geo/countries")]
    public ActionResult<ApiResponse<List<ResourceItem>>> GeoCountries()
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));
        return Ok(ApiResponse<List<ResourceItem>>.Ok(GeoData.Countries));
    }

    /// <summary>
    /// 获取指定国家的地区列表（中文）
    /// </summary>
    [HttpGet("geo/regions/{countryId:int}")]
    public ActionResult<ApiResponse<List<ResourceItem>>> GeoRegions(int countryId)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));
        if (GeoData.Regions.TryGetValue(countryId, out var regions))
            return Ok(ApiResponse<List<ResourceItem>>.Ok(regions));
        return Ok(ApiResponse<List<ResourceItem>>.Ok(new List<ResourceItem>()));
    }

    /// <summary>
    /// 获取指定物种的合法特性列表（带槽位标签，如"激流 (1)"、"激流 (2)"、"湿润之声 (H)"）
    /// </summary>
    [HttpGet("species/{speciesId:int}/abilities")]
    public ActionResult<ApiResponse<List<ResourceItem>>> SpeciesAbilities(int speciesId, [FromQuery] int generation = 7, [FromQuery] int form = 0)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        try
        {
            var abilities = GetValidAbilities((ushort)speciesId, (byte)Math.Max(0, form), generation);
            var strings = GameInfo.GetStrings("zh");

            // 槽位标签: 0=特性1, 1=特性2, 2=隐藏特性
            var slotLabels = new[] { " (1)", " (2)", " (H)" };
            // 检测是否有重复名称需要加槽位号
            var names = abilities.Select((id, _) =>
                id > 0 && id < strings.Ability.Count ? strings.Ability[id] : $"Ability {id}"
            ).ToArray();
            var hasDuplicates = names.Distinct().Count() < names.Length;

            var items = new List<ResourceItem>();
            for (int i = 0; i < abilities.Length; i++)
            {
                var abiId = abilities[i];
                var baseName = names[i];
                // 如果有重复名称，始终加槽位后缀；否则只有隐藏特性加(H)
                var label = hasDuplicates
                    ? $"{baseName}{slotLabels[i]}"
                    : i >= 2 ? $"{baseName}{slotLabels[i]}" : baseName;
                items.Add(new ResourceItem { Id = abiId, Name = label });
            }

            return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
        }
        catch
        {
            return Ok(ApiResponse<List<ResourceItem>>.Ok(new List<ResourceItem>()));
        }
    }

    /// <summary>
    /// 获取指定物种在当前世代可学习的招式列表
    /// </summary>
    [HttpGet("species/{speciesId:int}/moves")]
    public ActionResult<ApiResponse<List<ResourceItem>>> SpeciesMoves(int speciesId, [FromQuery] int generation = 7, [FromQuery] int form = 0)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<List<ResourceItem>>.Error(401, "未登录"));

        try
        {
            var moveIds = GetLearnableMoves((ushort)speciesId, (byte)Math.Max(0, form), generation);
            var strings = GameInfo.GetStrings("zh");

            var items = new List<ResourceItem>();
            foreach (var moveId in moveIds)
            {
                var name = moveId > 0 && moveId < strings.Move.Count
                    ? strings.Move[moveId] : $"Move {moveId}";
                items.Add(new ResourceItem { Id = moveId, Name = name });
            }

            return Ok(ApiResponse<List<ResourceItem>>.Ok(items));
        }
        catch
        {
            return Ok(ApiResponse<List<ResourceItem>>.Ok(new List<ResourceItem>()));
        }
    }

    /// <summary>
    /// 获取指定物种的经验成长表，用于前端同步 EXP 和等级。
    /// </summary>
    [HttpGet("species/{speciesId:int}/experience")]
    public ActionResult<ApiResponse<object>> SpeciesExperience(int speciesId, [FromQuery] int generation = 7, [FromQuery] int form = 0)
    {
        if (_userContext.UserId == null)
            return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        try
        {
            var pi = GetPersonalInfo((ushort)speciesId, (byte)Math.Max(0, form), generation);
            var growth = pi.EXPGrowth;
            var table = Experience.GetTable(growth).ToArray();
            return Ok(ApiResponse<object>.Ok(new
            {
                growthRate = growth,
                expTable = table,
            }));
        }
        catch
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                growthRate = 0,
                expTable = Array.Empty<uint>(),
            }));
        }
    }

    // ── 辅助方法 ──────────────────────────────────────

    private static int[] GetValidAbilities(ushort species, byte form, int generation)
    {
        var gen = (byte)Math.Clamp(generation, 1, 9);
        var key = (species, form, gen);
        if (_abilityCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var pi = GetPersonalInfo(species, form, generation);
            var count = pi.AbilityCount;
            var abilities = new int[count];
            for (int i = 0; i < count; i++)
                abilities[i] = pi.GetAbilityAtIndex(i);

            _abilityCache[key] = abilities;
            return abilities;
        }
        catch
        {
            return new[] { 0 };
        }
    }

    private static int[] GetLearnableMoves(ushort species, byte form, int generation)
    {
        var gen = (byte)Math.Clamp(generation, 1, 9);
        var key = (species, form, gen);
        if (_learnsetCache.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var version = GetRepresentativeVersion(generation);
            var learnSource = GameData.GetLearnSource(version);
            var moveSet = new HashSet<int>();

            foreach (var move in learnSource.GetLearnset(species, form).GetAllMoves())
                if (move > 0)
                    moveSet.Add(move);

            foreach (var move in learnSource.GetEggMoves(species, form))
                if (move > 0)
                    moveSet.Add(move);

            var blank = EntityBlank.GetBlank((byte)Math.Clamp(generation, 1, 9));
            blank.Species = species;
            blank.Form = form;
            blank.CurrentLevel = 100;
            blank.Version = version;

            var flags = new bool[Math.Max(1001, GameInfo.GetStrings("zh").Move.Count + 1)];
            var evo = new EvoCriteria
            {
                Species = species,
                Form = form,
                LevelMin = 1,
                LevelMax = 100,
                LevelUpRequired = 0,
                Method = EvolutionType.None,
            };
            learnSource.GetAllMoves(flags, blank, evo, MoveSourceType.ExternalSources);
            for (int i = 1; i < flags.Length; i++)
            {
                if (flags[i])
                    moveSet.Add(i);
            }

            var result = moveSet.OrderBy(x => x).ToArray();
            _learnsetCache[key] = result;
            return result;
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    private static PersonalInfo GetPersonalInfo(ushort species, byte form, int generation) => generation switch
    {
        <= 1 => PersonalTable.Y.GetFormEntry(species, form),
        2 => PersonalTable.C.GetFormEntry(species, form),
        3 => PersonalTable.E.GetFormEntry(species, form),
        4 => PersonalTable.HGSS.GetFormEntry(species, form),
        5 => PersonalTable.B2W2.GetFormEntry(species, form),
        6 => PersonalTable.AO.GetFormEntry(species, form),
        7 => PersonalTable.USUM.GetFormEntry(species, form),
        8 => PersonalTable.SWSH.GetFormEntry(species, form),
        9 => PersonalTable.SV.GetFormEntry(species, form),
        _ => PersonalTable.USUM.GetFormEntry(species, form),
    };

    private static GameVersion GetRepresentativeVersion(int generation) => generation switch
    {
        <= 1 => GameVersion.YW,
        2 => GameVersion.C,
        3 => GameVersion.E,
        4 => GameVersion.HGSS,
        5 => GameVersion.B2W2,
        6 => GameVersion.ORAS,
        7 => GameVersion.USUM,
        8 => GameVersion.SWSH,
        9 => GameVersion.SV,
        _ => GameVersion.USUM,
    };

    private static EntityContext GetEntityContext(int generation) => generation switch
    {
        1 => EntityContext.Gen1,
        2 => EntityContext.Gen2,
        3 => EntityContext.Gen3,
        4 => EntityContext.Gen4,
        5 => EntityContext.Gen5,
        6 => EntityContext.Gen6,
        7 => EntityContext.Gen7,
        8 => EntityContext.Gen8,
        _ => EntityContext.Gen9
    };
}

public class ResourceItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
