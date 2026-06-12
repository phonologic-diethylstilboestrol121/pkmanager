using System.Text.Json.Serialization;

namespace PkManager.Server.Models.Response;

public class SaveFileDto
{
    public Guid SaveFileId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Generation { get; set; }
    public int? GameVersion { get; set; }
    public string? GameVersionName { get; set; }
    public string? TrainerName { get; set; }
    public int? TrainerId { get; set; }
    public int? SecretId { get; set; }
    public int PlayTime { get; set; }
    public int BoxCount { get; set; }
    public int PokemonCount { get; set; }
    public bool IsModified { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveFileDetailDto : SaveFileDto
{
    public List<BoxDto> Boxes { get; set; } = new();
    public List<BoxSlotDto> Party { get; set; } = new();
}

public class BoxDto
{
    public int BoxIndex { get; set; }
    public string BoxName { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public List<BoxSlotDto> Slots { get; set; } = new();
}

public class BoxSlotDto
{
    public int SlotIndex { get; set; }
    public bool IsEmpty { get; set; } = true;
    public PokemonDto? Pokemon { get; set; }
}

public class PokemonDto
{
    public Guid? Id { get; set; }

    // ── Main Tab ────────────────────────────
    public int Species { get; set; }
    public string SpeciesName { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public bool IsNicknamed { get; set; }
    public int Gender { get; set; }
    public int Level { get; set; }
    public int Nature { get; set; }
    public string NatureName { get; set; } = string.Empty;
    public int Ability { get; set; }
    public string AbilityName { get; set; } = string.Empty;
    public bool IsShiny { get; set; }
    public bool IsEgg { get; set; }
    public int HeldItem { get; set; }
    public string? HeldItemName { get; set; }
    public int Ball { get; set; }
    public string? BallName { get; set; }
    public byte Form { get; set; }
    public string? FormName { get; set; }
    public byte FormArgument { get; set; }
    public int Language { get; set; }
    public string? LanguageName { get; set; }
    public int EXP { get; set; }
    public int OriginalTrainerFriendship { get; set; }
    public int HandlingTrainerFriendship { get; set; }
    public byte PokerusStrain { get; set; }
    public byte PokerusDays { get; set; }
    public bool FatefulEncounter { get; set; }
    public byte HeightScalar { get; set; }
    public byte WeightScalar { get; set; }
    public byte Scale { get; set; }

    // ── Stats Tab ────────────────────────────
    public int[] IVs { get; set; } = new int[6];
    public int[] EVs { get; set; } = new int[6];
    public int[] BaseStats { get; set; } = new int[6];  // HP/ATK/DEF/SPA/SPD/SPE — 种族值
    public int[] CalculatedStats { get; set; } = new int[6];  // HP/ATK/DEF/SPA/SPD/SPE — 实际能力值
    public int HiddenPowerType { get; set; }
    public int[]? AVs { get; set; }   // LGPE
    public int[]? GVs { get; set; }   // LA
    public int? DynamaxLevel { get; set; }
    public bool CanGigantamax { get; set; }
    public int? TeraTypeOriginal { get; set; }
    public int? TeraTypeOverride { get; set; }
    public bool IsAlpha { get; set; }
    public bool IsNoble { get; set; }
    public byte StatNature { get; set; }

    // ── Moves Tab ────────────────────────────
    public MoveDto[] Moves { get; set; } = Array.Empty<MoveDto>();
    public int[] MovePPs { get; set; } = new int[4];
    public int[] MovePPUps { get; set; } = new int[4];
    public int[]? RelearnMoves { get; set; }  // Gen6+
    public string[]? RelearnMoveNames { get; set; }

    // ── Met Tab ──────────────────────────────
    public uint PID { get; set; }
    public uint EC { get; set; }  // Encryption Constant (Gen6+ 独立，Gen5- 同PID)
    public int? MetLocation { get; set; }
    public string? MetLocationName { get; set; }
    public int? MetLevel { get; set; }
    public string? MetDate { get; set; }
    public int? OriginGame { get; set; }
    public string? OriginGameName { get; set; }
    public int? EggLocation { get; set; }
    public string? EggDate { get; set; }
    public int? MetTimeOfDay { get; set; }
    public int? GroundTile { get; set; }
    public int? BattleVersion { get; set; }
    public byte? ObedienceLevel { get; set; }

    // ── OT/Misc Tab ──────────────────────────
    public int TID { get; set; }
    public int SID { get; set; }
    public string? OriginalTrainerName { get; set; }
    public int OriginalTrainerGender { get; set; }
    public string? HandlingTrainerName { get; set; }
    public int HandlingTrainerGender { get; set; }
    public int HandlingTrainerLanguage { get; set; }
    public int? Affection { get; set; }
    public string? HomeTracker { get; set; }  // hex string
    public bool IsFavorite { get; set; }
    public int[]? GeoCountry { get; set; }  // 5 values
    public int[]? GeoRegion { get; set; }   // 5 values
    public int? Country { get; set; }
    public string? CountryName { get; set; }
    public int? SubRegion { get; set; }
    public string? SubRegionName { get; set; }
    public int? ConsoleRegion { get; set; }
    public string? ConsoleRegionName { get; set; }
    public int? AffixedRibbon { get; set; }

    // ── Cosmetic Tab ─────────────────────────
    public int[] Markings { get; set; } = new int[6];
    public byte ContestCool { get; set; }
    public byte ContestBeauty { get; set; }
    public byte ContestCute { get; set; }
    public byte ContestSmart { get; set; }
    public byte ContestTough { get; set; }
    public byte ContestSheen { get; set; }
    public int? OriginMark { get; set; }

    // ── Gen-Specific Tab ─────────────────────
    // Gen3 Colosseum/XD Shadow (IShadowCapture)
    [JsonPropertyName("shadowId")]
    public int? ShadowID { get; set; }
    public int? Purification { get; set; }
    public bool IsShadow { get; set; }

    // Gen4 HGSS Shiny Leaves (G4PKM — raw bitfield: bit0-4=leaves, bit5=crown)
    public int? ShinyLeaf { get; set; }

    // Gen5 NSparkle / PokeStar (PK5 class props)
    [JsonPropertyName("nSparkle")]
    public bool? NSparkle { get; set; }
    public byte? PokeStarFame { get; set; }
    public bool IsPokeStar { get; set; }

    // Gen6-7 Super Training (ISuperTrain + ISuperTrainRegimen)
    public bool SuperTrainingEnabled { get; set; }
    public bool? SecretSuperTrainingUnlocked { get; set; }
    public bool SuperTrainSupremelyTrained { get; set; }
    public bool[]? SuperTrainRegimenFlags { get; set; }
    public bool[]? DistSuperTrainFlags { get; set; }

    // Gen6-7 Amie Fullness/Enjoyment (IFullnessEnjoyment)
    public byte? Fullness { get; set; }
    public byte? Enjoyment { get; set; }

    // Gen7 Hyper Training (IHyperTrain)
    public bool HyperTrainingEnabled { get; set; }
    public bool[]? HyperTrainFlags { get; set; }

    // Gen7 LGPE (PB7 + ICombatPower)
    public int? CombatPower { get; set; }
    public byte? Spirit { get; set; }
    public byte? Mood { get; set; }

    // ── General ──────────────────────────────
    public int Format { get; set; }          // PKM format (3=Gen3 PK3, 4=Gen4 PK4, ..., 7=Gen7 PK7)
    public bool IsValid { get; set; } = true;
    public string? PkmDataBase64 { get; set; }
}

public class MoveDto
{
    public int MoveId { get; set; }
    public string MoveName { get; set; } = string.Empty;
    public byte MoveType { get; set; }
    public string? MoveTypeName { get; set; }
    public byte MoveCategory { get; set; }  // 0=Status, 1=Physical, 2=Special
    public byte? BasePower { get; set; }
    public byte? Accuracy { get; set; }
    public byte BasePP { get; set; }
}

public class SaveBackupDto
{
    public Guid Id { get; set; }
    public Guid SaveFileId { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PokemonCount { get; set; }
    public string TrainerName { get; set; } = string.Empty;
    public string PlayTime { get; set; } = string.Empty;
    public string GameVersion { get; set; } = string.Empty;
    public int BoxCount { get; set; }
}
