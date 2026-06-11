// ── PokemonSprite Component ─────────────────────────────────────
// 统一宝可梦精灵图展示，支持 Game 和 Home 两种风格。
//
// Game (默认): 本地 96×96 → jsdelivr CDN 标准图 → SVG 占位
// Home:       jsdelivr CDN Home 渲染图(256×256) → Game 本地 → Game CDN → SVG 占位
//
// variant 改变时通过 key 强制重挂载，保证回退阶段 state 完全重置。
// imageRendering: Home 模式自动走浏览器平滑缩放，Game 模式保留 pixelated。

import React, { useState } from 'react';
import { getPokemonSpriteUrl, getPokeApiSpriteUrl, getHomeSpriteUrl, type SpriteStyle } from '../lib/spriteUrl';

interface PokemonSpriteProps {
  speciesId: number;
  /** 精灵图风格: 'game' (像素风) | 'home' (3D 渲染) */
  variant?: SpriteStyle;
  /** 透传常见 img 属性 */
  alt?: string;
  width?: number;
  height?: number;
  style?: React.CSSProperties;
  className?: string;
}

/** 内联 SVG 占位 — 浅灰底 + 物种编号 */
function placeholderSvg(speciesId: number, w: number, h: number): string {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}">`
    + `<rect fill="%23f0f0f0" width="${w}" height="${h}" rx="4"/>`
    + `<text x="${w / 2}" y="${h / 2}" text-anchor="middle" dy=".3em" fill="%23999" font-size="${Math.max(8, w / 5)}">${speciesId}</text>`
    + `</svg>`;
  return 'data:image/svg+xml,' + encodeURIComponent(svg);
}

// ── Inner component — stateful, re-mounts when key changes ─────────

const PokemonSpriteInner: React.FC<PokemonSpriteProps> = ({
  speciesId, variant = 'game', alt, width, height, style, className,
}) => {
  // Default size: Home 图原生 256×256 可以安全缩放
  const w = width ?? (variant === 'home' ? 64 : 32);
  const h = height ?? (variant === 'home' ? 64 : 32);

  // Stage 0 = primary (local game / home CDN), 1 = PokeAPI CDN game sprite, 2 = SVG, 3 = terminal
  const [stage, setStage] = useState<0 | 1 | 2 | 3>(0);

  const src =
    stage === 0
      ? variant === 'home'
        ? getHomeSpriteUrl(speciesId)      // Home: CDN first (no local copy)
        : getPokemonSpriteUrl(speciesId)   // Game: local first
    : stage === 1
      ? getPokeApiSpriteUrl(speciesId)     // Both fall back to standard sprite CDN
    : placeholderSvg(speciesId, w, h);

  const handleError = stage < 2
    ? () => setStage((s) => (s + 1) as 0 | 1 | 2 | 3)
    : undefined; // Stage 2+ — 移除 handler，杜绝死循环

  // imageRendering: Home 图用浏览器默认平滑缩放，Game 图用 pixelated 保留像素风格
  // 调用方传入的 style 覆盖此默认值
  const defaultStyle: React.CSSProperties = variant === 'home'
    ? {}
    : { imageRendering: 'pixelated' as React.CSSProperties['imageRendering'] };

  return (
    <img
      src={src}
      alt={alt ?? `#${speciesId}`}
      width={w}
      height={h}
      style={{ ...defaultStyle, ...style }}
      className={className}
      onError={handleError}
    />
  );
};

// ── Outer component — key={speciesId}-{variant} drives clean remount ─
// variant 纳入 key 确保切换风格时 stage state 完全重置（不会残留旧回退阶段）
const PokemonSprite: React.FC<PokemonSpriteProps> = (props) => (
  <PokemonSpriteInner key={`${props.speciesId}-${props.variant ?? 'game'}`} {...props} />
);

export default PokemonSprite;
