using PKHeX.Core;

namespace PkManager.Server.Helpers;

/// <summary>
/// PKHeX 存档字段的强类型兼容适配层。
/// 将分散在不同 SAV 子类上的异构属性（Badges/BP/Coin/LeaguePoints/GameSync）
/// 收敛到一处，避免反射调用扩散到业务代码。
///
/// 升级 PKHeX 时只需在这里补分支。
/// </summary>
internal static class PkhexSaveAdapters
{
    // ═══ Badges ═══════════════════════════════════════════

    public static int? GetBadges(SaveFile sav) => sav switch
    {
        SAV6 s6 => s6.Badges,
        SAV8SWSH s8 => s8.Badges,
        SAV4 s4 => s4.Badges,
        SAV3 s3 => s3.Badges,
        SAV2 s2 => s2.Badges,
        SAV1 s1 => s1.Badges,
        _ => null,
    };

    public static void SetBadges(SaveFile sav, int value)
    {
        switch (sav)
        {
            case SAV6 s6: s6.Badges = value; break;
            case SAV8SWSH s8: s8.Badges = value; break;
            case SAV4 s4: s4.Badges = (byte)value; break;
            case SAV3 s3: s3.Badges = value; break;
            case SAV2 s2: s2.Badges = value; break;
            case SAV1 s1: s1.Badges = value; break;
        }
    }

    // ═══ BP ═══════════════════════════════════════════════

    public static int? GetBP(SaveFile sav) => sav switch
    {
        SAV6 s6 => s6.BP,
        SAV7 s7 => (int)s7.Misc.BP,
        SAV8SWSH s8 => s8.Misc.BP,
        SAV4 s4 => s4.BP,
        _ => null,
    };

    public static void SetBP(SaveFile sav, int value)
    {
        switch (sav)
        {
            case SAV6 s6: s6.BP = value; break;
            case SAV7 s7: s7.Misc.BP = (uint)value; break;
            case SAV8SWSH s8: s8.Misc.BP = value; break;
            case SAV4 s4: s4.BP = value; break;
        }
    }

    // ═══ Coin（v26: 属性名从 Coins 改为 Coin，类型 uint）══

    public static int? GetCoin(SaveFile sav) => sav switch
    {
        SAV4 s4 => (int)s4.Coin,
        SAV3 s3 => (int)s3.Coin,
        SAV2 s2 => (int)s2.Coin,
        SAV1 s1 => (int)s1.Coin,
        _ => null,
    };

    public static void SetCoin(SaveFile sav, int value)
    {
        var v = (uint)Math.Max(0, value);
        switch (sav)
        {
            case SAV4 s4: s4.Coin = v; break;
            case SAV3 s3: s3.Coin = v; break;
            case SAV2 s2: s2.Coin = v; break;
            case SAV1 s1: s1.Coin = v; break;
        }
    }

    // ═══ LeaguePoints ═════════════════════════════════════

    public static uint? GetLeaguePoints(SaveFile sav) => sav switch
    {
        SAV9SV s9 => s9.LeaguePoints,
        _ => null,
    };

    public static void SetLeaguePoints(SaveFile sav, uint value)
    {
        if (sav is SAV9SV s9)
            s9.LeaguePoints = value;
    }

    // ═══ GameSync ═════════════════════════════════════════

    public static string? GetGameSyncID(SaveFile sav) =>
        (sav as IGameSync)?.GameSyncID;

    // ═══ Trainer Card ═════════════════════════════════════

    public static string? GetCardNumber(SaveFile sav) =>
        (sav as SAV8SWSH)?.TrainerCard.Number;

    public static void SetCardNumber(SaveFile sav, string value)
    {
        if (sav is SAV8SWSH swsh)
            swsh.TrainerCard.Number = value.Length > 3 ? value[..3] : value;
    }

    // ═══ Gen3 RTC ════════════════════════════════════════

    /// <summary>
    /// 获取 Gen3 Hoenn 存档的 RTC3 时钟对。
    /// 仅在 RS/Emerald 上返回非 null（FRLG 的 SmallBlock 不实现 ISaveBlock3SmallHoenn）。
    /// </summary>
    public static (RTC3? initial, RTC3? elapsed) GetRTC3(SaveFile sav)
    {
        if (sav is SAV3 { SmallBlock: ISaveBlock3SmallHoenn hoenn })
            return (hoenn.ClockInitial, hoenn.ClockElapsed);
        return (null, null);
    }

    // ═══ Gen6 O-Power ═════════════════════════════════════

    /// <summary>
    /// 获取 Gen6 (XY/ORAS) O-Power 数据。非 Gen6 存档返回 null。
    /// </summary>
    public static OPower6? GetOPower(SaveFile sav) => sav switch
    {
        SAV6XY xy => xy.OPower,
        SAV6AO ao => ao.OPower,
        _ => null,
    };

    // ═══ Gen5 Dream World / Entralink / C-Gear ══════════

    public static (int totalSlots, int occupiedSlots, bool unlock9thArea, int unlock38Areas, List<(int index, int species, int move, int gender, int form, bool invisible, int area)> slots)? GetEntreeForest(SaveFile sav)
    {
        if (sav is not SAV5BW and not SAV5B2W2)
            return null;

        var forest = sav switch
        {
            SAV5BW bw => bw.EntreeForest,
            SAV5B2W2 b2w2 => b2w2.EntreeForest,
            _ => null,
        };
        if (forest == null)
            return null;

        forest.StartAccess();
        try
        {
            var slots = new List<(int index, int species, int move, int gender, int form, bool invisible, int area)>(forest.Slots.Length);
            int occupied = 0;
            for (int i = 0; i < forest.Slots.Length; i++)
            {
                var slot = forest.Slots[i];
                if (slot.Species != 0)
                    occupied++;
                slots.Add((i, slot.Species, slot.Move, slot.Gender, slot.Form, slot.Invisible, (int)slot.Area));
            }

            return (forest.Slots.Length, occupied, forest.Unlock9thArea, forest.Unlock38Areas, slots);
        }
        finally
        {
            forest.EndAccess();
        }
    }

    public static (int whiteForestLevel, int blackCityLevel, int? missionsComplete, int? passPower1, int? passPower2, int? passPower3)? GetEntralink(SaveFile sav) => sav switch
    {
        SAV5BW bw => (bw.Entralink.WhiteForestLevel, bw.Entralink.BlackCityLevel, bw.Entralink.MissionsComplete, null, null, null),
        SAV5B2W2 b2w2 when b2w2.Entralink is Entralink5B2W2 entralink => (entralink.WhiteForestLevel, entralink.BlackCityLevel, null, entralink.PassPower1, entralink.PassPower2, entralink.PassPower3),
        _ => null,
    };

    public static (bool hasCGearSkin, int checksum, int dataSize)? GetSkinInfo(SaveFile sav) => sav switch
    {
        SAV5BW bw => (bw.SkinInfo.HasCGearSkin, bw.SkinInfo.CGearSkinChecksum, bw.CGearSkinData.Length),
        SAV5B2W2 b2w2 => (b2w2.SkinInfo.HasCGearSkin, b2w2.SkinInfo.CGearSkinChecksum, b2w2.CGearSkinData.Length),
        _ => null,
    };

    // ═══ Gen7 Zygarde Cell ════════════════════════════════

    /// <summary>
    /// 获取 Gen7 (SM/USUM) EventWork7 数据（含 Zygarde Cell 收集进度）。
    /// SAV7b (Let's Go) 不在此分支，其 EventWork 不含 Zygarde 数据。
    /// </summary>
    public static EventWork7? GetEventWork7(SaveFile sav) => sav switch
    {
        SAV7SM sm => sm.EventWork,
        SAV7USUM usum => usum.EventWork,
        _ => null,
    };

    // ═══ Gen6 Holo Caster ═════════════════════════════════

    private const int HoloCasterOffsetXY = 0x15800;
    private const int HoloCasterOffsetAO = 0x16200;
    private const int HoloCasterSize = 0x644;

    /// <summary>
    /// 检测 Gen6 存档中 Holo Caster 数据区域是否存在非零内容。
    /// </summary>
    public static bool HasHoloCasterData(SaveFile sav)
    {
        int offset = sav switch
        {
            SAV6XY => HoloCasterOffsetXY,
            SAV6AO => HoloCasterOffsetAO,
            _ => -1,
        };
        if (offset < 0) return false;

        var span = sav.Data.Slice(offset, HoloCasterSize);
        // 检查是否非全零（至少有一个字节非零即认为已写入过内容）
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != 0) return true;
        }
        return false;
    }

    // ═══ Gen7 Festa ════════════════════════════════════════

    /// <summary>
    /// 获取 Gen7 (SM/USUM) Festival Plaza 数据。非 Gen7 存档返回 null。
    /// </summary>
    public static (int coins, int totalCoins, int rank)? GetFesta(SaveFile sav)
    {
        if (sav is not SAV7 s7) return null;
        var f = s7.Festa;
        return (f.FestaCoins, f.TotalFestaCoins, f.FestaRank);
    }

    // ═══ Gen7 Pelago ═══════════════════════════════════════

    /// <summary>
    /// 获取 Gen7 (SM/USUM) Poké Pelago 数据。非 Gen7 存档返回 null。
    /// </summary>
    public static (int occupied, int total, int[] beans, int visits, int eggs, int hunts)? GetPelago(SaveFile sav)
    {
        if (sav is not SAV7 s7) return null;
        var r = s7.ResortSave;

        // 统计已占用的槽位（非全零 PK7 数据）
        int occupied = 0;
        int total = ResortSave7.ResortCount;
        for (int i = 0; i < total; i++)
        {
            var slot = r[i].Span;
            bool hasData = false;
            for (int j = 0; j < slot.Length; j++)
            {
                if (slot[j] != 0) { hasData = true; break; }
            }
            if (hasData) occupied++;
        }

        // 豆子统计
        var beanSpan = r.GetBeans();
        int[] beans = new int[beanSpan.Length];
        for (int i = 0; i < beanSpan.Length; i++)
            beans[i] = beanSpan[i];

        int visits = (int)s7.GetRecord(054);
        int eggs = (int)s7.GetRecord(060);
        int hunts = (int)s7.GetRecord(160);

        return (occupied, total, beans, visits, eggs, hunts);
    }

    // ═══ Gen7 Totem Stamps ═════════════════════════════════

    /// <summary>
    /// 获取 Gen7 (SM/USUM) 贴纸收集数与护照印章。非 Gen7 存档返回 null。
    /// </summary>
    public static (int stickers, uint stamps)? GetTotemStamps(SaveFile sav)
    {
        if (sav is not SAV7 s7) return null;
        int stickers = (int)s7.GetRecord(072);
        uint stamps = s7.Misc.Stamps;
        return (stickers, stamps);
    }

    // ═══ Gen7 Rotom Dex ════════════════════════════════════

    /// <summary>
    /// 获取 Gen7 USUM 洛托姆图鉴数据。非 USUM 存档返回 null。
    /// </summary>
    public static (int affection, bool loto1, bool loto2, string? nickname)? GetRotomDex(SaveFile sav)
    {
        if (sav is not SAV7USUM usum) return null;
        var fm = usum.FieldMenu;
        return (fm.RotomAffection, fm.RotomLoto1, fm.RotomLoto2, fm.RotomOT);
    }
}
