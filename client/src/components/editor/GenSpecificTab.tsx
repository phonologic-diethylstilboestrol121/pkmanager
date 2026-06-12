import React from 'react';
import { Checkbox, InputNumber, Switch, Tag, Space, Button, Divider } from 'antd';
import type { PokemonDto } from '../../api/saveFile';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
}

// ── Style constants (matching CosmeticTab pattern) ──
const sectionStyle: React.CSSProperties = {
  marginBottom: 18,
  padding: '12px 14px',
  border: '1px solid var(--border-color, #e8e8e8)',
  borderRadius: 8,
  background: 'var(--bg-surface, #fafafa)',
};
const sectionTitle: React.CSSProperties = {
  fontWeight: 600,
  fontSize: 14,
  marginBottom: 10,
  color: 'var(--text-primary, #1a1a1a)',
  display: 'flex',
  alignItems: 'center',
  gap: 6,
};
const labelStyle: React.CSSProperties = {
  fontSize: 12,
  color: 'var(--text-secondary, #8c8c8c)',
  marginRight: 6,
  minWidth: 70,
  display: 'inline-block',
};
const rowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  marginBottom: 8,
  flexWrap: 'wrap' as const,
  gap: 4,
};

// ── Super Training regimen labels (L1-HP through L8-1) ──
const REGIMEN_LABELS: string[] = [
  'L1-HP', 'L1-ATK', 'L1-DEF', 'L1-SPA', 'L1-SPD', 'L1-SPE',
  'L2-HP', 'L2-ATK', 'L2-DEF', 'L2-SPA', 'L2-SPD', 'L2-SPE',
  'L3-HP', 'L3-ATK', 'L3-DEF', 'L3-SPA', 'L3-SPD', 'L3-SPE',
  'L4-1',
  'L5-1', 'L5-2', 'L5-3', 'L5-4',
  'L6-1', 'L6-2', 'L6-3',
  'L7-1', 'L7-2', 'L7-3',
  'L8-1',
];

const DIST_LABELS: string[] = ['Dist1', 'Dist2', 'Dist3', 'Dist4', 'Dist5', 'Dist6'];

const HT_STAT_LABELS: string[] = ['HP', '攻击', '防御', '特攻', '特防', '速度'];

const GenSpecificTab: React.FC<Props> = ({ pokemon, onChange }) => {
  const ch = () => onChange?.();

  // ── ShinyLeaf bitfield helpers ──
  const shinyVal = pokemon.shinyLeaf ?? 0;
  const leafBits = [0, 1, 2, 3, 4].map((i) => !!(shinyVal & (1 << i)));
  const crownBit = !!(shinyVal & 0x20);

  const writeShinyLeaf = (newLeaves: boolean[], newCrown: boolean) => {
    const leafVal = newLeaves.reduce((v, b, j) => v | (b ? (1 << j) : 0), 0);
    const crownVal = newCrown ? 0x20 : 0;
    const reserved = shinyVal & 0xc0; // preserve bits 6-7
    pokemon.shinyLeaf = reserved | crownVal | leafVal;
    ch();
  };

  const toggleLeaf = (i: number) => {
    const next = [...leafBits];
    next[i] = !next[i];
    writeShinyLeaf(next, crownBit);
  };

  const toggleCrown = () => writeShinyLeaf(leafBits, !crownBit);

  // ── Super Training helpers ──
  const regimenFlags = pokemon.superTrainRegimenFlags ?? [];
  const distFlags = pokemon.distSuperTrainFlags ?? [];

  const toggleRegimen = (i: number) => {
    const next = regimenFlags.length === 30 ? [...regimenFlags] : new Array(30).fill(false);
    next[i] = !next[i];
    pokemon.superTrainRegimenFlags = next;
    ch();
  };

  const toggleDist = (i: number) => {
    const next = distFlags.length === 6 ? [...distFlags] : new Array(6).fill(false);
    next[i] = !next[i];
    pokemon.distSuperTrainFlags = next;
    ch();
  };

  // ── Hyper Training helpers ──
  const htFlags = pokemon.hyperTrainFlags ?? [];
  const toggleHT = (i: number) => {
    const next = htFlags.length === 6 ? [...htFlags] : new Array(6).fill(false);
    next[i] = !next[i];
    pokemon.hyperTrainFlags = next;
    ch();
  };

  const setAllHT = (val: boolean) => {
    pokemon.hyperTrainFlags = new Array(6).fill(val);
    ch();
  };

  // ── Conditional visibility ──
  const showShadow = pokemon.isShadow || pokemon.shadowId != null;
  const showShinyLeaf = pokemon.shinyLeaf != null;
  const showGen5 = pokemon.nSparkle != null;
  const showAmie = pokemon.fullness != null;
  const showSuperTrain = pokemon.superTrainingEnabled;
  const showHyperTrain = pokemon.hyperTrainingEnabled;
  const showLGPE = pokemon.combatPower != null || pokemon.spirit != null || pokemon.mood != null;

  const hasAnySection = showShadow || showShinyLeaf || showGen5 || showAmie
    || showSuperTrain || showHyperTrain || showLGPE;

  if (!hasAnySection) {
    return (
      <div style={{ padding: 32, textAlign: 'center', color: 'var(--text-secondary, #8c8c8c)' }}>
        此宝可梦没有世代专属字段可编辑
      </div>
    );
  }

  return (
    <div>
      {/* ──────────── 1. 暗黑宝可梦 (Colosseum/XD) ──────────── */}
      {showShadow && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            🔴 暗黑宝可梦 <Tag color="purple">Colosseum / XD</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>Shadow ID</span>
            <Tag>{pokemon.shadowId != null ? `0x${pokemon.shadowId.toString(16).toUpperCase().padStart(4, '0')}` : '—'}</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>净化心槽</span>
            <InputNumber
              value={pokemon.purification ?? 0}
              onChange={(v) => { pokemon.purification = v ?? 0; ch(); }}
              style={{ width: 120 }}
            />
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>
              心槽计数器（CK3: -100 = 已净化）
            </span>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>暗黑状态</span>
            <Tag color={pokemon.isShadow ? 'red' : 'green'}>{pokemon.isShadow ? '是' : '否'}</Tag>
          </div>
        </div>
      )}

      {/* ──────────── 2. 闪光叶 (HGSS) ──────────── */}
      {showShinyLeaf && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            🍃 闪光叶 <Tag color="gold">HGSS</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>叶片</span>
            <Space size="small">
              {leafBits.map((on, i) => (
                <Checkbox key={i} checked={on} onChange={() => toggleLeaf(i)}>
                  叶{i + 1}
                </Checkbox>
              ))}
            </Space>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>皇冠</span>
            <Checkbox checked={crownBit} onChange={toggleCrown}>
              Shiny Crown
            </Checkbox>
            {crownBit && <Tag color="gold" style={{ marginLeft: 8 }}>👑 皇冠</Tag>}
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>原始值</span>
            <Tag>{`0x${shinyVal.toString(16).toUpperCase().padStart(2, '0')} (${shinyVal})`}</Tag>
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>
              bit0-4=叶片 bit5=皇冠 bit6-7=保留位(写入时保留)
            </span>
          </div>
        </div>
      )}

      {/* ──────────── 3. N的宝可梦 / 电影明星 (Gen5) ──────────── */}
      {showGen5 && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            ⭐ N的宝可梦 / 电影明星 <Tag color="geekblue">Gen5 BW/B2W2</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>N的宝可梦</span>
            <Switch
              checked={pokemon.nSparkle ?? false}
              onChange={(v) => { pokemon.nSparkle = v; ch(); }}
            />
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>
              仅 N 的宝可梦可启用此标志
            </span>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>PokeStar Fame</span>
            <InputNumber
              min={0}
              max={255}
              value={pokemon.pokeStarFame ?? 0}
              onChange={(v) => { pokemon.pokeStarFame = v ?? 0; ch(); }}
              style={{ width: 80 }}
            />
            {pokemon.isPokeStar && (
              <Tag color="orange" style={{ marginLeft: 8 }}>🎬 电影明星</Tag>
            )}
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>
              {'> 250 表示 PokeStar 参与者'}
            </span>
          </div>
        </div>
      )}

      {/* ──────────── 4. Amie 饱腹/愉悦 (Gen6-7) ──────────── */}
      {showAmie && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            🍓 Poké Amie <Tag color="pink">Gen6-7</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>饱腹度</span>
            <InputNumber
              min={0}
              max={255}
              value={pokemon.fullness ?? 0}
              onChange={(v) => { pokemon.fullness = v ?? 0; ch(); }}
              style={{ width: 80 }}
            />
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>愉悦度</span>
            <InputNumber
              min={0}
              max={255}
              value={pokemon.enjoyment ?? 0}
              onChange={(v) => { pokemon.enjoyment = v ?? 0; ch(); }}
              style={{ width: 80 }}
            />
          </div>
        </div>
      )}

      {/* ──────────── 5. 超级训练 (Gen6-7) ──────────── */}
      {showSuperTrain && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            🏋️ 超级训练 <Tag color="cyan">Gen6-7</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>秘密特训解锁</span>
            <Switch
              checked={pokemon.secretSuperTrainingUnlocked ?? false}
              onChange={(v) => { pokemon.secretSuperTrainingUnlocked = v; ch(); }}
            />
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>完成全部特训</span>
            <Tag color={pokemon.superTrainSupremelyTrained ? 'green' : 'default'}>
              {pokemon.superTrainSupremelyTrained ? '是' : '否'}
            </Tag>
          </div>

          <Divider style={{ margin: '8px 0' }} />

          <div style={{ fontWeight: 500, fontSize: 13, marginBottom: 6 }}>训练项目 (Regimen 1-8)</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 4 }}>
            {REGIMEN_LABELS.map((label, i) => (
              <Checkbox
                key={i}
                checked={regimenFlags[i] ?? false}
                onChange={() => toggleRegimen(i)}
                style={{ fontSize: 11, marginLeft: 0 }}
              >
                {label}
              </Checkbox>
            ))}
          </div>

          <Divider style={{ margin: '8px 0' }} />
          <div style={{ fontWeight: 500, fontSize: 13, marginBottom: 6 }}>Distribution 训练</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 4 }}>
            {DIST_LABELS.map((label, i) => (
              <Checkbox
                key={i}
                checked={distFlags[i] ?? false}
                onChange={() => toggleDist(i)}
                style={{ fontSize: 11, marginLeft: 0 }}
              >
                {label}
              </Checkbox>
            ))}
          </div>
        </div>
      )}

      {/* ──────────── 6. 极限特训 (Gen7) ──────────── */}
      {showHyperTrain && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            💪 极限特训 <Tag color="orange">Gen7+</Tag>
          </div>
          <div style={{ marginBottom: 8 }}>
            <Space size="small">
              <Button size="small" onClick={() => setAllHT(true)}>全部开启</Button>
              <Button size="small" onClick={() => setAllHT(false)}>全部清除</Button>
            </Space>
          </div>
          <Space size="middle">
            {HT_STAT_LABELS.map((label, i) => (
              <Checkbox
                key={i}
                checked={htFlags[i] ?? false}
                onChange={() => toggleHT(i)}
              >
                {label}
              </Checkbox>
            ))}
          </Space>
          <div style={{ fontSize: 11, color: '#8c8c8c', marginTop: 6 }}>
            Gen7 需 Lv.100 方可进行极限特训
          </div>
        </div>
      )}

      {/* ──────────── 7. LGPE 战力/精神/心情 ──────────── */}
      {showLGPE && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>
            ⚡ LGPE 专属 <Tag color="lime">Let's Go Pikachu/Eevee</Tag>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>Combat Power</span>
            <InputNumber
              min={0}
              value={pokemon.combatPower ?? 0}
              onChange={(v) => { pokemon.combatPower = v ?? 0; ch(); }}
              style={{ width: 100 }}
            />
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>精神 (Spirit)</span>
            <InputNumber
              min={0}
              max={255}
              value={pokemon.spirit ?? 100}
              onChange={(v) => { pokemon.spirit = v ?? 100; ch(); }}
              style={{ width: 80 }}
            />
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>默认值 100</span>
          </div>
          <div style={rowStyle}>
            <span style={labelStyle}>心情 (Mood)</span>
            <InputNumber
              min={0}
              max={255}
              value={pokemon.mood ?? 100}
              onChange={(v) => { pokemon.mood = v ?? 100; ch(); }}
              style={{ width: 80 }}
            />
            <span style={{ fontSize: 11, color: '#8c8c8c', marginLeft: 8 }}>默认值 100</span>
          </div>
        </div>
      )}
    </div>
  );
};

export default GenSpecificTab;
