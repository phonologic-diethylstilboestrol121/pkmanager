using System.Text;
using System.Text.Json;
using PKHeX.Core;
using PkManager.Server.Helpers;
using PkManager.Server.Localization;
using PkManager.Server.Models.Request;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Services;

/// <summary>
/// 合法性生成与自动修复服务 — 封装 PKHeX.Core EncounterMovesetGenerator + LegalityAnalysis API。
/// 提供: Showdown导入 / 模板生成 / 自动修复 三大能力。
/// </summary>
public class LegalizationService
{
    private readonly PokemonEditService _editService;
    private readonly ParseService _parseService;
    private readonly IPkhexStringProvider _pkhexStrings;
    private readonly IBackendMessageLocalizer _messages;

    public LegalizationService(
        PokemonEditService editService,
        ParseService parseService,
        IPkhexStringProvider pkhexStrings,
        IBackendMessageLocalizer messages)
    {
        _editService = editService;
        _parseService = parseService;
        _pkhexStrings = pkhexStrings;
        _messages = messages;
    }

    private string Text(string key, params object?[] args) => _messages.Get(key, args);

    // ── URL 获取辅助 ──────────────────────────────────────

    /// <summary>
    /// 尝试解析 Showdown/PokePaste URL 为原始文本。
    /// </summary>
    private static (string? Text, string? Error) ResolveUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (input, null);

        if (BattleTemplateTeams.TryGetSetLines(input, out var content))
            return (content, null);

        var trimmed = input.Trim();
        var isUrl = ShowdownTeam.IsURL(trimmed, out _) || PokepasteTeam.IsURL(trimmed, out _);
        if (!isUrl)
            return (input, null);

        return (null, null);
    }

    // ── Showdown 导入 ──────────────────────────────────────

    /// <summary>
    /// 从 Showdown 文本解析并生成合法宝可梦。多套文本只取第一只。
    /// </summary>
    public (PKM? Pkm, string? Error, string? EncounterType) GenerateFromShowdown(
        ShowdownImportRequest request, ITrainerInfo trainerInfo)
    {
        // 1. 解析 Showdown 文本（支持 PokePaste/Showdown URL）
        var (resolvedText, urlError) = ResolveUrl(request.ShowdownText);
        if (urlError != null)
            return (null, urlError, null);
        if (resolvedText == null)
            return (null, Text("legalize.urlFetchFailed"), null);

        List<ShowdownSet> sets;
        try
        {
            sets = ShowdownParsing.GetShowdownSets(resolvedText!).ToList();
        }
        catch (Exception ex)
        {
            return (null, Text("legalize.showdownParseFailed", ex.Message), null);
        }

        if (sets.Count == 0)
            return (null, Text("legalize.showdownTextInvalid"), null);

        var set = sets[0];
        if (set.Species == 0)
            return (null, Text("legalize.speciesUnrecognized"), null);

        // 2. 创建空白 PKM 并投影 ShowdownSet 字段
        var pk = EntityBlank.GetBlank(trainerInfo);

        try
        {
            pk.Species = set.Species;
            pk.Form = set.Form;
            if (set.Gender.HasValue)
                pk.Gender = set.Gender.Value;
            pk.CurrentLevel = (byte)Math.Clamp((int)set.Level, 1, 100);

            // IsShiny 只读 → 用 SetShiny()
            if (set.Shiny)
                pk.SetShiny();

            if (set.Nature.IsFixed)
                pk.SetNature(set.Nature);

            // Ability: ShowdownSet.Ability 是 int (ability ID)
            if (set.Ability >= 0)
            {
                var slot = MapAbilityIdToSlot(set.Ability, pk.PersonalInfo);
                if (slot == null)
                    return (null, Text("legalize.abilityNotApplicable", set.Ability), null);
                PokemonEditService.ApplyAbilitySelection(pk, set.Ability, slot.Value);
            }
        }
        catch (Exception ex)
        {
            return (null, Text("legalize.fieldProjectionFailed", ex.Message), null);
        }

        // 3. 获取招式列表
        var moves = new ReadOnlyMemory<ushort>(set.Moves);

        // 4. 搜索合法遭遇
        var version = (GameVersion)request.TargetGameVersion;
        IEnumerable<IEncounterable> encounters;
        try
        {
            encounters = EncounterMovesetGenerator.GenerateEncounters(pk, trainerInfo, moves, version);
        }
        catch (Exception ex)
        {
            return (null, Text("legalize.encounterSearchFailed", ex.Message), null);
        }

        // 5. 逐个尝试生成
        foreach (var enc in encounters)
        {
            try
            {
                if (enc is not IEncounterConvertible convertible)
                    continue;

                var criteria = EncounterCriteria.Unrestricted;
                var generated = convertible.ConvertToPKM(trainerInfo, criteria);
                if (generated == null)
                    continue;

                // 应用 ShowdownSet 全部字段到生成结果
                ApplyShowdownTraits(generated, set);

                var la = new LegalityAnalysis(generated);
                if (la.Valid)
                    return (generated, null, enc.GetType().Name);
            }
            catch
            {
                continue;
            }
        }

        return (null, Text("legalize.noEncounterTemplate"), null);
    }

    // ── 模板生成 ────────────────────────────────────────────

    /// <summary>
    /// 从模板（物种 + 版本 + 可选约束）生成合法宝可梦。
    /// </summary>
    public (PKM? Pkm, string? Error, List<string> Changes) GenerateFromTemplate(
        LegalizationRequest request, ITrainerInfo trainerInfo)
    {
        var changes = new List<string>();

        // 1. 创建空白 PKM
        var blank = EntityBlank.GetBlank(trainerInfo);

        blank.Species = (ushort)request.Species;
        blank.Form = (byte)(request.Form ?? 0);
        blank.Gender = request.Gender ?? blank.GetSaneGender();
        blank.CurrentLevel = (byte)(request.Level ?? 50);

        // 2. 构建 EncounterCriteria
        var criteria = EncounterCriteria.Unrestricted;
        if (request.Nature.HasValue)
            criteria = criteria with { Nature = (Nature)request.Nature.Value };
        if (request.IsShiny == true)
            criteria = criteria with { Shiny = Shiny.Always };

        // Ability: ID → 槽位映射，不匹配则 fail
        if (request.Ability.HasValue)
        {
            var slot = MapAbilityIdToSlot(request.Ability.Value, blank.PersonalInfo);
            if (slot == null)
                return (null, Text("legalize.requestedAbilityNotApplicable"), changes);
            criteria = criteria with { Ability = SlotToAbilityPermission(slot.Value) };
        }

        // 3. 构建招式列表
        var moves = request.DesiredMoves is { Length: > 0 }
            ? new ReadOnlyMemory<ushort>(request.DesiredMoves.Select(m => (ushort)m).ToArray())
            : new ReadOnlyMemory<ushort>();

        // 4. 搜索遭遇
        var version = (GameVersion)request.TargetGameVersion;
        IEnumerable<IEncounterable> encounters;
        try
        {
            encounters = EncounterMovesetGenerator.GenerateEncounters(blank, trainerInfo, moves, version);
        }
        catch (Exception ex)
        {
            return (null, Text("legalize.encounterSearchFailed", ex.Message), changes);
        }

        // 5. 逐个尝试生成
        foreach (var enc in encounters)
        {
            try
            {
                if (enc is not IEncounterConvertible convertible)
                    continue;

                var pk = convertible.ConvertToPKM(trainerInfo, criteria);
                if (pk == null)
                    continue;

                // 先验证生成结果，再应用用户指定的覆写字段
                var la = new LegalityAnalysis(pk);
                if (!la.Valid)
                    continue;

                changes.Add($"EncounterType={enc.GetType().Name}");
                changes.Add($"MetLocation={pk.MetLocation}");
                changes.Add($"OriginGame={pk.Version}");

                // 保留用户指定的 OT 信息
                if (request.PreserveOT && !string.IsNullOrEmpty(request.OriginalTrainerName))
                {
                    pk.OriginalTrainerName = request.OriginalTrainerName;
                    changes.Add("PreservedOT");
                }

                // 显式写回 DesiredMoves（encounter search 只筛选，这里覆盖最终招式）
                if (request.DesiredMoves is { Length: > 0 })
                {
                    for (int i = 0; i < 4; i++)
                        pk.SetMove(i, i < request.DesiredMoves.Length
                            ? (ushort)request.DesiredMoves[i] : (ushort)0);
                }

                // 覆写字段后复验合法性（OT名称/招式变更可能引入新的非法性）
                if (request.PreserveOT || request.DesiredMoves is { Length: > 0 })
                {
                    var la2 = new LegalityAnalysis(pk);
                    if (!la2.Valid)
                        continue;
                }

                return (pk, null, changes);
            }
            catch
            {
                continue;
            }
        }

        return (null, Text("legalize.noEncounterTemplateAdjust"), changes);
    }

    // ── 自动修复 ────────────────────────────────────────────

    /// <summary>
    /// 对非法宝可梦应用自动修复（临时状态，不持久化）。
    /// 先 Apply 当前编辑快照，再按 capability 分层执行修复。
    /// </summary>
    public AutoFixResultDto AutoFix(PKM pkm, PokemonEditRequest editSnapshot,
        string[]? fixActions, ITrainerInfo trainerInfo)
    {
        var result = new AutoFixResultDto();

        // Step 0: 应用当前编辑状态（解决修旧 base64 问题）
        try
        {
            _editService.ApplyEditsToPkm(pkm, editSnapshot);
        }
        catch (Exception ex)
        {
            result.FailedFixes.Add($"ApplyEdits: {ex.Message}");
            result.Status = LegalityStatus.Illegal;
            result.Report = ex.Message;
            return result;
        }

        // Step 1: 运行 LegalityAnalysis
        var la = new LegalityAnalysis(pkm);
        if (la.Valid)
        {
            result.Fixed = false;
            result.Status = LegalityStatus.Legal;
            return result;
        }

        var enc = la.EncounterMatch;
        if (enc == null)
        {
            result.FailedFixes.Add("FindEncounter");
            result.Status = LegalityStatus.Illegal;
            result.Report = "无法匹配合法遭遇模板";
            return result;
        }

        // 确定要执行的修复动作：null/空/"all" → 全部 7 项
        var allActions = new[] { "FixBall", "FixMetLocation", "FixMoves", "FixRelearnMoves",
                                "FixAbility", "FixNature", "FixShiny" };
        var actions = fixActions is { Length: > 0 }
                && !(fixActions.Length == 1 && string.Equals(fixActions[0], "all", StringComparison.OrdinalIgnoreCase))
            ? new HashSet<string>(fixActions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(allActions, StringComparer.OrdinalIgnoreCase);

        // FixBall — IFixedBall 一定可用
        if (actions.Contains("FixBall"))
        {
            try
            {
                pkm.Ball = enc.FixedBall != Ball.None
                    ? (byte)enc.FixedBall
                    : (byte)Ball.Poke;
                result.AppliedFixes.Add("FixBall");
            }
            catch { result.FailedFixes.Add("FixBall"); }
        }

        // FixMetLocation — ILocation + IVersion 一定可用
        if (actions.Contains("FixMetLocation"))
        {
            try
            {
                pkm.MetLocation = enc.Location != 0 ? (ushort)enc.Location : pkm.MetLocation;
                pkm.Version = enc.Version;
                result.AppliedFixes.Add("FixMetLocation");
            }
            catch { result.FailedFixes.Add("FixMetLocation"); }
        }

        // FixMoves — IMoveset 可选
        if (actions.Contains("FixMoves"))
        {
            try
            {
                if (enc is IMoveset { Moves: { HasMoves: true } })
                {
                    var m = ((IMoveset)enc).Moves;
                    pkm.SetMove(0, m.Move1);
                    pkm.SetMove(1, m.Move2);
                    pkm.SetMove(2, m.Move3);
                    pkm.SetMove(3, m.Move4);
                }
                else if (enc is IEncounterConvertible convertible)
                {
                    var regen = convertible.ConvertToPKM(trainerInfo);
                    if (regen != null)
                    {
                        for (int i = 0; i < 4; i++)
                            pkm.SetMove(i, regen.GetMove(i));
                    }
                }
                result.AppliedFixes.Add("FixMoves");
            }
            catch { result.FailedFixes.Add("FixMoves"); }
        }

        // FixRelearnMoves — IRelearn 可选（回退到 ConvertToPKM）
        if (actions.Contains("FixRelearnMoves"))
        {
            try
            {
                if (enc is IRelearn { Relearn: { HasMoves: true } })
                {
                    var r = ((IRelearn)enc).Relearn;
                    var rl = pkm.RelearnMoves;
                    if (rl != null)
                    {
                        rl[0] = r.Move1;
                        rl[1] = r.Move2;
                        rl[2] = r.Move3;
                        rl[3] = r.Move4;
                    }
                }
                else if (enc is IEncounterConvertible convertible)
                {
                    var regen = convertible.ConvertToPKM(trainerInfo);
                    var rlSrc = regen?.RelearnMoves;
                    var rlDst = pkm.RelearnMoves;
                    if (rlSrc != null && rlDst != null)
                        rlSrc.CopyTo(rlDst);
                }
                result.AppliedFixes.Add("FixRelearnMoves");
            }
            catch { result.FailedFixes.Add("FixRelearnMoves"); }
        }

        // FixAbility
        if (actions.Contains("FixAbility"))
        {
            try
            {
                var ap = enc.Ability;
                if (ap.IsSingleValue(out int slotIndex))
                    PokemonEditService.ApplyAbilitySelection(pkm, null, slotIndex);
                result.AppliedFixes.Add("FixAbility");
            }
            catch { result.FailedFixes.Add("FixAbility"); }
        }

        // FixNature
        if (actions.Contains("FixNature"))
        {
            try
            {
                if (enc is IFixedNature fn)
                {
                    pkm.SetNature(fn.Nature);
                    result.AppliedFixes.Add("FixNature");
                }
            }
            catch { result.FailedFixes.Add("FixNature"); }
        }

        // FixShiny — 完整处理 Shiny 枚举
        if (actions.Contains("FixShiny"))
        {
            try
            {
                var shinySpec = enc.Shiny;
                switch (shinySpec)
                {
                    case Shiny.Never:
                        if (pkm.IsShiny)
                        {
                            pkm.PID ^= 0x8000_0000;
                            if (pkm.IsShiny) pkm.PID ^= 0x1000_0000;
                        }
                        break;
                    case Shiny.Always:
                        if (!pkm.IsShiny)
                            pkm.SetShiny();
                        break;
                    case Shiny.AlwaysStar:
                        pkm.SetShiny();
                        if (pkm.ShinyXor != 1) pkm.SetShinySID(Shiny.AlwaysStar);
                        break;
                    case Shiny.AlwaysSquare:
                        pkm.SetShiny();
                        if (pkm.ShinyXor != 0) pkm.PID ^= 0x8000_0000;
                        break;
                    case Shiny.FixedValue:
                        if (enc is IEncounterConvertible cvt)
                        {
                            var regen = cvt.ConvertToPKM(trainerInfo);
                            if (regen != null)
                            {
                                pkm.PID = regen.PID;
                                pkm.EncryptionConstant = regen.EncryptionConstant;
                            }
                        }
                        break;
                    // Shiny.Random: no constraint
                }
                result.AppliedFixes.Add("FixShiny");
            }
            catch { result.FailedFixes.Add("FixShiny"); }
        }

        // Gen3-5 Method-1 PID/IV 关联（严格分层触发）
        if (pkm.Generation is >= 3 and <= 5
            && MethodFinder.Analyze(pkm).Type == PIDType.None
            && enc.GetType().Name.Contains("Slot")
            && enc is IEncounterConvertible cv)
        {
            try
            {
                var regen = cv.ConvertToPKM(trainerInfo);
                if (regen != null)
                {
                    pkm.PID = regen.PID;
                    pkm.EncryptionConstant = regen.EncryptionConstant;
                    pkm.IV_HP = regen.IV_HP;
                    pkm.IV_ATK = regen.IV_ATK;
                    pkm.IV_DEF = regen.IV_DEF;
                    pkm.IV_SPA = regen.IV_SPA;
                    pkm.IV_SPD = regen.IV_SPD;
                    pkm.IV_SPE = regen.IV_SPE;
                }
            }
            catch { /* non-critical */ }
        }

        // 修复后验证
        var postLa = new LegalityAnalysis(pkm);
        result.Status = ComputeLegalityStatus(postLa);
        result.Fixed = result.AppliedFixes.Count > 0;
        result.UpdatedPokemon = _parseService.MapToPokemonDto(pkm);
        result.PkmDataBase64 = GetPkmBase64(pkm);
        result.Judgements = postLa.Results.Select(r => new JudgementDto
        {
            Identifier = r.Identifier.ToString(),
            Judgement = r.Judgement.ToString(),
            Comment = "",
            Issue = GetHumanReadableIssue(r),
            CanFix = CanAutoFix(r),
            FixAction = GetFixAction(r)
        }).ToList();

        if (result.Status != LegalityStatus.Legal)
            result.Report = postLa.Report();

        return result;
    }

    // ── Showdown 解析（仅预览，不生成）──────────────────────

    /// <summary>
    /// 解析 Showdown 文本为预览列表（不执行遭遇搜索）。
    /// </summary>
    public List<ShowdownSetPreviewDto> ParseShowdownText(string text)
    {
        // 支持 PokePaste/Showdown URL
        var (resolvedText, urlError) = ResolveUrl(text);
        if (urlError != null)
            throw new BusinessException(urlError);
        if (resolvedText == null)
            throw BusinessException.FromKey("legalize.urlFetchFailed", 400);

        var sets = ShowdownParsing.GetShowdownSets(resolvedText!).ToList();
        var strings = _pkhexStrings.GetStrings();

        return sets.Select(set =>
        {
            var moves = set.Moves
                .Where(m => m != 0)
                .Select(m => m < strings.Move.Count ? strings.Move[m] : $"#{m}")
                .ToArray();

            return new ShowdownSetPreviewDto
            {
                Species = set.Species < strings.Species.Count
                    ? strings.Species[set.Species] : $"#{set.Species}",
                SpeciesId = set.Species,
                Nickname = set.Nickname,
                Level = set.Level,
                Shiny = set.Shiny,
                Gender = set.Gender?.ToString(),
                Ability = set.Ability >= 0 && set.Ability < strings.Ability.Count
                    ? strings.Ability[set.Ability] : null,
                Nature = set.Nature.IsFixed ? set.Nature.ToString() : null,
                Item = set.HeldItem > 0 && set.HeldItem < strings.Item.Count
                    ? strings.Item[set.HeldItem] : null,
                Moves = moves,
                Form = set.FormName,
                RawText = set.Text
            };
        }).ToList();
    }

    /// <summary>
    /// 将 PKM 导出为 Showdown 文本。
    /// </summary>
    public string ExportShowdown(PKM pkm, PokemonEditRequest? editSnapshot = null)
    {
        if (editSnapshot != null)
            _editService.ApplyEditsToPkm(pkm, editSnapshot);

        return ShowdownParsing.GetShowdownText(pkm);
    }

    // ── 缓存（银行批扫结果）── 已迁移至 LegalityCacheService（单一数据源）─────────────────

    // ── 辅助方法 ────────────────────────────────────────────

    /// <summary>
    /// 将 ShowdownSet 中的用户指定字段投影到已生成的 PKM 上。
    /// Nature/Ability 在此方法中处理；Nickname/HeldItem/IVs/EVs/Friendship/Moves 也写回。
    /// </summary>
    private static void ApplyShowdownTraits(PKM pk, ShowdownSet set)
    {
        // Nature
        if (set.Nature.IsFixed)
            pk.SetNature(set.Nature);

        // Ability
        if (set.Ability >= 0 && pk.PersonalInfo != null)
        {
            var slot = MapAbilityIdToSlot(set.Ability, pk.PersonalInfo);
            if (slot.HasValue)
                PokemonEditService.ApplyAbilitySelection(pk, set.Ability, slot.Value);
        }

        // Nickname
        if (!string.IsNullOrEmpty(set.Nickname))
            pk.Nickname = set.Nickname;

        // HeldItem
        if (set.HeldItem > 0)
            pk.HeldItem = set.HeldItem;

        // IVs
        if (set.IVs is { Length: 6 })
        {
            pk.IV_HP  = set.IVs[0]; pk.IV_ATK = set.IVs[1]; pk.IV_DEF = set.IVs[2];
            pk.IV_SPA = set.IVs[3]; pk.IV_SPD = set.IVs[4]; pk.IV_SPE = set.IVs[5];
        }

        // EVs
        if (set.EVs is { Length: 6 })
        {
            pk.EV_HP  = set.EVs[0]; pk.EV_ATK = set.EVs[1]; pk.EV_DEF = set.EVs[2];
            pk.EV_SPA = set.EVs[3]; pk.EV_SPD = set.EVs[4]; pk.EV_SPE = set.EVs[5];
        }

        // Friendship — 仅当 Showdown 文本显式声明时才覆盖（否则保留 ConvertToPKM 生成的物种基础值）
        if (set.Text.Contains("Friendship:", StringComparison.OrdinalIgnoreCase))
            pk.OriginalTrainerFriendship = (byte)Math.Clamp((int)set.Friendship, 0, 255);

        // Moves — 显式写回招式（encounter search 只用于筛选，这里覆盖最终结果）
        var moves = set.Moves;
        if (moves.Length >= 4)
        {
            for (int i = 0; i < 4; i++)
                pk.SetMove(i, i < moves.Length ? moves[i] : (ushort)0);
        }
    }

    /// <summary>PKM → Base64（v26: DecryptedPartyData→WriteDecryptedDataParty）</summary>
    private static string GetPkmBase64(PKM pkm)
    {
        var buffer = new byte[pkm.SIZE_PARTY];
        pkm.WriteDecryptedDataParty(buffer);
        return Convert.ToBase64String(buffer);
    }

    /// <summary>
    /// 将能力 ID 映射为 PKHeX 的能力槽位索引 (0=第一特性, 1=第二特性, 2=梦特)。
    /// 不匹配时返回 null（不降级到 Any12）。
    /// </summary>
    public static int? MapAbilityIdToSlot(int abilityId, IPersonalInfo pi)
    {
        var idx = pi.GetIndexOfAbility(abilityId);
        return idx >= 0 ? idx : null;
    }

    /// <summary>
    /// 将能力槽位索引转为 EncounterCriteria 的 AbilityPermission。
    /// </summary>
    private static AbilityPermission SlotToAbilityPermission(int slotIndex) => slotIndex switch
    {
        0 => AbilityPermission.OnlyFirst,
        1 => AbilityPermission.OnlySecond,
        2 => AbilityPermission.OnlyHidden,
        _ => AbilityPermission.Any12
    };

    // ── 公开的合法性辅助方法（从 PokemonEditService 提升，供 BankService 等复用）─

    public static LegalityStatus ComputeLegalityStatus(LegalityAnalysis la)
    {
        if (la.Valid) return LegalityStatus.Legal;

        var hasInvalid = la.Results.Any(r => r.Judgement == Severity.Invalid)
                         || !MoveResult.AllValid(la.Info.Moves)
                         || !MoveResult.AllValid(la.Info.Relearn);

        if (hasInvalid) return LegalityStatus.Illegal;

        var hasFishy = la.Results.Any(r => r.Judgement == Severity.Fishy);
        return hasFishy ? LegalityStatus.Fishy : LegalityStatus.Legal;
    }

    public static string GetFirstIssue(LegalityAnalysis la)
    {
        foreach (var r in la.Results)
        {
            if (r.Judgement == Severity.Invalid)
                return GetHumanReadableIssue(r);
        }
        if (!MoveResult.AllValid(la.Info.Moves))
            return "存在不合法招式";
        if (!MoveResult.AllValid(la.Info.Relearn))
            return "存在不合法回忆招式";
        foreach (var r in la.Results)
        {
            if (r.Judgement == Severity.Fishy)
                return GetHumanReadableIssue(r);
        }
        return string.Empty;
    }

    public static string GetHumanReadableIssue(CheckResult r)
    {
        var id = GetChineseCheckName(r.Identifier);
        var comment = $"{id}校验失败";
        return r.Judgement switch
        {
            Severity.Valid => string.Empty,
            Severity.Fishy => $"⚠️ {id}: {comment}",
            Severity.Invalid => $"❌ {id}: {comment}",
            _ => comment
        };
    }

    public static string GetChineseCheckName(CheckIdentifier id) => id switch
    {
        CheckIdentifier.Encounter => "遭遇",
        CheckIdentifier.CurrentMove => "当前招式",
        CheckIdentifier.RelearnMove => "回忆招式",
        CheckIdentifier.Shiny => "闪光",
        CheckIdentifier.Gender => "性别",
        CheckIdentifier.Language => "语言",
        CheckIdentifier.Nickname => "昵称",
        CheckIdentifier.Trainer => "训练家",
        CheckIdentifier.Level => "等级",
        CheckIdentifier.Ball => "球种",
        CheckIdentifier.Memory => "记忆",
        CheckIdentifier.Geography => "地理",
        CheckIdentifier.Form => "形态",
        CheckIdentifier.Egg => "蛋",
        CheckIdentifier.Misc => "杂项",
        CheckIdentifier.Fateful => "命运邂逅",
        CheckIdentifier.Ribbon => "缎带",
        CheckIdentifier.Training => "训练",
        CheckIdentifier.Ability => "特性",
        CheckIdentifier.Evolution => "进化",
        CheckIdentifier.Nature => "性格",
        CheckIdentifier.GameOrigin => "来源版本",
        CheckIdentifier.HeldItem => "持有道具",
        CheckIdentifier.RibbonMark => "证章",
        CheckIdentifier.Marking => "标记",
        _ => id.ToString()
    };

    public static bool CanAutoFix(CheckResult r)
    {
        if (r.Judgement == Severity.Valid) return false;
        return r.Identifier switch
        {
            CheckIdentifier.Ball => true,
            CheckIdentifier.Encounter => true,
            CheckIdentifier.CurrentMove => true,
            CheckIdentifier.RelearnMove => true,
            CheckIdentifier.Ability => true,
            CheckIdentifier.Nature => true,
            CheckIdentifier.Shiny => true,
            _ => false
        };
    }

    public static string? GetFixAction(CheckResult r)
    {
        return r.Identifier switch
        {
            CheckIdentifier.Ball => "FixBall",
            CheckIdentifier.Encounter => "FixMetLocation",
            CheckIdentifier.CurrentMove => "FixMoves",
            CheckIdentifier.RelearnMove => "FixRelearnMoves",
            CheckIdentifier.Ability => "FixAbility",
            CheckIdentifier.Nature => "FixNature",
            CheckIdentifier.Shiny => "FixShiny",
            _ => null
        };
    }

    // ── D.2 遭遇数据库 ──────────────────────────────────────

    /// <summary>
    /// 搜索合法遭遇模板。需要 ITrainerInfo 提供目标上下文（由 Controller 从存档构造）。
    /// </summary>
    public EncounterSearchResultDto SearchEncounters(EncounterSearchRequest request, ITrainerInfo trainerInfo)
    {
        var result = new EncounterSearchResultDto();
        var strings = _pkhexStrings.GetStrings();

        // 1. 创建空白 PKM
        var blank = EntityBlank.GetBlank(trainerInfo);
        blank.Species = (ushort)request.Species;
        blank.Form = (byte)request.Form;

        // 2. 搜索遭遇（空招式 = 无招式约束，返回全部遭遇）
        IEnumerable<IEncounterable> allEncounters;
        try
        {
            allEncounters = EncounterMovesetGenerator.GenerateEncounters(
                blank, trainerInfo, ReadOnlyMemory<ushort>.Empty);
        }
        catch (Exception ex)
        {
            throw BusinessException.FromKey("legalize.encounterSearchFailed", 400, ex.Message);
        }

        // 3. 准备过滤条件
        var typeFilter = request.EncounterTypes is { Length: > 0 }
            ? new HashSet<string>(request.EncounterTypes, StringComparer.OrdinalIgnoreCase)
            : null;

        // 4. 迭代构建结果
        int index = 0;
        foreach (var enc in allEncounters)
        {
            var encounterType = ClassifyEncounterType(enc);
            if (typeFilter != null && !typeFilter.Contains(encounterType))
                continue;
            if (request.LevelMin.HasValue && enc.LevelMax < request.LevelMin.Value)
                continue;
            if (request.LevelMax.HasValue && enc.LevelMin > request.LevelMax.Value)
                continue;

            var item = BuildEncounterItem(enc, encounterType, strings, request, index);
            result.Items.Add(item);
            index++;
        }

        result.TotalCount = result.Items.Count;
        return result;
    }

    /// <summary>
    /// 将遭遇模板的约束字段应用到当前编辑中的宝可梦（不写盘）。
    /// 先投影 editSnapshot → 再覆写遭遇约束字段 → 返回更新后的 PokemonDto（含新 base64）。
    /// </summary>
    public EncounterApplyResultDto ApplyEncounter(
        EncounterApplyRequest request, ITrainerInfo trainerInfo, PKHeX.Core.SaveFile sav)
    {
        var result = new EncounterApplyResultDto();

        // 1. 重建 PKM
        PKM pkm;
        try
        {
            var data = Convert.FromBase64String(request.PkmDataBase64);
            pkm = EntityFormat.GetFromBytes(data)
                ?? throw BusinessException.FromKey("legalize.parsePokemonFailed", 400);
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            throw BusinessException.FromKey("legalize.parsePokemonDetailedFailed", 400, ex.Message);
        }

        // 2. 应用当前编辑面板快照
        try
        {
            _editService.ApplyEditsToPkm(pkm, request.EditSnapshot);
        }
        catch (Exception ex)
        {
            throw BusinessException.FromKey("legalize.applyEditStateFailed", 400, ex.Message);
        }

        // 3. 定位遭遇
        var enc = RecomputeEncounter(request.RecomputeToken, trainerInfo, request.SaveFileId);
        if (enc == null)
        {
            result.Error = Text("legalize.encounterTemplateExpired");
            return result;
        }

        // 4. 按约束字段规则表逐字段覆写
        ApplyEncounterConstraints(pkm, enc, trainerInfo, result.AppliedFields);

        // 5. 返回更新后的 DTO（含新 base64）
        result.Success = true;
        result.Pokemon = _parseService.MapToPokemonDto(pkm);
        return result;
    }

    /// <summary>
    /// 从遭遇模板生成宝可梦（不写盘，返回 PKM 供 Controller 做兼容转换+写入）。
    /// </summary>
    public (PKM? Pkm, string? Error) GenerateFromEncounter(
        EncounterGenerateRequest request, ITrainerInfo trainerInfo)
    {
        // 1. 定位遭遇
        var enc = RecomputeEncounter(request.RecomputeToken, trainerInfo, request.SaveFileId);
        if (enc == null)
            return (null, Text("legalize.encounterTemplateExpired"));

        // 2. 检查是否可转换
        if (enc is not IEncounterConvertible convertible)
            return (null, Text("legalize.encounterTypeUnsupported", enc.GetType().Name));

        // 3. 构建 EncounterCriteria
        var criteria = EncounterCriteria.Unrestricted;
        if (request.ForceShiny == true)
            criteria = criteria with { Shiny = Shiny.Always };
        if (request.Nature.HasValue)
            criteria = criteria with { Nature = (Nature)request.Nature.Value };
        if (request.Gender.HasValue)
            criteria = criteria with { Gender = (Gender)request.Gender.Value };
        if (request.Level.HasValue)
            criteria = criteria with { LevelMin = (byte)request.Level.Value, LevelMax = (byte)request.Level.Value };

        // 4. 生成
        PKM pkm;
        try
        {
            pkm = convertible.ConvertToPKM(trainerInfo, criteria);
            if (pkm == null)
                return (null, Text("legalize.encounterConvertNull"));
        }
        catch (Exception ex)
        {
            return (null, Text("legalize.encounterGenerateException", ex.Message));
        }

        return (pkm, null);
    }

    // ── D.2 私有辅助方法 ──────────────────────────────────

    /// <summary>
    /// 遭遇分类：Egg / Mystery / Static / Trade / Slot
    /// </summary>
    private static string ClassifyEncounterType(IEncounterable enc)
    {
        if (enc is IEncounterEgg) return "Egg";
        if (enc is MysteryGift) return "Mystery";
        var typeName = enc.GetType().Name;
        if (typeName.StartsWith("EncounterStatic") || typeName.StartsWith("EncounterTera")
            || typeName.StartsWith("EncounterDist") || typeName.StartsWith("EncounterMight")
            || typeName.StartsWith("EncounterOutbreak") || typeName.StartsWith("EncounterFixed")
            || typeName.StartsWith("EncounterGift"))
            return "Static";
        if (typeName.StartsWith("EncounterTrade")) return "Trade";
        return "Slot";
    }

    /// <summary>闪光规格 → 可读字符串</summary>
    private static string ShinyToString(Shiny shiny) => shiny switch
    {
        Shiny.Never => "Never",
        Shiny.Random => "Random",
        Shiny.Always => "Always",
        Shiny.AlwaysStar => "AlwaysStar",
        Shiny.AlwaysSquare => "AlwaysSquare",
        Shiny.FixedValue => "FixedValue",
        _ => shiny.ToString()
    };

    /// <summary>提取遭遇地点中文名（处理 MetLocation / EggLocation 选择）</summary>
    private static string? GetEncounterLocationName(IEncounterable enc)
    {
        ushort location;
        bool isEgg;

        if (enc is ILocation loc)
        {
            if (loc.Location != 0)
            {
                location = (ushort)loc.Location;
                isEgg = false;
            }
            else if (loc.EggLocation != 0)
            {
                location = (ushort)loc.EggLocation;
                isEgg = true;
            }
            else
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        var gen = (byte)enc.Generation;
        return GameInfo.GetLocationName(isEgg, location, gen, gen, enc.Version);
    }

    /// <summary>构建单条遭遇信息 DTO</summary>
    private EncounterItemDto BuildEncounterItem(IEncounterable enc, string encounterType,
        IBasicStrings strings, EncounterSearchRequest request, int index)
    {
        var item = new EncounterItemDto
        {
            Index = index,
            EncounterType = encounterType,
            TypeName = enc.GetType().Name,
            LongName = enc.LongName ?? enc.Name,
            Version = (int)enc.Version,
            VersionName = GameInfo.GetVersionName(enc.Version),
            Generation = enc.Generation,
            LocationName = GetEncounterLocationName(enc),
            LevelMin = enc.LevelMin,
            LevelMax = enc.LevelMax,
            Shiny = ShinyToString(enc.Shiny),
            Ability = enc.Ability.ToString(),
            RecomputeToken = BuildRecomputeToken(request, index),
        };

        // Moves
        if (enc is IMoveset { Moves: { HasMoves: true } } ms)
        {
            var moves = new[] { (int)ms.Moves.Move1, (int)ms.Moves.Move2, (int)ms.Moves.Move3, (int)ms.Moves.Move4 };
            item.Moves = moves.Where(m => m != 0).ToArray();
            item.MoveNames = item.Moves
                .Select(m => m < strings.Move.Count ? strings.Move[m] : $"#{m}")
                .ToArray();
        }

        // RelearnMoves
        if (enc is IRelearn { Relearn: { HasMoves: true } } rl)
        {
            item.RelearnMoves = new[] { (int)rl.Relearn.Move1, (int)rl.Relearn.Move2, (int)rl.Relearn.Move3, (int)rl.Relearn.Move4 };
        }

        // Ball
        if (enc is IFixedBall fb && fb.FixedBall != Ball.None)
        {
            item.FixedBall = (int)fb.FixedBall;
            var ballId = (int)fb.FixedBall;
            item.FixedBallName = ballId < strings.Item.Count ? strings.Item[ballId] : $"Ball#{ballId}";
        }

        // Nature
        if (enc is IFixedNature fn)
            item.FixedNature = (int)fn.Nature;

        // Gender
        if (enc is IFixedGender fg && fg.IsFixedGender)
            item.Gender = fg.Gender;

        return item;
    }

    /// <summary>
    /// 按约束字段规则表逐字段覆写（仅可单值表达的约束）。
    /// 规则：
    ///   - IFixedAbilityNumber: 仅 Ability.IsSingleValue 时写
    ///   - IFixedGender: 仅 Gender != 0xFF (IsFixedGender) 时写
    ///   - IShiny: Never/Always/AlwaysStar/AlwaysSquare → 应用；FixedValue/Random → 跳过
    /// </summary>
    private static void ApplyEncounterConstraints(PKM pkm, IEncounterable enc,
        ITrainerInfo trainerInfo, List<string> appliedFields)
    {
        // MetLocation
        if (enc is ILocation { Location: not 0 } loc)
        {
            pkm.MetLocation = (ushort)loc.Location;
            appliedFields.Add("MetLocation");
        }

        // MetLevel
        if (enc.LevelMin > 0)
        {
            pkm.MetLevel = (byte)enc.LevelMin;
            appliedFields.Add("MetLevel");
        }

        // Version
        pkm.Version = enc.Version;
        appliedFields.Add("Version");

        // Ball
        if (enc is IFixedBall fb && fb.FixedBall != Ball.None)
        {
            pkm.Ball = (byte)fb.FixedBall;
            appliedFields.Add("Ball");
        }

        // Ability — 仅单值时应用（RefreshAbility 将槽位索引 0/1/2 映射为正确位值 1/2/4）
        if (enc.Ability.IsSingleValue(out int abilitySlot))
        {
            pkm.RefreshAbility(abilitySlot);
            appliedFields.Add("Ability");
        }

        // Nature
        if (enc is IFixedNature fn)
        {
            pkm.SetNature(fn.Nature);
            appliedFields.Add("Nature");
        }

        // Gender — 仅固定性别时应用
        if (enc is IFixedGender fg && fg.IsFixedGender)
        {
            pkm.Gender = fg.Gender;
            appliedFields.Add("Gender");
        }

        // Shiny — 仅 Never/Always/AlwaysStar/AlwaysSquare；跳过 FixedValue 和 Random
        switch (enc.Shiny)
        {
            case Shiny.Never:
                if (pkm.IsShiny)
                {
                    pkm.PID ^= 0x8000_0000;
                    if (pkm.IsShiny) pkm.PID ^= 0x1000_0000;
                }
                appliedFields.Add("Shiny=Never");
                break;
            case Shiny.Always:
                if (!pkm.IsShiny)
                    pkm.SetShiny();
                appliedFields.Add("Shiny=Always");
                break;
            case Shiny.AlwaysStar:
                pkm.SetShiny();
                if (pkm.ShinyXor != 1) pkm.SetShinySID(Shiny.AlwaysStar);
                appliedFields.Add("Shiny=AlwaysStar");
                break;
            case Shiny.AlwaysSquare:
                pkm.SetShiny();
                if (pkm.ShinyXor != 0) pkm.PID ^= 0x8000_0000;
                appliedFields.Add("Shiny=AlwaysSquare");
                break;
            // FixedValue: 跳过（需要固定 PID，超出 D.2 范围）
            // Random: 跳过（无约束）
        }

        // Moves
        if (enc is IMoveset { Moves: { HasMoves: true } } moveset)
        {
            var m = moveset.Moves;
            pkm.SetMove(0, m.Move1);
            pkm.SetMove(1, m.Move2);
            pkm.SetMove(2, m.Move3);
            pkm.SetMove(3, m.Move4);
            appliedFields.Add("Moves");
        }

        // RelearnMoves
        if (enc is IRelearn { Relearn: { HasMoves: true } } rl)
        {
            var r = rl.Relearn;
            var dst = pkm.RelearnMoves;
            if (dst != null)
            {
                dst[0] = r.Move1;
                dst[1] = r.Move2;
                dst[2] = r.Move3;
                dst[3] = r.Move4;
                appliedFields.Add("RelearnMoves");
            }
        }

        // Egg encounter
        if (enc is IEncounterEgg)
        {
            pkm.IsEgg = true;
            if (enc is ILocation eggLoc && eggLoc.EggLocation != 0)
                pkm.EggLocation = (ushort)eggLoc.EggLocation;
            pkm.EggMetDate = pkm.MetDate;
            appliedFields.Add("EggData");
        }
        else if (enc.IsEgg && enc is ILocation eggLoc2 && eggLoc2.EggLocation != 0)
        {
            // 非 IEncounterEgg 但 IsEgg=true（如 Gen2 赠蛋）
            pkm.EggLocation = (ushort)eggLoc2.EggLocation;
            appliedFields.Add("EggLocation");
        }

        // FatefulEncounter — 读取遭遇模板实际值（部分模板为 false）
        if (enc is IFatefulEncounterReadOnly fe)
        {
            pkm.FatefulEncounter = fe.FatefulEncounter;
            appliedFields.Add($"FatefulEncounter={fe.FatefulEncounter}");
        }
    }

    // ── Token 序列化 ──────────────────────────────────────

    private record EncounterTokenData(
        int Species, int Form, Guid SaveFileId,
        int? LevelMin, int? LevelMax, string[]? EncounterTypes,
        int ResultIndex);

    private static string BuildRecomputeToken(EncounterSearchRequest request, int index)
    {
        var data = new EncounterTokenData(
            request.Species, request.Form, request.SaveFileId,
            request.LevelMin, request.LevelMax, request.EncounterTypes, index);
        var json = JsonSerializer.Serialize(data);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static EncounterTokenData ParseRecomputeToken(string token)
    {
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            return JsonSerializer.Deserialize<EncounterTokenData>(json)
                ?? throw BusinessException.FromKey("legalize.tokenDeserializeFailed", 400);
        }
        catch (BusinessException) { throw; }
        catch (Exception ex)
        {
            throw BusinessException.FromKey("legalize.tokenParseFailed", 400, ex.Message);
        }
    }

    /// <summary>
    /// 从 Token 重算并定位遭遇模板（确定性：PKHeX 静态数组迭代顺序固定）。
    /// </summary>
    private IEncounterable? RecomputeEncounter(string token, ITrainerInfo trainerInfo, Guid saveFileId)
    {
        var tokenData = ParseRecomputeToken(token);
        if (tokenData.SaveFileId != saveFileId)
            return null;

        var blank = EntityBlank.GetBlank(trainerInfo);
        blank.Species = (ushort)tokenData.Species;
        blank.Form = (byte)tokenData.Form;

        IEnumerable<IEncounterable> allEncounters;
        try
        {
            allEncounters = EncounterMovesetGenerator.GenerateEncounters(
                blank, trainerInfo, ReadOnlyMemory<ushort>.Empty);
        }
        catch
        {
            return null;
        }

        var typeFilter = tokenData.EncounterTypes is { Length: > 0 }
            ? new HashSet<string>(tokenData.EncounterTypes, StringComparer.OrdinalIgnoreCase)
            : null;

        int currentIndex = 0;
        foreach (var enc in allEncounters)
        {
            var encounterType = ClassifyEncounterType(enc);
            if (typeFilter != null && !typeFilter.Contains(encounterType))
                continue;
            if (tokenData.LevelMin.HasValue && enc.LevelMax < tokenData.LevelMin.Value)
                continue;
            if (tokenData.LevelMax.HasValue && enc.LevelMin > tokenData.LevelMax.Value)
                continue;

            if (currentIndex == tokenData.ResultIndex)
                return enc;

            currentIndex++;
        }

        return null;
    }
}
