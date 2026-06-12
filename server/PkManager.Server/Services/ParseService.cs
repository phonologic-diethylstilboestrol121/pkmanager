using System.Linq;
using System.Text;
using PKHeX.Core;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Services;

/// <summary>
/// 存档与单PM解析服务 — 调用 PKHeX.Core 引擎
/// </summary>
public class ParseService
{
    private const int DeSmuMEFooterSize = 0x7A;
    private const int NdsRawSaveSize = SaveUtil.SIZE_G4RAW;
    private static readonly byte[] DeSmuMEMarker = Encoding.ASCII.GetBytes("|-DESMUME SAVE-|");

    private static bool IsDeSmuMESave(byte[] saveData)
    {
        if (saveData.Length != NdsRawSaveSize + DeSmuMEFooterSize)
            return false;

        var tail = saveData.AsSpan(saveData.Length - DeSmuMEMarker.Length, DeSmuMEMarker.Length);
        return tail.SequenceEqual(DeSmuMEMarker);
    }

    private static byte[] GetCoreSaveBytes(byte[] saveData)
    {
        if (IsDeSmuMESave(saveData))
            return saveData[..NdsRawSaveSize];
        return (byte[])saveData.Clone();
    }

    public static PKHeX.Core.SaveFile OpenSaveFile(byte[] saveData, string? fileName = null)
    {
        var parseBuffer = GetCoreSaveBytes(saveData);
        var sav = SaveUtil.GetSaveFile(parseBuffer);
        if (sav == null)
            throw new BusinessException("不支持的存档格式或文件已损坏");
        return sav;
    }

    public static byte[] FinalizeSaveBytes(PKHeX.Core.SaveFile sav, byte[] originalBytes)
    {
        var coreBytes = sav.Write().ToArray();
        if (!IsDeSmuMESave(originalBytes))
            return coreBytes;

        var footer = originalBytes[NdsRawSaveSize..];
        var result = new byte[coreBytes.Length + footer.Length];
        Buffer.BlockCopy(coreBytes, 0, result, 0, coreBytes.Length);
        Buffer.BlockCopy(footer, 0, result, coreBytes.Length, footer.Length);
        return result;
    }

    /// <summary>
    /// 解析存档文件，返回所有箱子及内部宝可梦的结构化数据
    /// </summary>
    public SaveFileDetailDto ParseSaveFile(byte[] saveData, string fileName)
    {
        // 文件大小检查，给出友好提示
        var knownSizes = new Dictionary<int, string>
        {
            { 131072, "GBA (128KB) — 红/蓝/绿宝石、火红/叶绿" },
            { 524288, "NDS (512KB) — 珍珠/钻石/白金/心金/魂银/黑/白" },
            { 1048576, "3DS (1MB) — X/Y/OR/AS/太阳/月亮" },
        };

        var sizeHint = knownSizes.ContainsKey(saveData.Length)
            ? $"（识别为 {knownSizes[saveData.Length]} 存档）"
            : $"（文件大小 {saveData.Length} 字节，常见大小: {string.Join(", ", knownSizes.Select(k => $"{k.Key}"))}）";

        PKHeX.Core.SaveFile sav;
        try
        {
            // DeSmuME .dsv 带 footer，解析前需要剥离外层包装。
            sav = OpenSaveFile(saveData, fileName);
        }
        catch (BusinessException)
        {
            throw new BusinessException(
                $"不支持的存档格式或文件已损坏 {sizeHint}。" +
                " 请确认：1) 存档来自宝可梦主系列游戏 2) 文件未损坏 3) 文件大小正确");
        }

        var boxes = new List<BoxDto>();

        for (int boxIndex = 0; boxIndex < sav.BoxCount; boxIndex++)
        {
            var boxData = sav.GetBoxData(boxIndex);
            var slots = new List<BoxSlotDto>();

            for (int slot = 0; slot < boxData.Length; slot++)
            {
                var pkm = boxData[slot];

                if (pkm.Species == 0 || !pkm.Valid)
                {
                    slots.Add(new BoxSlotDto { SlotIndex = slot, IsEmpty = true });
                }
                else
                {
                    slots.Add(new BoxSlotDto
                    {
                        SlotIndex = slot,
                        IsEmpty = false,
                        Pokemon = MapToPokemonDto(pkm)
                    });
                }
            }

            // 获取箱子名称（通过 IBoxDetailNameRead 接口）
            var boxName = (sav as IBoxDetailNameRead)?.GetBoxName(boxIndex) ?? $"Box {boxIndex + 1}";

            boxes.Add(new BoxDto
            {
                BoxIndex = boxIndex,
                BoxName = boxName,
                Capacity = boxData.Length,
                Slots = slots
            });
        }

        // 解析随行宝可梦 (Party)
        var party = new List<BoxSlotDto>();
        for (int i = 0; i < 6; i++)
        {
            var pkm = sav.GetPartySlotAtIndex(i);
            if (pkm != null && pkm.Species > 0 && pkm.Valid)
            {
                party.Add(new BoxSlotDto
                {
                    SlotIndex = i,
                    IsEmpty = false,
                    Pokemon = MapToPokemonDto(pkm)
                });
            }
            else
            {
                party.Add(new BoxSlotDto { SlotIndex = i, IsEmpty = true });
            }
        }

        var totalPokemon = boxes.Sum(b => b.Slots.Count(s => !s.IsEmpty)) + party.Count(s => !s.IsEmpty);

        return new SaveFileDetailDto
        {
            Filename = fileName,
            FileSize = saveData.Length,
            Generation = sav.Generation,
            GameVersion = (int)sav.Version,
            GameVersionName = GameInfo.GetVersionName(sav.Version),
            TrainerName = sav.OT,
            TrainerId = sav.TID16,
            SecretId = sav.SID16,
            PlayTime = (int)(sav.PlayedHours * 3600 + sav.PlayedMinutes * 60 + sav.PlayedSeconds),
            BoxCount = sav.BoxCount,
            PokemonCount = totalPokemon,
            Boxes = boxes,
            Party = party
        };
    }

    /// <summary>
    /// 解析单个 .pk* 文件
    /// </summary>
    public PokemonDto ParseSinglePokemon(byte[] pkmData)
    {
        var pkm = EntityFormat.GetFromBytes(pkmData);
        if (pkm == null)
            throw new BusinessException("不支持的宝可梦文件格式");

        return MapToPokemonDto(pkm);
    }

    /// <summary>
    /// 从 Base64 字符串重建 PKM 对象
    /// </summary>
    public PKM RebuildPkm(string base64Data)
    {
        var data = Convert.FromBase64String(base64Data);
        var format = EntityFormat.GetFormat(data);
        var pkm = EntityFormat.GetFromBytes(data);
        if (pkm == null)
            throw new BusinessException("无法重建宝可梦对象");

        return pkm;
    }

    // ── 映射方法 ────────────────────────────────────────

    /// <summary>
    /// PKM 对象 → 前端可消费的 DTO
    /// </summary>
    public static PokemonDto MapToPokemonDto(PKM pkm)
    {
        var strings = GameInfo.GetStrings("zh");
        var moveStrings = strings.Move;
        var typeStrings = GameInfo.GetStrings("zh").Types;

        // 种族值（Base Stats）
        var baseStats = new int[6];
        try
        {
            var pi = pkm.PersonalInfo;
            baseStats[0] = pi.HP;
            baseStats[1] = pi.ATK;
            baseStats[2] = pi.DEF;
            baseStats[3] = pi.SPA;
            baseStats[4] = pi.SPD;
            baseStats[5] = pi.SPE;
        }
        catch { /* use zeros */ }

        // 计算实际战斗能力值 — box Pokémon 的 Stat_HPMax 等为 0，需要手动计算
        var calculatedStats = new int[6];
        try
        {
            // Use PKHeX.Core built-in stat calculation via PersonalInfo
            var pi = pkm.PersonalInfo;
            if (pi != null)
            {
                var stats = pkm.GetStats(pi);
                calculatedStats[0] = stats[0];  // HP
                calculatedStats[1] = stats[1];  // ATK
                calculatedStats[2] = stats[2];  // DEF
                calculatedStats[3] = stats[4];  // SPA
                calculatedStats[4] = stats[5];  // SPD
                calculatedStats[5] = stats[3];  // SPE
            }
        }
        catch (Exception)
        {
            // Fallback: manual stat calculation
            CalculateStatsManually(pkm, calculatedStats);
        }

        // Hidden Power type from IVs (Gen2+)
        var hiddenPowerType = ((pkm.IV_HP & 1) + (pkm.IV_ATK & 1) * 2 + (pkm.IV_DEF & 1) * 4 +
                               (pkm.IV_SPE & 1) * 8 + (pkm.IV_SPA & 1) * 16 + (pkm.IV_SPD & 1) * 32) * 15 / 63;

            // 招式详情
        var moveDetails = new MoveDto[4];
        for (int i = 0; i < 4; i++)
        {
            var moveId = pkm.GetMove(i);
            var moveName = GetSafeString(moveStrings, moveId, $"招式{moveId}");
            byte moveType = 0;
            string? moveTypeName = null;
            byte? basePower = null;
            byte? accuracy = null;
            byte basePP = 0;
            if (moveId > 0)
            {
                moveType = MoveInfo.GetType(moveId, pkm.Context);
                moveTypeName = GetSafeString(typeStrings, moveType, "");
                basePP = MoveInfo.GetPP(pkm.Context, (ushort)moveId);
            }
            moveDetails[i] = new MoveDto
            {
                MoveId = moveId,
                MoveName = moveName,
                MoveType = moveType,
                MoveTypeName = moveTypeName,
                MoveCategory = 0,  // Not easily accessible from PKHeX.Core static API
                BasePower = basePower,
                Accuracy = accuracy,
                BasePP = basePP
            };
        }

        // 回忆招式 (Gen6+) — RelearnMoves is on base PKM class
        int[]? relearnMoves = null;
        string[]? relearnMoveNames = null;
        try
        {
            var rlSpan = pkm.RelearnMoves;
            if (rlSpan != null && rlSpan.Length > 0)
            {
                var count = Math.Min(4, rlSpan.Length);
                relearnMoves = new int[4];
                relearnMoveNames = new string[4];
                for (int i = 0; i < 4; i++)
                {
                    var rmId = i < count ? (int)rlSpan[i] : 0;
                    relearnMoves[i] = rmId;
                    relearnMoveNames[i] = rmId > 0 ? GetSafeString(moveStrings, rmId, "") : "";
                }
            }
        }
        catch { /* RelearnMoves not available for this format */ }

        // Geo locations (Gen6-7)
        int[]? geoCountry = null;
        int[]? geoRegion = null;
        if (pkm is IGeoTrack)
        {
            // Access GeoLocation array via reflection
            var geoProp = pkm.GetType().GetProperty("GeoLocation");
            if (geoProp != null)
            {
                var geoVal = geoProp.GetValue(pkm);
                if (geoVal is IReadOnlyList<int> geoList && geoList.Count >= 10)
                {
                    geoCountry = new int[5];
                    geoRegion = new int[5];
                    for (int i = 0; i < 5; i++)
                    {
                        geoCountry[i] = geoList[i * 2];
                        geoRegion[i] = geoList[i * 2 + 1];
                    }
                }
            }
        }

        return new PokemonDto
        {
            // ── Main Tab ────────────────────────────
            Species = pkm.Species,
            SpeciesName = GetSafeString(strings.Species, pkm.Species, $"#{pkm.Species}"),
            Nickname = pkm.Nickname,
            IsNicknamed = pkm.IsNicknamed,
            Gender = (byte)pkm.Gender,
            Level = pkm.CurrentLevel,
            Nature = (byte)pkm.Nature,
            NatureName = GetSafeString(strings.Natures, (int)pkm.Nature, $"Nature {(int)pkm.Nature}"),
            Ability = pkm.Ability,
            AbilityName = GetSafeString(strings.Ability, pkm.Ability, $"Ability {pkm.Ability}"),
            IsShiny = pkm.IsShiny,
            IsEgg = pkm.IsEgg,
            HeldItem = pkm.HeldItem,
            HeldItemName = GetSafeString(strings.Item, pkm.HeldItem, ""),
            Ball = pkm.Ball,
            BallName = GetSafeString(strings.balllist, pkm.Ball, $"Ball {pkm.Ball}"),
            Form = pkm.Form,
            FormName = pkm.Form > 0 ? $"Form {pkm.Form}" : null,
            FormArgument = GetFormArgumentSafe(pkm),
            Language = pkm.Language,
            LanguageName = GetLanguageName(pkm.Language),
            EXP = (int)pkm.EXP,
            OriginalTrainerFriendship = pkm.OriginalTrainerFriendship,
            HandlingTrainerFriendship = pkm.HandlingTrainerFriendship,
            PokerusStrain = (byte)pkm.PokerusStrain,
            PokerusDays = (byte)pkm.PokerusDays,
            FatefulEncounter = pkm.FatefulEncounter,
            HeightScalar = GetHeightScalarSafe(pkm),
            WeightScalar = GetWeightScalarSafe(pkm),
            Scale = GetScaleSafe(pkm),

            // ── Stats Tab ────────────────────────────
            BaseStats = baseStats,
            IVs = new[] { pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPA, pkm.IV_SPD, pkm.IV_SPE },
            EVs = new[] { pkm.EV_HP, pkm.EV_ATK, pkm.EV_DEF, pkm.EV_SPA, pkm.EV_SPD, pkm.EV_SPE },
            CalculatedStats = calculatedStats,
            HiddenPowerType = hiddenPowerType,
            AVs = pkm is IAwakened av ? new[] { (int)av.AV_HP, (int)av.AV_ATK, (int)av.AV_DEF, (int)av.AV_SPA, (int)av.AV_SPD, (int)av.AV_SPE } : null,
            GVs = pkm is IGanbaru gv ? new[] { (int)gv.GV_HP, (int)gv.GV_ATK, (int)gv.GV_DEF, (int)gv.GV_SPA, (int)gv.GV_SPD, (int)gv.GV_SPE } : null,
            DynamaxLevel = (pkm as IDynamaxLevel)?.DynamaxLevel,
            CanGigantamax = (pkm as IGigantamax)?.CanGigantamax ?? false,
            TeraTypeOriginal = (int?)(pkm as ITeraType)?.TeraTypeOriginal,
            TeraTypeOverride = (int?)(pkm as ITeraType)?.TeraTypeOverride,
            IsAlpha = (pkm as IAlpha)?.IsAlpha ?? false,
            IsNoble = (pkm as INoble)?.IsNoble ?? false,
            StatNature = GetStatNatureSafe(pkm),

            // ── Moves Tab ────────────────────────────
            Moves = moveDetails,
            MovePPs = new[] { pkm.Move1_PP, pkm.Move2_PP, pkm.Move3_PP, pkm.Move4_PP },
            MovePPUps = new[] { pkm.Move1_PPUps, pkm.Move2_PPUps, pkm.Move3_PPUps, pkm.Move4_PPUps },
            RelearnMoves = relearnMoves,
            RelearnMoveNames = relearnMoveNames,

            // ── Met Tab ──────────────────────────────
            PID = pkm.PID,
            EC = pkm.EncryptionConstant,
            MetLocation = pkm.MetLocation,
            MetLocationName = strings.GetLocationName(false, pkm.MetLocation, pkm.Format, (byte)pkm.Generation, pkm.Version),
            MetLevel = pkm.MetLevel,
            MetDate = pkm.MetDate?.ToString("yyyy-MM-dd"),
            OriginGame = (int)pkm.Version,
            OriginGameName = GameInfo.GetVersionName(pkm.Version),
            EggLocation = pkm.EggLocation,
            // EggLocationName is not mapped separately for now
            EggDate = pkm.EggMetDate?.ToString("yyyy-MM-dd"),
            MetTimeOfDay = GetMetTimeOfDaySafe(pkm),
            GroundTile = (int?)(pkm as IGroundTile)?.GroundTile,
            BattleVersion = (int?)(pkm as IBattleVersion)?.BattleVersion,
            ObedienceLevel = GetObedienceLevelSafe(pkm),

            // ── OT/Misc Tab ──────────────────────────
            TID = pkm.TID16,
            SID = pkm.SID16,
            OriginalTrainerName = pkm.OriginalTrainerName,
            OriginalTrainerGender = pkm.OriginalTrainerGender,
            HandlingTrainerName = GetHandlingTrainerNameSafe(pkm),
            HandlingTrainerGender = GetHandlingTrainerGenderSafe(pkm),
            HandlingTrainerLanguage = GetHandlerLanguageSafe(pkm),
            Affection = GetAffectionSafe(pkm),
            HomeTracker = GetHomeTrackerSafe(pkm),
            IsFavorite = (pkm as IFavorite)?.IsFavorite ?? false,
            GeoCountry = geoCountry,
            GeoRegion = geoRegion,
            Country = (pkm as IRegionOrigin)?.Country,
            CountryName = GetCountryName((pkm as IRegionOrigin)?.Country),
            SubRegion = (pkm as IRegionOrigin)?.Region,
            SubRegionName = GetSubRegionName((pkm as IRegionOrigin)?.Country, (pkm as IRegionOrigin)?.Region),
            ConsoleRegion = (pkm as IRegionOrigin)?.ConsoleRegion,
            ConsoleRegionName = GetConsoleRegionName((pkm as IRegionOrigin)?.ConsoleRegion),
            AffixedRibbon = (int?)(pkm as IRibbonSetAffixed)?.AffixedRibbon,

            // ── Cosmetic Tab ─────────────────────────
            Markings = GetMarkingsSafe(pkm),
            ContestCool = (pkm as IContestStats)?.ContestCool ?? 0,
            ContestBeauty = (pkm as IContestStats)?.ContestBeauty ?? 0,
            ContestCute = (pkm as IContestStats)?.ContestCute ?? 0,
            ContestSmart = (pkm as IContestStats)?.ContestSmart ?? 0,
            ContestTough = (pkm as IContestStats)?.ContestTough ?? 0,
            ContestSheen = (pkm as IContestStats)?.ContestSheen ?? 0,
            OriginMark = GetOriginMark(pkm),

            // ── Gen-Specific Tab ─────────────────────
            // Gen3 Colosseum/XD Shadow
            ShadowID = (pkm as IShadowCapture)?.ShadowID,
            Purification = (pkm as IShadowCapture)?.Purification,
            IsShadow = (pkm as IShadowCapture)?.IsShadow ?? false,

            // Gen4 HGSS Shiny Leaves (raw bitfield)
            ShinyLeaf = (pkm as G4PKM)?.ShinyLeaf,

            // Gen5 NSparkle / PokeStar (PK5 class-only props)
            NSparkle = (pkm as PK5)?.NSparkle,
            PokeStarFame = (pkm as PK5)?.PokeStarFame,
            IsPokeStar = (pkm as PK5)?.IsPokeStar ?? false,

            // Gen6-7 Super Training
            SuperTrainingEnabled = pkm is ISuperTrain,
            SecretSuperTrainingUnlocked = (pkm as ISuperTrain)?.SecretSuperTrainingUnlocked,
            SuperTrainSupremelyTrained = (pkm as ISuperTrain)?.SuperTrainSupremelyTrained ?? false,
            SuperTrainRegimenFlags = pkm is ISuperTrainRegimen str
                ? Enumerable.Range(0, 30).Select(i => str.GetRegimenState(i)).ToArray()
                : null,
            DistSuperTrainFlags = pkm is ISuperTrainRegimen str2
                ? Enumerable.Range(0, 6).Select(i => str2.GetRegimenStateDistribution(i)).ToArray()
                : null,

            // Gen6-7 Amie Fullness/Enjoyment
            Fullness = (pkm as IFullnessEnjoyment)?.Fullness,
            Enjoyment = (pkm as IFullnessEnjoyment)?.Enjoyment,

            // Gen7 Hyper Training
            HyperTrainingEnabled = pkm is IHyperTrain,
            HyperTrainFlags = pkm is IHyperTrain ht7
                ? new[] { ht7.HT_HP, ht7.HT_ATK, ht7.HT_DEF, ht7.HT_SPA, ht7.HT_SPD, ht7.HT_SPE }
                : null,

            // Gen7 LGPE (PB7 class-only props)
            CombatPower = (pkm as ICombatPower)?.Stat_CP,
            Spirit = (pkm as PB7)?.Spirit,
            Mood = (pkm as PB7)?.Mood,

            // ── General ──────────────────────────────
            Format = pkm.Format,
            IsValid = true,
            PkmDataBase64 = GetPkmBase64(pkm)
        };
    }

    // ── 语言名称映射 ──────────────────────────────
    private static string GetLanguageName(int langId) => langId switch
    {
        1 => "日本語", 2 => "English", 3 => "Français", 4 => "Italiano",
        5 => "Deutsch", 7 => "Español", 8 => "한국어",
        9 => "简体中文", 10 => "繁體中文",
        _ => $"Language {langId}"
    };

    // ── 国家/地区名称映射 ──────────────────────────
    private static string? GetCountryName(byte? countryCode)
    {
        if (countryCode == null) return null;
        return countryCode.Value switch
        {
            1 => "日本",
            8 => "安圭拉",
            9 => "安提瓜和巴布达",
            10 => "阿根廷",
            11 => "阿鲁巴",
            12 => "巴哈马",
            13 => "巴巴多斯",
            14 => "伯利兹",
            15 => "玻利维亚",
            16 => "巴西",
            17 => "英属维尔京群岛",
            18 => "加拿大",
            20 => "开曼群岛",
            21 => "智利",
            22 => "哥伦比亚",
            23 => "哥斯达黎加",
            24 => "多米尼克",
            25 => "多米尼加",
            26 => "厄瓜多尔",
            27 => "萨尔瓦多",
            28 => "法属圭亚那",
            29 => "格林纳达",
            30 => "瓜德罗普",
            31 => "危地马拉",
            32 => "圭亚那",
            33 => "海地",
            34 => "洪都拉斯",
            35 => "牙买加",
            36 => "马提尼克",
            37 => "墨西哥",
            38 => "蒙特塞拉特",
            39 => "荷属安的列斯",
            40 => "尼加拉瓜",
            41 => "巴拿马",
            42 => "巴拉圭",
            43 => "秘鲁",
            44 => "圣基茨和尼维斯",
            45 => "圣卢西亚",
            46 => "圣文森特和格林纳丁斯",
            47 => "苏里南",
            48 => "特立尼达和多巴哥",
            49 => "特克斯和凯科斯群岛",
            50 => "美国",
            51 => "乌拉圭",
            52 => "美属维尔京群岛",
            53 => "委内瑞拉",
            64 => "阿尔巴尼亚",
            65 => "澳大利亚",
            66 => "奥地利",
            67 => "比利时",
            68 => "波黑",
            69 => "博茨瓦纳",
            70 => "保加利亚",
            71 => "克罗地亚",
            72 => "塞浦路斯",
            73 => "捷克",
            74 => "丹麦",
            75 => "爱沙尼亚",
            76 => "芬兰",
            77 => "法国",
            78 => "德国",
            79 => "希腊",
            80 => "匈牙利",
            81 => "冰岛",
            82 => "爱尔兰",
            83 => "意大利",
            84 => "拉脱维亚",
            85 => "列支敦士登",
            86 => "立陶宛",
            87 => "卢森堡",
            88 => "马其顿",
            89 => "马耳他",
            90 => "黑山",
            91 => "莫桑比克",
            92 => "纳米比亚",
            93 => "荷兰",
            94 => "新西兰",
            95 => "挪威",
            96 => "波兰",
            97 => "葡萄牙",
            98 => "罗马尼亚",
            99 => "俄罗斯",
            100 => "塞尔维亚",
            101 => "斯洛伐克",
            102 => "斯洛文尼亚",
            103 => "南非",
            104 => "西班牙",
            105 => "瑞典",
            106 => "瑞士",
            107 => "土耳其",
            108 => "英国",
            109 => "赞比亚",
            110 => "津巴布韦",
            128 => "台湾",
            136 => "韩国",
            144 => "中国",
            160 => "香港",
            168 => "澳门",
            _ => $"[{countryCode}]"
        };
    }

    private static string? GetSubRegionName(byte? countryCode, byte? regionCode)
    {
        if (regionCode == null) return null;
        if (regionCode == 0) return "—";

        // 日本 (country=1) 的都道府县
        if (countryCode == 1)
        {
            return regionCode.Value switch
            {
                1 => "北海道",
                2 => "青森/東北",
                3 => "関東",
                4 => "中部",
                5 => "近畿",
                6 => "中国",
                7 => "四国",
                8 => "九州",
                9 => "沖縄",
                _ => $"日本[{regionCode}]"
            };
        }

        return $"[{regionCode}]";
    }

    private static string? GetConsoleRegionName(byte? consoleRegion)
    {
        if (consoleRegion == null) return null;
        return consoleRegion.Value switch
        {
            0 => "日本 (JPN)",
            1 => "美洲 (USA)",
            2 => "欧洲 (EUR)",
            3 => "澳洲 (AUS)",
            4 => "中国 (CHN)",
            5 => "韩国 (KOR)",
            6 => "台湾 (TWN)",
            _ => $"[{consoleRegion}]"
        };
    }

    // ── 来源标记 ──────────────────────────────────
    private static int? GetOriginMark(PKM pkm)
    {
        try
        {
            var mark = PKHeX.Core.OriginMarkUtil.GetOriginMark(pkm);
            return (int)mark;
        }
        catch { return null; }
    }

    // ── 手动能力值计算（当 PKHeX.Core 内置计算不可用时）──
    private static void CalculateStatsManually(PKM pkm, int[] stats)
    {
        try
        {
            var pi = pkm.PersonalInfo;
            if (pi == null)
            {
                // Fallback: use stored stats
                stats[0] = pkm.Stat_HPMax;
                stats[1] = pkm.Stat_ATK;
                stats[2] = pkm.Stat_DEF;
                stats[3] = pkm.Stat_SPA;
                stats[4] = pkm.Stat_SPD;
                stats[5] = pkm.Stat_SPE;
                return;
            }

            int level = pkm.CurrentLevel;
            var nature = (int)pkm.Nature;
            // Base stats from PersonalInfo
            int[] bases = [pi.HP, pi.ATK, pi.DEF, pi.SPE, pi.SPA, pi.SPD];
            int[] ivs = [pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPE, pkm.IV_SPA, pkm.IV_SPD];
            int[] evs = [pkm.EV_HP, pkm.EV_ATK, pkm.EV_DEF, pkm.EV_SPE, pkm.EV_SPA, pkm.EV_SPD];

            // HP formula
            stats[0] = (int)((2 * bases[0] + ivs[0] + evs[0] / 4) * level / 100 + level + 10);

            // Other stats formula
            for (int i = 1; i < 6; i++)
            {
                double baseVal = (2 * bases[i] + ivs[i] + evs[i] / 4) * level / 100.0 + 5;
                // Apply nature modifier
                double natureMod = 1.0;
                if (nature >= 0)
                {
                    int up = nature / 5;
                    int down = nature % 5;
                    if (up == down) { natureMod = 1.0; }
                    else if (i - 1 == up) natureMod = 1.1;
                    else if (i - 1 == down) natureMod = 0.9;
                }
                stats[i] = (int)(baseVal * natureMod);
            }

            // Reorder: SPE is position 3 in PKHeX internal, we want it at position 5
            // Our output order: HP, ATK, DEF, SPA, SPD, SPE
            // Current order:  HP, ATK, DEF, SPE, SPA, SPD
            var spe = stats[3];
            stats[3] = stats[4];  // SPA
            stats[4] = stats[5];  // SPD
            stats[5] = spe;       // SPE
        }
        catch
        {
            stats[0] = pkm.Stat_HPMax;
            stats[1] = pkm.Stat_ATK;
            stats[2] = pkm.Stat_DEF;
            stats[3] = pkm.Stat_SPA;
            stats[4] = pkm.Stat_SPD;
            stats[5] = pkm.Stat_SPE;
        }
    }

    // ── 安全属性访问器（属性仅在特定世代/类型上存在）──

    private static byte GetHeightScalarSafe(PKM pkm)
    {
        // HeightScalar is available on specific types (PA8, PA9) via reflection
        var prop = pkm.GetType().GetProperty("HeightScalar");
        return prop != null ? Convert.ToByte(prop.GetValue(pkm)) : (byte)0;
    }

    private static byte GetWeightScalarSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("WeightScalar");
        return prop != null ? Convert.ToByte(prop.GetValue(pkm)) : (byte)0;
    }

    private static byte GetFormArgumentSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("FormArgument");
        return prop != null ? Convert.ToByte(prop.GetValue(pkm)) : (byte)0;
    }

    private static byte GetScaleSafe(PKM pkm)
    {
        // Scale is on specific types (PA8/PA9/ISCAL_SIZE)
        var prop = pkm.GetType().GetProperty("Scale");
        return prop != null ? Convert.ToByte(prop.GetValue(pkm)) : (byte)0;
    }

    private static byte GetStatNatureSafe(PKM pkm)
    {
        // StatNature on Gen8+ types
        var prop = pkm.GetType().GetProperty("StatNature");
        return prop != null ? Convert.ToByte(prop.GetValue(pkm)) : (byte)0;
    }

    private static int? GetMetTimeOfDaySafe(PKM pkm)
    {
        // MetTimeOfDay is available with Gen2-specific types
        // Use reflection to check
        var prop = pkm.GetType().GetProperty("MetTimeOfDay");
        return prop != null ? (int?)Convert.ToInt32(prop.GetValue(pkm)) : null;
    }

    private static byte? GetObedienceLevelSafe(PKM pkm)
    {
        // ObedienceLevel for Gen9+
        var prop = pkm.GetType().GetProperty("ObedienceLevel");
        return prop != null ? (byte?)Convert.ToByte(prop.GetValue(pkm)) : null;
    }

    private static string? GetHandlingTrainerNameSafe(PKM pkm)
    {
        // Try IHandlingTrainerName or IHandlerLanguage
        var prop = pkm.GetType().GetProperty("HandlingTrainerName");
        return prop != null ? prop.GetValue(pkm) as string : null;
    }

    private static int GetHandlingTrainerGenderSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("HandlingTrainerGender");
        return prop != null ? Convert.ToInt32(prop.GetValue(pkm)) : 0;
    }

    private static int GetHandlerLanguageSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("HandlerLanguage");
        return prop != null ? Convert.ToInt32(prop.GetValue(pkm)) : 0;
    }

    private static int? GetAffectionSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("Affection");
        return prop != null ? (int?)Convert.ToInt32(prop.GetValue(pkm)) : null;
    }

    private static string? GetHomeTrackerSafe(PKM pkm)
    {
        var prop = pkm.GetType().GetProperty("Tracker");
        if (prop != null)
        {
            var val = prop.GetValue(pkm);
            return val is ulong ul ? ul.ToString("X16") : val?.ToString();
        }
        return null;
    }

    private static int[] GetMarkingsSafe(PKM pkm)
    {
        var markings = new int[6];
        for (int i = 0; i < 6; i++)
        {
            try
            {
                // Marking is available via IAppliedMarkings or similar
                var prop = pkm.GetType().GetProperty("Marking");
                if (prop != null)
                {
                    var val = prop.GetValue(pkm);
                    if (val is byte b) markings[i] = b;
                    // For individual markings, try indexed approach
                }
                // Fallback: try GetMarking via reflection
                var method = pkm.GetType().GetMethod("GetMarking");
                if (method != null)
                    markings[i] = Convert.ToInt32(method.Invoke(pkm, new object[] { i }));
            }
            catch { markings[i] = 0; }
        }
        return markings;
    }

    // ── 辅助方法 ────────────────────────────────────────

    /// <summary>PKM → Base64（v26 中 DecryptedPartyData 已移除，改用 WriteDecryptedDataParty）</summary>
    private static string GetPkmBase64(PKM pkm)
    {
        var buffer = new byte[pkm.SIZE_PARTY];
        pkm.WriteDecryptedDataParty(buffer);
        return Convert.ToBase64String(buffer);
    }

    private static string GetSafeString(IReadOnlyList<string> list, int index, string fallback)
    {
        if (index >= 0 && index < list.Count)
            return list[index];
        return fallback;
    }
}
