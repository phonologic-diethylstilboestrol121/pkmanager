// ── 宝可梦精灵图 URL resolver ──────────────────────────────────
// 统一收口所有精灵图 URL，方便切换图源或部署基路径。

const BASE = import.meta.env.BASE_URL; // 适配部署基路径，dev 环境为 '/'

/** 标准 96×96 精灵图本地路径 */
export function getPokemonSpriteUrl(speciesId: number): string {
  return `${BASE}assets/sprites/pokemon/${speciesId}.png`;
}

/** 远端回退 URL — PokeAPI standard sprite（jsdelivr CDN） */
export function getPokeApiSpriteUrl(speciesId: number): string {
  return `https://gcore.jsdelivr.net/gh/PokeAPI/sprites@master/sprites/pokemon/${speciesId}.png`;
}

/** 远端 official-artwork URL（Phase 1 无本地，直接走远端，jsdelivr CDN） */
export function getPokeApiArtworkUrl(speciesId: number): string {
  return `https://gcore.jsdelivr.net/gh/PokeAPI/sprites@master/sprites/pokemon/other/official-artwork/${speciesId}.png`;
}
