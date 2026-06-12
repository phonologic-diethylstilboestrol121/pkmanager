using System.ComponentModel.DataAnnotations;

namespace PkManager.Server.Models.Request;

public class PokemonEditRequest
{
    // ── Main Tab ──────────────────────────────────────
    public int? Species { get; set; }
    public string? Nickname { get; set; }
    public bool? IsNicknamed { get; set; }
    public byte? Gender { get; set; }
    public byte? Nature { get; set; }
    public int? Ability { get; set; }
    public bool? IsShiny { get; set; }
    public bool? IsEgg { get; set; }
    public int? Level { get; set; }
    public int? HeldItem { get; set; }
    public int? Ball { get; set; }
    public byte? Form { get; set; }
    public byte? FormArgument { get; set; }
    public int? Language { get; set; }
    public int? EXP { get; set; }
    public byte? Friendship { get; set; }
    public byte? HandlingTrainerFriendship { get; set; }
    public byte? PokerusStrain { get; set; }
    public byte? PokerusDays { get; set; }
    public bool? FatefulEncounter { get; set; }
    public int? ShinyType { get; set; }  // 0=Random, 1=Star, 2=Square (Gen8+)
    public byte? HeightScalar { get; set; }
    public byte? WeightScalar { get; set; }
    public byte? Scale { get; set; }  // 0-3: XS/S/M/L/XL (Gen8 LA/SV)

    // ── Stats Tab ─────────────────────────────────────
    public int[]? IVs { get; set; }  // [HP, ATK, DEF, SPA, SPD, SPE]
    public int[]? EVs { get; set; }  // [HP, ATK, DEF, SPA, SPD, SPE]
    public int[]? AVs { get; set; }  // LGPE Awakening Values
    public int[]? GVs { get; set; }  // LA Grit Values
    public int? DynamaxLevel { get; set; }  // Gen8 SwSh
    public bool? CanGigantamax { get; set; }  // Gen8 SwSh
    public int? TeraTypeOriginal { get; set; }  // Gen9 SV
    public int? TeraTypeOverride { get; set; }  // Gen9 SV
    public bool? IsAlpha { get; set; }  // LA/ZA
    public bool? IsNoble { get; set; }  // LA
    public byte? StatNature { get; set; }  // Gen8+

    // ── Moves Tab ─────────────────────────────────────
    public int[]? Moves { get; set; }  // [Move1, Move2, Move3, Move4]
    public int[]? MovePPs { get; set; }  // Current PP for each move
    public int[]? MovePPUps { get; set; }  // PP Up count for each move
    public int[]? RelearnMoves { get; set; }  // [Relearn1..4] (Gen6+)

    // ── Met Tab ───────────────────────────────────────
    public int? MetLocation { get; set; }
    public byte? MetLevel { get; set; }
    public int? OriginGame { get; set; }
    public string? MetDate { get; set; }  // yyyy-MM-dd
    public int? EggLocation { get; set; }
    public string? EggDate { get; set; }
    public int? MetTimeOfDay { get; set; }  // Gen2
    public int? GroundTile { get; set; }  // Gen4
    public int? BattleVersion { get; set; }  // Gen8+
    public byte? ObedienceLevel { get; set; }  // Gen9+

    // ── OT/Misc Tab ───────────────────────────────────
    public string? OriginalTrainerName { get; set; }
    public byte? OriginalTrainerGender { get; set; }
    public ushort? TID16 { get; set; }
    public ushort? SID16 { get; set; }
    public string? HandlingTrainerName { get; set; }
    public byte? HandlingTrainerGender { get; set; }
    public int? HandlingTrainerLanguage { get; set; }
    public byte? Affection { get; set; }  // Gen6+ Amie
    public int? HomeTracker { get; set; }  // Gen8+
    public bool? IsFavorite { get; set; }  // Gen7b+
    // Geo Locations (Gen6-7)
    public int? GeoLocation1_Country { get; set; }
    public int? GeoLocation1_Region { get; set; }
    public int? GeoLocation2_Country { get; set; }
    public int? GeoLocation2_Region { get; set; }
    public int? GeoLocation3_Country { get; set; }
    public int? GeoLocation3_Region { get; set; }
    public int? GeoLocation4_Country { get; set; }
    public int? GeoLocation4_Region { get; set; }
    public int? GeoLocation5_Country { get; set; }
    public int? GeoLocation5_Region { get; set; }
    // IRegionOrigin (Gen6-7)
    public int? Country { get; set; }
    public int? SubRegion { get; set; }
    public int? ConsoleRegion { get; set; }
    // Affixed Ribbon/Mark (Gen8+)
    public int? AffixedRibbon { get; set; }

    // ── Cosmetic Tab ──────────────────────────────────
    public int[]? Markings { get; set; }  // 6 values: 0=None, 1=Blue, 2=Red
    public byte? ContestCool { get; set; }
    public byte? ContestBeauty { get; set; }
    public byte? ContestCute { get; set; }
    public byte? ContestSmart { get; set; }
    public byte? ContestTough { get; set; }
    public byte? ContestSheen { get; set; }  // Gen3-4

    // ── Gen-Specific Tab ───────────────────────────────
    // Gen3 Colosseum/XD Shadow
    public int? Purification { get; set; }

    // Gen4 HGSS Shiny Leaves (raw bitfield — front-end preserves bits 6-7)
    public int? ShinyLeaf { get; set; }

    // Gen5 NSparkle / PokeStar
    public bool? NSparkle { get; set; }
    public byte? PokeStarFame { get; set; }

    // Gen6-7 Super Training
    public bool? SecretSuperTrainingUnlocked { get; set; }
    public bool[]? SuperTrainRegimenFlags { get; set; }
    public bool[]? DistSuperTrainFlags { get; set; }

    // Gen6-7 Amie Fullness/Enjoyment
    public byte? Fullness { get; set; }
    public byte? Enjoyment { get; set; }

    // Gen7 Hyper Training
    public bool[]? HyperTrainFlags { get; set; }

    // Gen7 LGPE (PB7 + ICombatPower)
    public int? CombatPower { get; set; }
    public byte? Spirit { get; set; }
    public byte? Mood { get; set; }

    // ── Ribbons ───────────────────────────────────────
    public int[]? RibbonFlags { get; set; }  // 0-based indices of enabled ribbons
}

/// <summary>
/// 随行宝可梦编辑请求（直接修改存档原始二进制）
/// </summary>
public class PartyPokemonEditRequest : PokemonEditRequest
{
    public string PkmDataBase64 { get; set; } = string.Empty;
    public Guid SaveFileId { get; set; }
    public int SlotIndex { get; set; }
}

/// <summary>
/// 存档槽位编辑请求（统一 Box 和 Party，直接写二进制）
/// </summary>
public class SaveSlotEditRequest : PokemonEditRequest
{
    public string PkmDataBase64 { get; set; } = string.Empty;
    public Guid SaveFileId { get; set; }
    public int BoxIndex { get; set; }
    public int SlotIndex { get; set; }
    public bool IsParty { get; set; }
}

/// <summary>
/// QR 码生成请求
/// </summary>
public class QrGenerateRequest
{
    public string PkmDataBase64 { get; set; } = string.Empty;
}
