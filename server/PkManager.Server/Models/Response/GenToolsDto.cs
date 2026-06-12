namespace PkManager.Server.Models.Response;

/// <summary>
/// 世代专属工具 capability — 前端条件渲染开关。
/// RTC（Gen3 RS/Emerald）、O-Power（Gen6 XY/ORAS）、Zygarde Cell（Gen7 SM/USUM）。
/// </summary>
public class GenToolsCapability
{
    public bool HasRtc { get; set; }
    public bool HasOPowers { get; set; }
    public bool HasZygardeCells { get; set; }
}

/// <summary>
/// 单个 RTC3 时钟条目（初始时钟 或 已流逝时钟）。
/// </summary>
public class Rtc3EntryDto
{
    /// <summary>"initial" | "elapsed"</summary>
    public string Key { get; set; } = "";

    /// <summary>中文标签："初始时钟" | "已流逝时钟"</summary>
    public string Label { get; set; } = "";

    public int Day { get; set; }
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int Second { get; set; }
}

/// <summary>
/// 单个 O-Power 类型条目（如 孵化/攻击 等）。
/// DTO key 为纯标识符，与 PKHeX 枚举的映射在后端元数据表中维护。
/// </summary>
public class OPowerTypeEntryDto
{
    /// <summary>"hatching", "spAttack" …</summary>
    public string Key { get; set; } = "";

    /// <summary>中文名："孵化", "特攻" …</summary>
    public string Name { get; set; } = "";

    /// <summary>"field" | "battle"</summary>
    public string Category { get; set; } = "";

    /// <summary>Lv.1 等级 (0-3)</summary>
    public int Level1 { get; set; }

    /// <summary>Lv.2 等级 (0-3)</summary>
    public int Level2 { get; set; }

    public bool Level1Unlocked { get; set; }
    public bool Level2Unlocked { get; set; }
    public bool Level3Unlocked { get; set; }

    /// <summary>是否有 S 变种（部分 Field 类型）</summary>
    public bool HasLevelS { get; set; }

    public bool LevelSUnlocked { get; set; }

    /// <summary>是否有 MAX 变种（部分 Field 类型）</summary>
    public bool HasLevelMax { get; set; }

    public bool LevelMaxUnlocked { get; set; }
}

/// <summary>
/// O-Power 数据（Gen6 XY/ORAS）。
/// </summary>
public class OPowerDto
{
    /// <summary>能量点数 (0-255)</summary>
    public int Points { get; set; }

    /// <summary>系统启用标志 (OPower6Index.Enable)</summary>
    public bool EnableUnlocked { get; set; }

    /// <summary>完全恢复已解锁 (OPower6Index.FullRecovery)</summary>
    public bool FullRecoveryUnlocked { get; set; }

    /// <summary>17 种 O-Power 类型（10 Field + 7 Battle）</summary>
    public List<OPowerTypeEntryDto> Entries { get; set; } = [];
}

/// <summary>
/// 单个 Zygarde Cell/Core 条目（Gen7 SM/USUM）。
/// </summary>
public class ZygardeCellDto
{
    /// <summary>0-based cell 编号 (0~94 SM, 0~99 USUM)</summary>
    public int Index { get; set; }

    /// <summary>是否已收集 (GetZygardeCell != 0)</summary>
    public bool Collected { get; set; }
}

/// <summary>
/// Zygarde Cell/Core 收集数据（Gen7 SM/USUM）。
/// </summary>
public class ZygardeDto
{
    /// <summary>已收集 cell 数量（后端从逐个 cell 遍历统计）</summary>
    public int CollectedCount { get; set; }

    /// <summary>存档中最大 cell 数（TotalZygardeCellCount: 95 SM / 100 USUM）</summary>
    public int TotalCount { get; set; }

    /// <summary>逐个 cell 状态列表</summary>
    public List<ZygardeCellDto> Cells { get; set; } = [];
}

/// <summary>
/// 世代专属工具统一响应 DTO。
/// </summary>
public class GenToolsDto
{
    public GenToolsCapability Capability { get; set; } = new();

    /// <summary>RTC 时钟条目列表（Gen3 Hoenn 非 null，其他存档为 null）</summary>
    public List<Rtc3EntryDto>? RtcEntries { get; set; }

    /// <summary>O-Power 数据（Gen6 XY/ORAS 非 null，其他存档为 null）</summary>
    public OPowerDto? OPower { get; set; }

    /// <summary>Zygarde Cell 收集数据（Gen7 SM/USUM 非 null，其他存档为 null）</summary>
    public ZygardeDto? Zygarde { get; set; }
}
