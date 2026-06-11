// ── 统一游戏元数据 ─────────────────────────────────────────────────
// 项目的唯一游戏数据源。Dashboard、Saves、SaveEditor、mgba.ts、melonds.ts 统一引用此处。
//
// 优先级：
//   1. gameId → 封面图路径  /assets/covers/{gameId}.png
//   2. 封面图不存在 → 彩色占位卡片（color + shortName + platform Tag）

export type GamePlatform = 'GBA' | 'NDS' | '3DS' | 'Switch';

export interface GameMeta {
  gameId: string;          // pkm_ruby, pkm_platinum ...
  displayName: string;     // 宝可梦 红宝石
  shortName: string;       // 红宝石
  color: string;           // #cf1322
  gameVersion: number;     // PKHeX version number
  generation: number;      // 3-9
  platform: GamePlatform;
}

// ── 主映射表 (gameId → GameMeta) ──────────────────────────────────

export const GAME_META: Record<string, GameMeta> = {
  // ── GBA Gen3 ──
  pkm_ruby:      { gameId: 'pkm_ruby',      displayName: '宝可梦 红宝石',   shortName: '红宝石',   color: '#cf1322', gameVersion: 2,  generation: 3, platform: 'GBA' },
  pkm_sapphire:  { gameId: 'pkm_sapphire',  displayName: '宝可梦 蓝宝石',   shortName: '蓝宝石',   color: '#0958d9', gameVersion: 1,  generation: 3, platform: 'GBA' },
  pkm_emerald:   { gameId: 'pkm_emerald',   displayName: '宝可梦 绿宝石',   shortName: '绿宝石',   color: '#08979c', gameVersion: 3,  generation: 3, platform: 'GBA' },
  pkm_firered:   { gameId: 'pkm_firered',   displayName: '宝可梦 火红',     shortName: '火红',     color: '#d4380d', gameVersion: 4,  generation: 3, platform: 'GBA' },
  pkm_leafgreen: { gameId: 'pkm_leafgreen', displayName: '宝可梦 叶绿',     shortName: '叶绿',     color: '#389e0d', gameVersion: 5,  generation: 3, platform: 'GBA' },

  // ── NDS Gen4 ──
  pkm_diamond:   { gameId: 'pkm_diamond',   displayName: '宝可梦 钻石',     shortName: '钻石',     color: '#5b8bd4', gameVersion: 10, generation: 4, platform: 'NDS' },
  pkm_pearl:     { gameId: 'pkm_pearl',     displayName: '宝可梦 珍珠',     shortName: '珍珠',     color: '#e799b0', gameVersion: 11, generation: 4, platform: 'NDS' },
  pkm_platinum:  { gameId: 'pkm_platinum',  displayName: '宝可梦 白金',     shortName: '白金',     color: '#b8b8b8', gameVersion: 12, generation: 4, platform: 'NDS' },
  pkm_heartgold: { gameId: 'pkm_heartgold', displayName: '宝可梦 心金',     shortName: '心金',     color: '#d4a017', gameVersion: 7,  generation: 4, platform: 'NDS' },
  pkm_soulsilver:{ gameId: 'pkm_soulsilver',displayName: '宝可梦 魂银',     shortName: '魂银',     color: '#8b9dc3', gameVersion: 8,  generation: 4, platform: 'NDS' },

  // ── NDS Gen5 ──
  pkm_white:     { gameId: 'pkm_white',     displayName: '宝可梦 白',       shortName: '白',       color: '#b0b0b0', gameVersion: 20, generation: 5, platform: 'NDS' },
  pkm_black:     { gameId: 'pkm_black',     displayName: '宝可梦 黑',       shortName: '黑',       color: '#1a1a1a', gameVersion: 21, generation: 5, platform: 'NDS' },
  pkm_white2:    { gameId: 'pkm_white2',    displayName: '宝可梦 白2',      shortName: '白2',      color: '#f0e6d3', gameVersion: 22, generation: 5, platform: 'NDS' },
  pkm_black2:    { gameId: 'pkm_black2',    displayName: '宝可梦 黑2',      shortName: '黑2',      color: '#0d2137', gameVersion: 23, generation: 5, platform: 'NDS' },

  // ── 3DS Gen6 ──
  pkm_x:         { gameId: 'pkm_x',         displayName: '宝可梦 X',         shortName: 'X',         color: '#6376b4', gameVersion: 24, generation: 6, platform: '3DS' },
  pkm_y:         { gameId: 'pkm_y',         displayName: '宝可梦 Y',         shortName: 'Y',         color: '#e03a2e', gameVersion: 25, generation: 6, platform: '3DS' },
  pkm_omegaruby: { gameId: 'pkm_omegaruby', displayName: '宝可梦 欧米伽红宝石', shortName: 'Ω红宝石', color: '#cf1322', gameVersion: 26, generation: 6, platform: '3DS' },
  pkm_alphasapphire: { gameId: 'pkm_alphasapphire', displayName: '宝可梦 阿尔法蓝宝石', shortName: 'α蓝宝石', color: '#0958d9', gameVersion: 27, generation: 6, platform: '3DS' },

  // ── 3DS Gen7 ──
  pkm_sun:       { gameId: 'pkm_sun',       displayName: '宝可梦 太阳',     shortName: '太阳',     color: '#fa8c16', gameVersion: 30, generation: 7, platform: '3DS' },
  pkm_moon:      { gameId: 'pkm_moon',      displayName: '宝可梦 月亮',     shortName: '月亮',     color: '#722ed1', gameVersion: 31, generation: 7, platform: '3DS' },
  pkm_ultrasun:  { gameId: 'pkm_ultrasun',  displayName: '宝可梦 究极之日', shortName: '究极日',   color: '#fa541c', gameVersion: 32, generation: 7, platform: '3DS' },
  pkm_ultramoon: { gameId: 'pkm_ultramoon', displayName: '宝可梦 究极之月', shortName: '究极月',   color: '#531dab', gameVersion: 33, generation: 7, platform: '3DS' },

  // ── Switch Gen8 (PKHeX: SW=44, SH=45, PLA=47, BD=48, SP=49) ──
  pkm_sword:            { gameId: 'pkm_sword',            displayName: '宝可梦 剑',       shortName: '剑',       color: '#1677ff', gameVersion: 44, generation: 8, platform: 'Switch' },
  pkm_shield:           { gameId: 'pkm_shield',           displayName: '宝可梦 盾',       shortName: '盾',       color: '#f5222d', gameVersion: 45, generation: 8, platform: 'Switch' },
  pkm_legendsarceus:    { gameId: 'pkm_legendsarceus',    displayName: '宝可梦传说 阿尔宙斯', shortName: '阿尔宙斯', color: '#13c2c2', gameVersion: 47, generation: 8, platform: 'Switch' },
  pkm_brilliantdiamond: { gameId: 'pkm_brilliantdiamond', displayName: '宝可梦 晶灿钻石',  shortName: '晶灿钻石', color: '#5b8bd4', gameVersion: 48, generation: 8, platform: 'Switch' },
  pkm_shiningpearl:     { gameId: 'pkm_shiningpearl',     displayName: '宝可梦 明亮珍珠',  shortName: '明亮珍珠', color: '#e799b0', gameVersion: 49, generation: 8, platform: 'Switch' },
  // ── Switch Gen9 (PKHeX: SL=50, VL=51) ──
  pkm_scarlet:  { gameId: 'pkm_scarlet',  displayName: '宝可梦 朱', shortName: '朱', color: '#fa541c', gameVersion: 50, generation: 9, platform: 'Switch' },
  pkm_violet:   { gameId: 'pkm_violet',   displayName: '宝可梦 紫', shortName: '紫', color: '#722ed1', gameVersion: 51, generation: 9, platform: 'Switch' },
};

// ── 按发行日期排序的可玩游戏列表 ──────────────────────────────────
// 仅暴露当前已接通游玩/本机启动流程的游戏；Switch 元数据保留用于展示，
// 但暂不在工作台开放入口，避免提前暴露未适配能力。

export const PLAYABLE_GAMES: GameMeta[] = [
  GAME_META.pkm_ruby,
  GAME_META.pkm_sapphire,
  GAME_META.pkm_firered,
  GAME_META.pkm_leafgreen,
  GAME_META.pkm_emerald,
  // Gen4
  GAME_META.pkm_diamond,
  GAME_META.pkm_pearl,
  GAME_META.pkm_platinum,
  GAME_META.pkm_heartgold,
  GAME_META.pkm_soulsilver,
  // Gen5
  GAME_META.pkm_black,
  GAME_META.pkm_white,
  GAME_META.pkm_black2,
  GAME_META.pkm_white2,
  // Gen6 (3DS)
  GAME_META.pkm_x,
  GAME_META.pkm_y,
  GAME_META.pkm_omegaruby,
  GAME_META.pkm_alphasapphire,
  // Gen7 (3DS)
  GAME_META.pkm_sun,
  GAME_META.pkm_moon,
  GAME_META.pkm_ultrasun,
  GAME_META.pkm_ultramoon,
];

// ── gameVersion → gameId 映射（含 PKHeX 复合版本兜底） ────────────

export const VERSION_TO_GAME_ID: Record<number, string> = {
  // Gen3
  1: 'pkm_sapphire', 2: 'pkm_ruby', 3: 'pkm_emerald',
  4: 'pkm_firered', 5: 'pkm_leafgreen',
  56: 'pkm_ruby', 57: 'pkm_emerald', 58: 'pkm_firered',  // PKHeX RS/RSE/FRLG composite
  // Gen4
  10: 'pkm_diamond', 11: 'pkm_pearl', 12: 'pkm_platinum',
  7: 'pkm_heartgold', 8: 'pkm_soulsilver',
  62: 'pkm_diamond', 63: 'pkm_platinum', 64: 'pkm_heartgold',  // DP/DPPt/HGSS composite
  // Gen5
  20: 'pkm_white', 21: 'pkm_black', 22: 'pkm_white2', 23: 'pkm_black2',
  66: 'pkm_black', 67: 'pkm_black2',  // BW/B2W2 composite
  // Gen6 (3DS)
  24: 'pkm_x', 25: 'pkm_y', 26: 'pkm_omegaruby', 27: 'pkm_alphasapphire',
  68: 'pkm_x', 69: 'pkm_omegaruby',  // XY/ORAS composite
  // Gen7 (3DS)
  30: 'pkm_sun', 31: 'pkm_moon', 32: 'pkm_ultrasun', 33: 'pkm_ultramoon',
  71: 'pkm_sun', 72: 'pkm_ultrasun',  // SM/USUM composite
  // Gen8 (Switch)
  44: 'pkm_sword', 45: 'pkm_shield', 47: 'pkm_legendsarceus',
  48: 'pkm_brilliantdiamond', 49: 'pkm_shiningpearl',
  74: 'pkm_sword', 75: 'pkm_brilliantdiamond',  // SWSH/BDSP composite
  // Gen9 (Switch)
  50: 'pkm_scarlet', 51: 'pkm_violet',
  76: 'pkm_scarlet',  // SV composite
};

// ── 通过 gameVersion 查找 GameMeta ─────────────────────────────────

export function getGameMetaByVersion(version: number): GameMeta | undefined {
  const gameId = VERSION_TO_GAME_ID[version];
  return gameId ? GAME_META[gameId] : undefined;
}

// ── gameVersion → 显示名称（兼容 GAME_VERSION_DISPLAY 的所有条目） ─

export const GAME_VERSION_DISPLAY: Record<number, { name: string; color: string }> = {
  // GBA Gen3
  1:  { name: '蓝宝石', color: '#0958d9' },
  2:  { name: '红宝石', color: '#cf1322' },
  3:  { name: '绿宝石', color: '#08979c' },
  4:  { name: '火红',   color: '#d4380d' },
  5:  { name: '叶绿',   color: '#389e0d' },
  // NDS Gen4
  7:  { name: '心金',   color: '#d4a017' },
  8:  { name: '魂银',   color: '#8b9dc3' },
  10: { name: '钻石',   color: '#5b8bd4' },
  11: { name: '珍珠',   color: '#e799b0' },
  12: { name: '白金',   color: '#b8b8b8' },
  // NDS Gen5
  20: { name: '白',     color: '#b0b0b0' },
  21: { name: '黑',     color: '#1a1a1a' },
  22: { name: '白2',    color: '#f0e6d3' },
  23: { name: '黑2',    color: '#0d2137' },
  // 3DS Gen6
  24: { name: 'X',      color: '#6376b4' },
  25: { name: 'Y',      color: '#e03a2e' },
  26: { name: '欧米伽红宝石', color: '#cf1322' },
  27: { name: '阿尔法蓝宝石', color: '#0958d9' },
  // 3DS Gen7
  30: { name: '太阳',   color: '#fa8c16' },
  31: { name: '月亮',   color: '#722ed1' },
  32: { name: '究极之日', color: '#fa541c' },
  33: { name: '究极之月', color: '#531dab' },
  34: { name: 'GO',     color: '#52c41a' },
  35: { name: 'GO',     color: '#52c41a' },
  36: { name: '皮卡丘', color: '#fadb14' },
  37: { name: '伊布',   color: '#d48806' },
  // Switch Gen8 (PKHeX: SW=44,SH=45,PLA=47,BD=48,SP=49)
  44: { name: '剑',     color: '#1677ff' },
  45: { name: '盾',     color: '#f5222d' },
  47: { name: '传说 阿尔宙斯', color: '#13c2c2' },
  48: { name: '晶灿钻石', color: '#5b8bd4' },
  49: { name: '明亮珍珠', color: '#e799b0' },
  // Switch Gen9 (PKHeX: SL=50,VL=51)
  50: { name: '朱',     color: '#fa541c' },
  51: { name: '紫',     color: '#722ed1' },
  // PKHeX 复合版本兜底
  56: { name: '红宝石/蓝宝石', color: '#cf1322' },
  57: { name: '绿宝石',       color: '#08979c' },
  58: { name: '火红/叶绿',    color: '#d4380d' },
  62: { name: '珍珠/钻石',    color: '#5b8bd4' },
  63: { name: '白金',         color: '#b8b8b8' },
  64: { name: '心金/魂银',    color: '#d4a017' },
  66: { name: '黑/白',        color: '#1a1a1a' },
  67: { name: '黑2/白2',      color: '#0d2137' },
  68: { name: 'X/Y',          color: '#6376b4' },
  69: { name: 'OR/AS',        color: '#cf1322' },
  71: { name: '太阳/月亮',    color: '#fa8c16' },
  72: { name: '究极之日/月',  color: '#fa541c' },
  73: { name: 'Let\'s Go',    color: '#fadb14' },
  74: { name: '剑/盾',        color: '#1677ff' },
  75: { name: 'BD/SP',        color: '#5b8bd4' },
  76: { name: '朱/紫',        color: '#fa541c' },
};

export const GENERATION_MAP: Record<number, string> = {
  1: 'Gen1 (GB)', 2: 'Gen2 (GBC)', 3: 'Gen3 (GBA)',
  4: 'Gen4 (NDS)', 5: 'Gen5 (NDS)', 6: 'Gen6 (3DS)',
  7: 'Gen7 (3DS)', 8: 'Gen8 (Switch)', 9: 'Gen9 (Switch)',
};
