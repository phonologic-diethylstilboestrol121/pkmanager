namespace PkManager.Server.Models.Response;

/// <summary>
/// 世代专属工具 capability — 前端条件渲染开关。
/// RTC（Gen3 RS/Emerald）、O-Power（Gen6 XY/ORAS）、Zygarde Cell（Gen7 SM/USUM）、
/// Holo Caster（Gen6 XY/ORAS）、Festa/Pelago/Totem（Gen7 SM/USUM）、Rotom（Gen7 USUM）。
/// </summary>
public class GenToolsCapability
{
    public bool HasRtc { get; set; }
    public bool HasOPowers { get; set; }
    public bool HasZygardeCells { get; set; }
    public bool HasEntreeForest { get; set; }
    public bool HasEntralink { get; set; }
    public bool HasCGearSkin { get; set; }
    public bool HasHoloCaster { get; set; }
    public bool HasFesta { get; set; }
    public bool HasPelago { get; set; }
    public bool HasTotemStamps { get; set; }
    public bool HasRotomDex { get; set; }
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
/// Holo Caster 数据（Gen6 XY/ORAS）— 只读。
/// PKHeX 当前未解析 Holo Caster 数据结构，仅暴露区块是否有非零内容。
/// </summary>
public class HoloCasterDto
{
    /// <summary>数据区域是否有非零内容</summary>
    public bool DataPresent { get; set; }
}

/// <summary>
/// Festival Plaza 数据（Gen7 SM/USUM）— 只读。
/// </summary>
public class FestaDto
{
    /// <summary>当前 Festival Coins (0-9,999,999)</summary>
    public int FestaCoins { get; set; }

    /// <summary>累计 Festival Coins</summary>
    public int TotalFestaCoins { get; set; }

    /// <summary>Festival Plaza 等级</summary>
    public int FestaRank { get; set; }
}

/// <summary>
/// Poké Pelago 数据（Gen7 SM/USUM）— 只读。
/// </summary>
public class PelagoDto
{
    /// <summary>已占用槽位数</summary>
    public int OccupiedSlots { get; set; }

    /// <summary>总槽位数 (93)</summary>
    public int TotalSlots { get; set; }

    /// <summary>15 种豆子数量（7 普通 + 7 花纹 + 1 彩虹）</summary>
    public List<int> BeanCounts { get; set; } = [];

    /// <summary>Poké Pelago 访问次数 (Record 054)</summary>
    public int Visits { get; set; }

    /// <summary>孵蛋数 (Record 060)</summary>
    public int EggsHatched { get; set; }

    /// <summary>寻宝次数 (Record 160)</summary>
    public int TreasureHunts { get; set; }
}

/// <summary>
/// 训练家护照印章项（Gen7 SM/USUM）— 只读。
/// </summary>
public class TotemStampItem
{
    /// <summary>中文名称</summary>
    public string Name { get; set; } = "";

    /// <summary>是否已获得</summary>
    public bool Earned { get; set; }
}

/// <summary>
/// 贴纸与护照印章数据（Gen7 SM/USUM）— 只读。
/// </summary>
public class TotemStampsDto
{
    /// <summary>收集的贴纸数量 (Record 72)</summary>
    public int StickersCollected { get; set; }

    /// <summary>15 个护照印章状态</summary>
    public List<TotemStampItem> Stamps { get; set; } = [];
}

/// <summary>
/// Rotom 图鉴数据（Gen7 USUM）— 只读。
/// </summary>
public class RotomDexDto
{
    /// <summary>洛托姆好感度 0-1000</summary>
    public int Affection { get; set; }

    /// <summary>Roto Loto 1 已启用</summary>
    public bool RotoLoto1 { get; set; }

    /// <summary>Roto Loto 2 已启用</summary>
    public bool RotoLoto2 { get; set; }

    /// <summary>洛托姆图鉴昵称</summary>
    public string? Nickname { get; set; }
}

/// <summary>
/// 单个 Entree Forest 槽位（Gen5 Dream World）— 只读。
/// </summary>
public class EntreeSlotDto
{
    public int Index { get; set; }
    public int Species { get; set; }
    public int Move { get; set; }
    public int Gender { get; set; }
    public int Form { get; set; }
    public bool IsOccupied { get; set; }
    public bool IsInvisible { get; set; }
    public int Area { get; set; }
}

/// <summary>
/// Entree Forest 概览（Gen5 BW/B2W2）— 只读。
/// </summary>
public class EntreeForestDto
{
    public int TotalSlots { get; set; }
    public int OccupiedSlots { get; set; }
    public bool Unlock9thArea { get; set; }
    public int Unlock38Areas { get; set; }
    public List<EntreeSlotDto> Slots { get; set; } = [];
}

/// <summary>
/// Entralink 概览（Gen5 BW/B2W2）— 只读。
/// </summary>
public class EntralinkDto
{
    public int WhiteForestLevel { get; set; }
    public int BlackCityLevel { get; set; }
    public int? MissionsComplete { get; set; }
    public int? PassPower1 { get; set; }
    public int? PassPower2 { get; set; }
    public int? PassPower3 { get; set; }
}

/// <summary>
/// C-Gear Skin 状态（Gen5 BW/B2W2）— 只读。
/// </summary>
public class CGearSkinDto
{
    public bool HasCGearSkin { get; set; }
    public int Checksum { get; set; }
    public int DataSize { get; set; }
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

    /// <summary>Entree Forest 数据（Gen5 BW/B2W2 非 null）— 只读</summary>
    public EntreeForestDto? EntreeForest { get; set; }

    /// <summary>Entralink 数据（Gen5 BW/B2W2 非 null）— 只读</summary>
    public EntralinkDto? Entralink { get; set; }

    /// <summary>C-Gear Skin 数据（Gen5 BW/B2W2 非 null）— 只读</summary>
    public CGearSkinDto? CGearSkin { get; set; }

    /// <summary>Holo Caster 数据（Gen6 XY/ORAS 非 null）— 只读</summary>
    public HoloCasterDto? HoloCaster { get; set; }

    /// <summary>Festival Plaza 数据（Gen7 SM/USUM 非 null）— 只读</summary>
    public FestaDto? Festa { get; set; }

    /// <summary>Poké Pelago 数据（Gen7 SM/USUM 非 null）— 只读</summary>
    public PelagoDto? Pelago { get; set; }

    /// <summary>贴纸与护照印章（Gen7 SM/USUM 非 null）— 只读</summary>
    public TotemStampsDto? TotemStamps { get; set; }

    /// <summary>Rotom 图鉴数据（Gen7 USUM 非 null）— 只读</summary>
    public RotomDexDto? RotomDex { get; set; }
}
