import React, { useState } from 'react';
import { Tag, Typography } from 'antd';
import { PlayCircleOutlined } from '@ant-design/icons';
import { GAME_META, type GameMeta, getGameMetaByVersion, type GamePlatform } from '../constants/games';

const { Text } = Typography;

const PLATFORM_LABELS: Record<GamePlatform, string> = {
  GBA: 'GBA', NDS: 'NDS', '3DS': '3DS', Switch: 'NS',
};

const PLATFORM_COLORS: Record<GamePlatform, string> = {
  GBA: '#722ed1', NDS: '#1677ff', '3DS': '#fa541c', Switch: '#eb2f96',
};

// ── Poké Ball SVG placeholder ─────────────────────────────────────

const PokeballPlaceholder: React.FC<{ color: string; size: number }> = ({ color, size }) => (
  <svg width={size} height={size} viewBox="0 0 100 100" fill="none"
    style={{ opacity: 0.5 }}>
    <circle cx="50" cy="50" r="46" stroke={color} strokeWidth="4" fill="none" />
    <path d="M4 50 H96" stroke={color} strokeWidth="4" />
    <rect x="50" y="50" width="4" height="4" fill={color} transform="translate(-16,-16)" />
    <circle cx="50" cy="50" r="14" fill={color} opacity="0.3" stroke={color} strokeWidth="4" />
  </svg>
);

// ── GameCover Component ────────────────────────────────────────────

interface GameCoverProps {
  /** gameId string (pkm_ruby, pkm_platinum...) */
  gameId?: string;
  /** fallback: gameVersion number, used to look up gameId if not provided */
  gameVersion?: number;
  /** Display size */
  size?: 'small' | 'medium' | 'large';
  /** Show platform badge */
  showPlatform?: boolean;
  /** Custom style */
  style?: React.CSSProperties;
}

const GameCover: React.FC<GameCoverProps> = ({
  gameId, gameVersion, size = 'medium', showPlatform = true, style,
}) => {
  const [imgError, setImgError] = useState(false);

  // Resolve meta
  const meta: GameMeta | undefined = gameId
    ? GAME_META[gameId]
    : gameVersion !== undefined
      ? getGameMetaByVersion(gameVersion)
      : undefined;

  const coverPath = meta ? `${import.meta.env.BASE_URL}assets/covers/${meta.gameId}.png` : undefined;
  const hasCustomCover = coverPath && !imgError;

  // Size presets
  const dims = size === 'small' ? { img: 28, fontSize: 8, minWidth: 40, minHeight: 40, padding: 4 } :
               size === 'large' ? { img: 56, fontSize: 13, minWidth: 160, minHeight: 160, padding: 16 } :
               { img: 48, fontSize: 12, minWidth: 140, minHeight: 140, padding: 12 };

  const containerStyle: React.CSSProperties = {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    minWidth: dims.minWidth,
    minHeight: dims.minHeight,
    padding: dims.padding,
    borderRadius: 8,
    overflow: 'hidden',
    position: 'relative',
    ...style,
  };

  // ── Has custom cover image ─────────────────────────────────

  if (hasCustomCover && meta) {
    return (
      <div style={containerStyle}>
        <img
          src={coverPath}
          alt={meta.displayName}
          style={{
            width: '100%',
            height: '100%',
            objectFit: 'contain',
            borderRadius: 6,
          }}
          onError={() => setImgError(true)}
        />
      </div>
    );
  }

  // ── Colored placeholder card ───────────────────────────────

  if (meta) {
    const bgColor = meta.color + '18'; // 10% opacity
    return (
      <div style={{
        ...containerStyle,
        background: bgColor,
        border: `2px solid ${meta.color}40`,
        gap: size === 'small' ? 2 : 6,
      }}>
        {/* Poké Ball */}
        <PokeballPlaceholder color={meta.color} size={dims.img} />

        {/* Game name */}
        {size !== 'small' && (
          <Text
            strong
            style={{
              fontSize: dims.fontSize,
              color: meta.color,
              textAlign: 'center',
              lineHeight: 1.2,
            }}
          >
            {meta.shortName}
          </Text>
        )}

        {/* Platform badge */}
        {showPlatform && (
          <Tag
            color={PLATFORM_COLORS[meta.platform]}
            style={{
              fontSize: size === 'small' ? 8 : 10,
              margin: 0,
              padding: '0 4px',
              lineHeight: size === 'small' ? '12px' : '16px',
            }}
          >
            {PLATFORM_LABELS[meta.platform]}
          </Tag>
        )}
      </div>
    );
  }

  // ── Unknown game — generic placeholder ──────────────────────

  return (
    <div style={{
      ...containerStyle,
      background: '#fafafa',
      border: '2px dashed #d9d9d9',
    }}>
      <PlayCircleOutlined style={{ fontSize: dims.img, color: '#bbb' }} />
      {size !== 'small' && (
        <Text type="secondary" style={{ fontSize: dims.fontSize }}>
          {gameVersion ? `Gen ${gameVersion}` : '?'}
        </Text>
      )}
    </div>
  );
};

export default GameCover;
