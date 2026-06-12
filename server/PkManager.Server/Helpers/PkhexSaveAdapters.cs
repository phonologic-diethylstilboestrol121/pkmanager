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
}
