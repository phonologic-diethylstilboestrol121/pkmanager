import type { PokemonDto } from '../../api/saveFile';

/** Validate basic field ranges before save */
export function validateFields(p: PokemonDto): string[] {
  const errs: string[] = [];
  if (p.species < 1 || p.species > 1025) errs.push('物种ID无效');
  if (p.level < 1 || p.level > 100) errs.push('等级需在1-100之间');
  if (p.gender < 0 || p.gender > 2) errs.push('性别无效');
  if (p.nature < 0 || p.nature > 24) errs.push('性格无效');
  if (p.form < 0 || p.form > 63) errs.push('形态无效');
  if ((p.nickname || '').length > 12) errs.push('昵称最多12个字符');
  if ((p.originalTrainerName || '').length > 12) errs.push('初训家名称最多12个字符');
  if ((p.handlingTrainerName || '').length > 12) errs.push('现持有人名称最多12个字符');

  const ivs = p.ivs || [];
  for (let i = 0; i < 6; i++) {
    if (ivs[i] < 0 || ivs[i] > 31) { errs.push(`个体值[${['HP','攻击','防御','特攻','特防','速度'][i]}]需在0-31范围`); break; }
  }

  const evs = p.evs || [];
  let evSum = 0;
  for (let i = 0; i < 6; i++) {
    if (evs[i] < 0 || evs[i] > 252) { errs.push('努力值需在0-252范围'); break; }
    evSum += evs[i];
  }
  if (evSum > 510) errs.push('努力值总和不可超过510');

  if (p.tid < 0 || p.tid > 65535) errs.push('表ID需在0-65535范围');
  if (p.sid < 0 || p.sid > 65535) errs.push('里ID需在0-65535范围');
  if (p.originalTrainerFriendship < 0 || p.originalTrainerFriendship > 255) errs.push('亲密度需在0-255范围');

  const moveIds = (p.moves || []).map(m => m.moveId).filter(id => id > 0);
  if (new Set(moveIds).size < moveIds.length) errs.push('招式不能重复');

  // Gen-Specific 范围校验
  if (p.purification != null && (p.purification < -10000 || p.purification > 10000))
    errs.push('净化值范围异常');
  if (p.shinyLeaf != null && (p.shinyLeaf < 0 || p.shinyLeaf > 255))
    errs.push('闪光叶原始值需在0-255范围');
  if (p.pokeStarFame != null && (p.pokeStarFame < 0 || p.pokeStarFame > 255))
    errs.push('PokeStarFame 需在0-255范围');
  if (p.fullness != null && (p.fullness < 0 || p.fullness > 255))
    errs.push('饱腹度需在0-255范围');
  if (p.enjoyment != null && (p.enjoyment < 0 || p.enjoyment > 255))
    errs.push('愉悦度需在0-255范围');
  if (p.spirit != null && (p.spirit < 0 || p.spirit > 255))
    errs.push('精神需在0-255范围');
  if (p.mood != null && (p.mood < 0 || p.mood > 255))
    errs.push('心情需在0-255范围');
  if (p.combatPower != null && p.combatPower < 0)
    errs.push('CP不能为负');

  return errs;
}

/** Build a PokemonEditRequest from PokemonDto state */
export function buildEditRequest(pokemon: PokemonDto): Record<string, unknown> {
  return {
    species: pokemon.species,
    nickname: pokemon.nickname || null,
    isNicknamed: pokemon.isNicknamed,
    gender: pokemon.gender,
    level: pokemon.level,
    nature: pokemon.nature,
    ability: pokemon.ability,
    heldItem: pokemon.heldItem,
    ball: pokemon.ball,
    isShiny: pokemon.isShiny,
    isEgg: pokemon.isEgg,
    form: pokemon.form,
    formArgument: pokemon.formArgument,
    language: pokemon.language,
    exp: pokemon.exp,
    friendship: pokemon.originalTrainerFriendship,
    handlingTrainerFriendship: pokemon.handlingTrainerFriendship,
    pokerusStrain: pokemon.pokerusStrain,
    pokerusDays: pokemon.pokerusDays,
    fatefulEncounter: pokemon.fatefulEncounter,
    heightScalar: pokemon.heightScalar,
    weightScalar: pokemon.weightScalar,
    scale: pokemon.scale,
    ivs: pokemon.ivs || [0,0,0,0,0,0],
    evs: pokemon.evs || [0,0,0,0,0,0],
    avs: pokemon.avs,
    gvs: pokemon.gvs,
    dynamaxLevel: pokemon.dynamaxLevel,
    canGigantamax: pokemon.canGigantamax,
    teraTypeOriginal: pokemon.teraTypeOriginal,
    teraTypeOverride: pokemon.teraTypeOverride,
    isAlpha: pokemon.isAlpha,
    isNoble: pokemon.isNoble,
    statNature: pokemon.statNature,
    moves: (pokemon.moves || []).map(m => m.moveId),
    movePPs: pokemon.movePPs || [0,0,0,0],
    movePPUps: pokemon.movePPUps || [0,0,0,0],
    relearnMoves: pokemon.relearnMoves,
    metLocation: pokemon.metLocation,
    metLevel: pokemon.metLevel,
    originGame: pokemon.originGame,
    metDate: pokemon.metDate,
    eggLocation: pokemon.eggLocation,
    eggDate: pokemon.eggDate,
    metTimeOfDay: pokemon.metTimeOfDay,
    groundTile: pokemon.groundTile,
    battleVersion: pokemon.battleVersion,
    obedienceLevel: pokemon.obedienceLevel,
    originalTrainerName: pokemon.originalTrainerName,
    originalTrainerGender: pokemon.originalTrainerGender,
    tid16: pokemon.tid,
    sid16: pokemon.sid,
    handlingTrainerName: pokemon.handlingTrainerName,
    handlingTrainerGender: pokemon.handlingTrainerGender,
    handlingTrainerLanguage: pokemon.handlingTrainerLanguage,
    affection: pokemon.affection,
    homeTracker: pokemon.homeTracker ? parseInt(pokemon.homeTracker, 16) : null,
    isFavorite: pokemon.isFavorite,
    country: pokemon.country,
    subRegion: pokemon.subRegion,
    consoleRegion: pokemon.consoleRegion,
    affixedRibbon: pokemon.affixedRibbon,
    markings: pokemon.markings,
    contestCool: pokemon.contestCool,
    contestBeauty: pokemon.contestBeauty,
    contestCute: pokemon.contestCute,
    contestSmart: pokemon.contestSmart,
    contestTough: pokemon.contestTough,
    contestSheen: pokemon.contestSheen,
    // -- Gen-Specific Tab --
    purification: pokemon.purification ?? null,
    shinyLeaf: pokemon.shinyLeaf ?? null,
    nSparkle: pokemon.nSparkle ?? null,
    pokeStarFame: pokemon.pokeStarFame ?? null,
    secretSuperTrainingUnlocked: pokemon.secretSuperTrainingUnlocked ?? null,
    // 确保固定长度（不足补false，超长截断）
    superTrainRegimenFlags: pokemon.superTrainRegimenFlags
      ? (() => { const a = [...pokemon.superTrainRegimenFlags!]; while (a.length < 30) a.push(false); return a.slice(0, 30); })()
      : null,
    distSuperTrainFlags: pokemon.distSuperTrainFlags
      ? (() => { const a = [...pokemon.distSuperTrainFlags!]; while (a.length < 6) a.push(false); return a.slice(0, 6); })()
      : null,
    fullness: pokemon.fullness ?? null,
    enjoyment: pokemon.enjoyment ?? null,
    hyperTrainFlags: pokemon.hyperTrainFlags
      ? (() => { const a = [...pokemon.hyperTrainFlags!]; while (a.length < 6) a.push(false); return a.slice(0, 6); })()
      : null,
    combatPower: pokemon.combatPower ?? null,
    spirit: pokemon.spirit ?? null,
    mood: pokemon.mood ?? null,
  };
}
