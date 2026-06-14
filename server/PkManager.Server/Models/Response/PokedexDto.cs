namespace PkManager.Server.Models.Response;

/// <summary>
/// 图鉴完整数据 — 包含支持状态、统计和条目列表。
/// visibleSpeciesMax / isSupported / unsupportedReason 由后端集中判定，前端不自行映射版本号范围。
/// </summary>
public class PokedexDto
{
    /// <summary>该存档是否有图鉴（信息字段，不做 UI gate）</summary>
    public bool HasPokeDex { get; set; }

    /// <summary>DB 中存储的版本号（可空；后端用 sav.Version 兜底）</summary>
    public int? GameVersion { get; set; }

    public int Generation { get; set; }

    /// <summary>后端判定的可见物种上限；0 表示「使用 TotalSpecies 作为上限」</summary>
    public int VisibleSpeciesMax { get; set; }

    /// <summary>该存档是否被 V1 图鉴面板支持（LA 等返回 false）</summary>
    public bool IsSupported { get; set; }

    /// <summary>不支持时的提示文案</summary>
    public string? UnsupportedReason { get; set; }

    /// <summary>该世代最大物种编号 (sav.MaxSpeciesID)</summary>
    public int TotalSpecies { get; set; }

    public int SeenCount { get; set; }
    public int CaughtCount { get; set; }
    public decimal PercentSeen { get; set; }
    public decimal PercentCaught { get; set; }

    public List<PokedexEntryDto> Entries { get; set; } = [];
}

/// <summary>
/// 单个图鉴条目 — 物种索引 + Seen/Caught 状态
/// </summary>
public class PokedexEntryDto
{
    public ushort Species { get; set; }
    public bool Seen { get; set; }
    public bool Caught { get; set; }
    public int? SeenGender { get; set; }
    public int[]? DisplayFormValues { get; set; }
    public uint? SpindaPID { get; set; }
    public int? LanguageFlags { get; set; }
}

/// <summary>
/// 批量操作请求 — action 白名单: seenAll / caughtAll / clearAll
/// </summary>
public class PokedexBatchRequest
{
    public string? Action { get; set; }
}
