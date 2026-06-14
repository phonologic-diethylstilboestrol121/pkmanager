using System.Text.Json;
using PKHeX.Core;
using PkManager.Server.Models.Request;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Services;

/// <summary>
/// 一键进化服务 — 查询进化路径、执行进化。
/// PKHeX.Core EvolutionTree 是只读分析系统，进化需手动设置 pk.Species / pk.Form。
/// </summary>
public class EvolutionService
{
    private readonly PokemonEditService _editService;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public EvolutionService(PokemonEditService editService)
    {
        _editService = editService;
    }

    /// <summary>
    /// 将前端 editSnapshot (camelCase Dictionary) 应用到 PKM。
    /// editSnapshot 的 key 与 PokemonEditRequest 属性名对应（小驼峰 vs PascalCase）。
    /// </summary>
    private void ApplyEditSnapshot(PKM pkm, Dictionary<string, object?>? editSnapshot)
    {
        if (editSnapshot == null || editSnapshot.Count == 0) return;

        // System.Text.Json 默认将 Dictionary<string,object?> 序列化为 JSON object。
        // editSnapshot 的 key 已是 camelCase（来自前端 buildEditRequest 输出）。
        var json = JsonSerializer.Serialize(editSnapshot);
        var request = JsonSerializer.Deserialize<PokemonEditRequest>(json, _jsonOptions);
        if (request != null)
            _editService.ApplyEditsToPkm(pkm, request);
    }

    /// <summary>
    /// 获取当前宝可梦的进化路径（含 TryEvolve 可用性判定）。
    /// </summary>
    public EvolutionPathDto GetEvolutionPaths(PKM pkm, Dictionary<string, object?>? editSnapshot)
    {
        // 1. 应用当前未保存编辑
        ApplyEditSnapshot(pkm, editSnapshot);

        // 2. 获取当前世代的进化树
        var tree = EvolutionTree.GetEvolutionTree(pkm.Context);

        // 3. 获取所有可能的进化方法
        var methods = tree.Forward.GetForward(pkm.Species, pkm.Form);
        if (methods.IsEmpty)
            return new EvolutionPathDto { HasAnyEvolution = false };

        var strings = GameInfo.GetStrings("zh");
        var groupedMethods = new Dictionary<(ushort Species, byte Form), List<EvolutionMethod>>();
        var options = new List<EvolutionOptionDto>();

        foreach (var method in methods.Span)
        {
            var targetSpecies = method.Species;
            var targetForm = method.GetDestinationForm(pkm.Form);
            var key = (targetSpecies, targetForm);
            if (!groupedMethods.TryGetValue(key, out var list))
            {
                list = new List<EvolutionMethod>();
                groupedMethods[key] = list;
            }
            list.Add(method);
        }

        foreach (var ((targetSpecies, targetForm), methodGroup) in groupedMethods)
        {
            var displayMethod = SelectPreferredMethod(methodGroup, pkm, out var displayResult);

            var destForm = targetForm != 0 && targetForm != byte.MaxValue ? targetForm : (byte)0;
            string formName = destForm > 0 ? $"形态{destForm}" : "";

            var target = (ISpeciesForm)new EvoTarget(targetSpecies, destForm);
            bool isAvailable = tree.Forward.TryEvolve(
                pkm, target, pkm,
                pkm.CurrentLevel, pkm.MetLevel, skipChecks: false,
                EvolutionRuleTweak.Default, out _);

            if (isAvailable)
            {
                foreach (var method in methodGroup)
                {
                    var result = GetCheckResult(method, pkm);
                    if (result == EvolutionCheckResult.Valid)
                    {
                        displayMethod = method;
                        displayResult = result;
                        break;
                    }
                }
            }

            string? blockReason = isAvailable ? null : GetBlockReason(displayResult);

            options.Add(new EvolutionOptionDto
            {
                Species = targetSpecies,
                SpeciesName = strings.Species[targetSpecies],
                Form = destForm,
                FormName = formName,
                MethodLabel = GetChineseMethodLabel(displayMethod, strings),
                RequiredLevel = displayMethod.Level,
                Argument = displayMethod.Argument,
                IsAvailable = isAvailable,
                BlockReason = blockReason,
            });

        }

        return new EvolutionPathDto
        {
            HasAnyEvolution = options.Count > 0,
            HasBranchingPaths = options.Count > 1,
            IsNincada = pkm.Species == 290,
            Options = options,
        };
    }

    /// <summary>
    /// 执行进化：应用 editSnapshot → 强制补足可写回条件 → 公共状态同步 → 处理脱壳忍者 → 修改物种/形态 → 写回。
    /// 返回 EvolveResultDto（调用方负责 WriteBackSave 持久化）。
    /// </summary>
    public EvolveResultDto ExecuteEvolve(PKM pkm, PKHeX.Core.SaveFile sav, EvolveRequest request)
    {
        // 1. 应用当前未保存编辑
        ApplyEditSnapshot(pkm, request.EditSnapshot);

        // 2. 获取进化树并找到匹配的 EvolutionMethod
        var tree = EvolutionTree.GetEvolutionTree(pkm.Context);
        var matchedMethods = new List<EvolutionMethod>();
        foreach (var m in tree.Forward.GetForward(pkm.Species, pkm.Form).Span)
        {
            if (m.Species == request.TargetSpecies && m.GetDestinationForm(pkm.Form) == request.TargetForm)
                matchedMethods.Add(m);
        }
        if (matchedMethods.Count == 0)
            return new EvolveResultDto { Success = false, Error = "目标物种不在进化路径中" };

        var method = SelectPreferredMethod(matchedMethods, pkm, out _);

        // 3. 强制补齐可直接写回的进化条件
        ApplyForcedEvolutionConditions(pkm, method);

        // 4. 按宝可梦自身语言获取物种名（用于昵称同步）
        var langCode = GetLanguageCode(pkm.Language);
        var langStrings = GameInfo.GetStrings(langCode);

        // 5. 昵称同步：仅当未自定义昵称时，用对应语言的物种名更新
        if (!pkm.IsNicknamed)
        {
            var nameList = langStrings.Species;
            if (request.TargetSpecies < nameList.Count)
                pkm.Nickname = nameList[request.TargetSpecies];
        }

        // 6. Nincada → Shedinja 特例（在状态同步后克隆，共享 level/exp）
        EvolveResultDto? shedinjaResult = null;
        if (pkm.Species == 290 && request.TargetSpecies == 291 && request.AlsoCreateShedinja)
        {
            PKM shedinja;
            try
            {
                shedinja = pkm.Clone();
            }
            catch
            {
                var buf = new byte[pkm.SIZE_PARTY];
                pkm.WriteDecryptedDataParty(buf);
                shedinja = EntityFormat.GetFromBytes(buf)!;
                if (shedinja == null)
                    return new EvolveResultDto { Success = false, Error = "无法克隆宝可梦数据" };
            }

            shedinja.Species = 292;  // Shedinja
            shedinja.Form = 0;
            shedinja.IsNicknamed = false;
            // Shedinja 使用与 Nincada 相同的语言/物种名
            var nameList = langStrings.Species;
            if (292 < nameList.Count)
                shedinja.Nickname = nameList[292];

            // 找第一个空箱位
            (int boxIdx, int slotIdx)? emptySlot = null;
            for (int b = 0; b < sav.BoxCount; b++)
            {
                var boxData = sav.GetBoxData(b);
                for (int s = 0; s < boxData.Length; s++)
                {
                    if (boxData[s].Species == 0)
                    {
                        emptySlot = (b, s);
                        break;
                    }
                }
                if (emptySlot.HasValue) break;
            }

            if (!emptySlot.HasValue)
                return new EvolveResultDto { Success = false, Error = "无空位存放脱壳忍者" };

            var (sBox, sSlot) = emptySlot.Value;
            var compatShedinja = sav.GetCompatiblePKM(shedinja);
            var boxData2 = sav.GetBoxData(sBox);
            boxData2[sSlot] = compatShedinja;
            sav.SetBoxData(boxData2, sBox);

            shedinjaResult = new EvolveResultDto
            {
                Shedinja = ParseService.MapToPokemonDto(compatShedinja),
                ShedinjaLocation = $"箱子 {sBox + 1} 槽位 {sSlot + 1}",
            };
        }

        // 7. 执行进化
        pkm.Species = (ushort)request.TargetSpecies;
        if (method.Form != byte.MaxValue) // AnyForm → 保留当前形态
            pkm.Form = method.Form;

        // 8. 写回原始槽位，返回 compat（实际落盘对象）
        PKM compat;
        if (request.IsParty)
        {
            if (request.SlotIndex < 0 || request.SlotIndex >= 6)
                return new EvolveResultDto { Success = false, Error = "Party 槽位无效" };
            compat = sav.GetCompatiblePKM(pkm);
            sav.SetPartySlotAtIndex(compat, request.SlotIndex);
        }
        else
        {
            if (request.BoxIndex < 0 || request.BoxIndex >= sav.BoxCount)
                return new EvolveResultDto { Success = false, Error = "箱子索引无效" };
            var boxData = sav.GetBoxData(request.BoxIndex);
            if (request.SlotIndex < 0 || request.SlotIndex >= boxData.Length)
                return new EvolveResultDto { Success = false, Error = "箱子槽位无效" };
            compat = sav.GetCompatiblePKM(pkm);
            boxData[request.SlotIndex] = compat;
            sav.SetBoxData(boxData, request.BoxIndex);
        }

        // 9. 返回 compat（实际落盘对象），与现有保存链路一致
        return new EvolveResultDto
        {
            Success = true,
            EvolvedPokemon = ParseService.MapToPokemonDto(compat),
            Shedinja = shedinjaResult?.Shedinja,
            ShedinjaLocation = shedinjaResult?.ShedinjaLocation,
        };
    }

    /// <summary>
    /// PKHeX 语言 ID → ISO 语言代码映射。
    /// </summary>
    private static string GetLanguageCode(int langId) => langId switch
    {
        1 => "ja",
        2 => "en",
        3 => "fr",
        4 => "it",
        5 => "de",
        7 => "es",
        8 => "ko",
        9 => "zh",       // 简体中文
        10 => "zh-Hant", // 繁體中文
        _ => "en",       // 回退
    };

    /// <summary>
    /// 将 EvolutionCheckResult 映射为中文阻塞原因。
    /// </summary>
    private static EvolutionCheckResult GetCheckResult(EvolutionMethod method, PKM pkm) =>
        method.Check(pkm, (byte)pkm.CurrentLevel, pkm.MetLevel,
            skipChecks: false, EvolutionRuleTweak.Default);

    private static EvolutionMethod SelectPreferredMethod(
        IReadOnlyList<EvolutionMethod> methods,
        PKM pkm,
        out EvolutionCheckResult result)
    {
        foreach (var method in methods)
        {
            var check = GetCheckResult(method, pkm);
            if (check == EvolutionCheckResult.Valid)
            {
                result = check;
                return method;
            }
        }

        var fallback = methods.OrderBy(m => m.Level).First();
        result = GetCheckResult(fallback, pkm);
        return fallback;
    }

    /// <summary>
    /// 强制补足可直接写回的进化条件。
    /// 目标是尽量让当前 PKM 满足目标进化方式，而不是严格模拟游戏内交互。
    /// </summary>
    private void ApplyForcedEvolutionConditions(PKM pkm, EvolutionMethod method)
    {
        if (method.Level > 0 && pkm.CurrentLevel < method.Level)
            pkm.CurrentLevel = method.Level;

        switch (method.Method)
        {
            case EvolutionType.LevelUpMale:
                pkm.Gender = 0;
                break;

            case EvolutionType.LevelUpFemale:
            case EvolutionType.LevelUpFormFemale1:
                pkm.Gender = 1;
                break;

            case EvolutionType.LevelUpFriendship:
            case EvolutionType.LevelUpFriendshipMorning:
            case EvolutionType.LevelUpFriendshipNight:
            case EvolutionType.LevelUpWithTeammate:
                pkm.OriginalTrainerFriendship = 255;
                break;

            case EvolutionType.LevelUpBeauty:
                if (pkm is IContestStats contest)
                    contest.ContestBeauty = 255;
                break;

            case EvolutionType.LevelUpAffection50MoveType:
                SetAffection(pkm, 255);
                pkm.OriginalTrainerFriendship = 255;
                if (TryGetMoveOfType(pkm, method.Argument, out var affectionMove))
                    pkm.SetMove(0, affectionMove);
                break;

            case EvolutionType.LevelUpKnowMove:
            case EvolutionType.LevelUpKnowMoveECElse:
            case EvolutionType.LevelUpKnowMoveEC100:
                if (method.Argument > 0)
                    pkm.SetMove(0, method.Argument);
                break;

            case EvolutionType.LevelUpMoveType:
                if (TryGetMoveOfType(pkm, method.Argument, out var moveId))
                    pkm.SetMove(0, moveId);
                break;

            case EvolutionType.LevelUpUseMoveSpecial:
                if (method.Argument > 0)
                    pkm.SetMove(0, method.Argument);
                break;

            case EvolutionType.Trade:
            case EvolutionType.TradeHeldItem:
            case EvolutionType.TradeShelmetKarrablast:
                ApplyTradedState(pkm);
                break;
        }
    }

    private static void ApplyTradedState(PKM pkm)
    {
        // 通讯进化在 PKHeX 中主要依赖“已交换”状态。
        // 这里补足处理者信息，避免进化后被判定为 Untraded。
        var handledName = pkm.OriginalTrainerName;
        if (string.IsNullOrWhiteSpace(handledName))
            handledName = "PKHeX";

        var nameProp = pkm.GetType().GetProperty("HandlingTrainerName");
        nameProp?.SetValue(pkm, handledName);

        var genderProp = pkm.GetType().GetProperty("HandlingTrainerGender");
        if (genderProp != null)
        {
            var gender = pkm.OriginalTrainerGender;
            if (gender < 0)
                gender = 0;
            genderProp.SetValue(pkm, gender);
        }

        var languageProp = pkm.GetType().GetProperty("HandlingTrainerLanguage");
        if (languageProp != null)
            languageProp.SetValue(pkm, pkm.Language);

        // 保险起见，补满处理者亲密度，避免后续展示或校验出现低值状态。
        try
        {
            pkm.HandlingTrainerFriendship = 255;
        }
        catch
        {
            // 某些类型/版本可能不支持，忽略即可。
        }
    }

    private static void SetAffection(PKM pkm, int value)
    {
        var prop = pkm.GetType().GetProperty("Affection");
        prop?.SetValue(pkm, (byte)Math.Clamp(value, 0, 255));
    }

    private static bool TryGetMoveOfType(PKM pkm, ushort typeId, out ushort moveId)
    {
        moveId = 0;
        var strings = GameInfo.GetStrings("zh");
        for (ushort id = 1; id < strings.Move.Count; id++)
        {
            try
            {
                if (MoveInfo.GetType(id, pkm.Context) == typeId)
                {
                    moveId = id;
                    return true;
                }
            }
            catch
            {
                // Ignore invalid IDs for this context and keep scanning.
            }
        }
        return false;
    }

    private static string GetBlockReason(EvolutionCheckResult result) =>
        result switch
        {
            EvolutionCheckResult.Valid => "未知原因",
            EvolutionCheckResult.InsufficientLevel => "等级不足",
            EvolutionCheckResult.BadGender => "性别不符",
            EvolutionCheckResult.BadForm => "形态不符",
            EvolutionCheckResult.WrongEC => "加密常数不匹配",
            EvolutionCheckResult.VisitVersion => "版本不匹配",
            EvolutionCheckResult.LowContestStat => "选美属性不足",
            EvolutionCheckResult.Untraded => "需要通讯交换",
            _ => $"不满足条件({result})",
        };

    /// <summary>
    /// 将 EvolutionMethod 映射为中文进化方式标签。
    /// 覆盖 Gen1-7 使用的 ~30 种进化类型，其余回退到通用标签。
    /// </summary>
    private static string GetChineseMethodLabel(EvolutionMethod m, GameStrings strings)
    {
        string ItemName(int id) =>
            id > 0 && id < strings.Item.Count ? strings.Item[id] : $"道具{id}";

        return m.Method switch
        {
            // ── 等级类 ──
            EvolutionType.LevelUp => m.Level > 1 ? $"等级{m.Level}以上" : "升级",
            EvolutionType.LevelUpFriendship => $"亲密度升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpFriendshipMorning => $"亲密度升级·白天 (Lv.{m.Level}+)",
            EvolutionType.LevelUpFriendshipNight => $"亲密度升级·夜晚 (Lv.{m.Level}+)",
            EvolutionType.LevelUpATK => $"攻击＞防御时升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpAeqD => $"攻击＝防御时升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpDEF => $"防御＞攻击时升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpECl5 => $"随机EC＜5升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpECgeq5 => $"随机EC≥5升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpMale => $"♂ 升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpFemale => $"♀ 升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpKnowMove => $"习得{m.Argument}后升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpBeauty => $"美丽度{m.Argument}+升级",
            EvolutionType.LevelUpVersion => $"特定版本升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpVersionDay => $"特定版本白天升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpVersionNight => $"特定版本夜晚升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpMorning => $"白天升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpNight => $"夜晚升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpDusk => $"黄昏升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpElectric => $"电气石洞穴升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpForest => $"森林升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpCold => $"寒冷地带升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpSummit => $"山顶升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpWormhole => $"究极之洞升级 (Lv.{m.Level}+)",

            // ── 道具类 ──
            EvolutionType.UseItem => $"使用{ItemName(m.Argument)}",
            EvolutionType.UseItemMale => $"♂ 使用{ItemName(m.Argument)}",
            EvolutionType.UseItemFemale => $"♀ 使用{ItemName(m.Argument)}",
            EvolutionType.UseItemWormhole => $"究极之洞使用{ItemName(m.Argument)}",
            EvolutionType.UseItemFullMoon => $"满月之夜使用{ItemName(m.Argument)}",

            // ── 通讯交换类 ──
            EvolutionType.Trade => "通讯交换",
            EvolutionType.TradeHeldItem => $"通讯交换 (携带{ItemName(m.Argument)})",
            EvolutionType.TradeShelmetKarrablast => "通讯交换 (盖盖虫/小嘴蜗)",

            // ── 特殊类 ──
            EvolutionType.LevelUpWithTeammate => $"同行有特定宝可梦时升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpHeldItemDay => $"白天携带{ItemName(m.Argument)}升级",
            EvolutionType.LevelUpHeldItemNight => $"夜晚携带{ItemName(m.Argument)}升级",
            EvolutionType.LevelUpNatureAmped => $"昂扬性格升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpNatureLowKey => $"低调性格升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpFormFemale1 => $"♀特定形态升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpAffection50MoveType => $"友好度+特定招式升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpMoveType => $"特定属性招式升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpWeather => $"特定天气升级 (Lv.{m.Level}+)",
            EvolutionType.LevelUpInverted => $"倒置升级 (Lv.{m.Level}+)",
            EvolutionType.TowerOfDarkness => "恶之塔",
            EvolutionType.TowerOfWaters => "水之塔",
            EvolutionType.CriticalHitsInBattle => "3次击中要害后升级",
            EvolutionType.HitPointsLostInBattle => "受到特定HP损伤后升级",
            EvolutionType.Spin => "旋转进化",
            EvolutionType.Hisui => "洗翠地区进化",
            EvolutionType.UseMoveAgileStyle => "使用迅疾招式",
            EvolutionType.UseMoveStrongStyle => "使用刚猛招式",
            EvolutionType.UseMoveBarbBarrage => "使用千针鱼招式",
            EvolutionType.LevelUpWalkStepsWith => $"行走{m.Argument}步后升级",
            EvolutionType.LevelUpUnionCircle => "联盟集友圈升级",
            EvolutionType.LevelUpInBattleEC100 => "战斗中升级 (特定EC)",
            EvolutionType.LevelUpInBattleECElse => "战斗中升级 (其他EC)",
            EvolutionType.LevelUpCollect999 => $"收集{m.Argument}×999后升级",
            EvolutionType.LevelUpDefeatEquals => "击败特定宝可梦后升级",
            EvolutionType.LevelUpUseMoveSpecial => "使用特定招式后升级",
            EvolutionType.LevelUpKnowMoveECElse => "习得招式后升级 (其他EC)",
            EvolutionType.LevelUpKnowMoveEC100 => "习得招式后升级 (特定EC)",
            EvolutionType.LevelUpRecoilDamageMale => "♂受到反伤后升级",
            EvolutionType.LevelUpRecoilDamageFemale => "♀受到反伤后升级",

            _ => $"特殊进化[{m.Method}]",
        };
    }

    /// <summary>
    /// ISpeciesForm 临时实现，用于 TryEvolve 的 target 参数。
    /// </summary>
    private readonly record struct EvoTarget(ushort Species, byte Form) : ISpeciesForm;
}
