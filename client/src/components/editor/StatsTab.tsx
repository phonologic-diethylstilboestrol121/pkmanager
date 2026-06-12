import React from 'react';
import { InputNumber, Tag, Space, Switch } from 'antd';
import type { PokemonDto } from '../../api/saveFile';
import StatsRadar from './StatsRadar';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
}

const STAT_LABELS = ['HP', '攻击', '防御', '特攻', '特防', '速度'];

const HIDDEN_POWER_TYPES = [
  '格斗', '飞行', '毒', '地面', '岩石', '虫', '幽灵', '钢',
  '火', '水', '草', '电', '超能力', '冰', '龙', '恶',
];

const StatsTab: React.FC<Props> = ({ pokemon, generation, onChange }) => {
  const g = generation;
  const evs = pokemon.evs || [0,0,0,0,0,0];
  const ivs = pokemon.ivs || [0,0,0,0,0,0];
  const base = pokemon.baseStats || [0,0,0,0,0,0];
  const calc = pokemon.calculatedStats || [0,0,0,0,0,0];
  const isGen12 = g <= 2;
  const isGen8Plus = g >= 8;
  const isGen9Plus = g >= 9;
  const maxIV = isGen12 ? 15 : 31;

  const ivTotal = ivs.reduce((s, v) => s + v, 0);
  const evTotal = evs.reduce((s, v) => s + v, 0);
  const baseTotal = base.reduce((s, v) => s + v, 0);
  const calcTotal = calc.reduce((s, v) => s + v, 0);

  const handleIVChange = (i: number, val: number | null) => {
    if (val !== null && pokemon.ivs) {
      pokemon.ivs[i] = Math.min(maxIV, Math.max(0, val));
      onChange?.();
    }
  };

  const handleEVChange = (i: number, val: number | null) => {
    if (val !== null && pokemon.evs) {
      pokemon.evs[i] = Math.min(252, Math.max(0, val));
      onChange?.();
    }
  };

  return (
    <div>
      {/* ── 种族值雷达图 + 摘要 ── */}
      <div style={{
        display: 'flex', alignItems: 'flex-start', gap: 16, flexWrap: 'wrap',
        marginBottom: 12,
      }}>
        <div style={{ flexShrink: 0 }}>
          <StatsRadar values={base} maxValue={255} title="种族值雷达图" />
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, minWidth: 160 }}>
          <Tag color="purple" style={{ fontSize: 13, padding: '2px 10px' }}>
            觉醒力量: {HIDDEN_POWER_TYPES[pokemon.hiddenPowerType] || `类型${pokemon.hiddenPowerType}`}
          </Tag>
          <Tag color={evTotal > 510 ? 'red' : evTotal === 510 ? 'green' : 'blue'}>
            努力值: {evTotal} / 510
          </Tag>
          <Tag color="default">
            个体值: {ivTotal} / {maxIV * 6}
          </Tag>
        </div>
      </div>

      {/* Stat Table — PKHeX style */}
      <table style={{
        width: '100%', borderCollapse: 'collapse', fontSize: 13,
        border: '1px solid #e8e8e8', borderRadius: 6, overflow: 'hidden',
      }}>
        <thead>
          <tr style={{ background: '#fafafa' }}>
            <th style={thStyle}>能力</th>
            <th style={thStyle}>种族值</th>
            <th style={thStyle}>个体值</th>
            <th style={thStyle}>努力值</th>
            <th style={{ ...thStyle, background: '#e6f7ff', fontWeight: 700 }}>能力值</th>
          </tr>
        </thead>
        <tbody>
          {STAT_LABELS.map((label, i) => {
            return (
              <tr key={label} style={{ borderTop: '1px solid #f0f0f0' }}>
                <td style={{ ...tdStyle, fontWeight: 600 }}>{label}</td>
                <td style={{ ...tdStyle, color: '#8c8c8c' }}>{base[i]}</td>
                <td style={tdStyle}>
                  <InputNumber
                    size="small"
                    min={0} max={maxIV}
                    value={ivs[i]}
                    onChange={(v) => handleIVChange(i, v)}
                    style={{ width: 65 }}
                  />
                </td>
                <td style={tdStyle}>
                  <InputNumber
                    size="small"
                    min={0} max={252}
                    value={isGen12 ? 0 : evs[i]}
                    disabled={isGen12}
                    onChange={(v) => handleEVChange(i, v)}
                    style={{ width: 65 }}
                  />
                </td>
                <td style={{
                  ...tdStyle, background: '#f6ffed', fontWeight: 700, fontSize: 14,
                  color: i === 0 ? '#389e0d' : 'inherit',
                }}>
                  {calc[i] || 0}
                </td>
              </tr>
            );
          })}
        </tbody>
        {/* Totals row */}
        <tfoot>
          <tr style={{ borderTop: '2px solid #d9d9d9', background: '#fafafa' }}>
            <td style={{ ...tdStyle, fontWeight: 700 }}>合计</td>
            <td style={{ ...tdStyle, fontWeight: 600, color: '#595959' }}>{baseTotal}</td>
            <td style={{ ...tdStyle, fontWeight: 600 }}>{ivTotal}</td>
            <td style={{ ...tdStyle, fontWeight: 600 }}>{evTotal}</td>
            <td style={{ ...tdStyle, fontWeight: 700, color: '#389e0d' }}>{calcTotal}</td>
          </tr>
        </tfoot>
      </table>

      {/* Gen-specific stats */}
      {g < 8 && pokemon.avs && pokemon.avs.some(v => v > 0) && (
        <div style={{ marginTop: 12 }}>
          <div style={sectionTitle}>觉醒值 (AVs — LGPE)</div>
          <Space>
            {pokemon.avs.map((v, i) => (
              <span key={i}>
                <span style={{ fontSize: 11, marginRight: 2 }}>{STAT_LABELS[i]}</span>
                <InputNumber size="small" min={0} max={200} value={v}
                  onChange={(val) => { if (pokemon.avs && val !== null) { pokemon.avs[i] = val; onChange?.(); } }}
                  style={{ width: 55 }} />
              </span>
            ))}
          </Space>
        </div>
      )}

      {g >= 8 && pokemon.gvs && pokemon.gvs.some(v => v > 0) && (
        <div style={{ marginTop: 12 }}>
          <div style={sectionTitle}>奋斗值 (GVs — LA)</div>
          <Space>
            {pokemon.gvs.map((v, i) => (
              <span key={i}>
                <span style={{ fontSize: 11, marginRight: 2 }}>{STAT_LABELS[i]}</span>
                <InputNumber size="small" min={0} max={10} value={v}
                  onChange={(val) => { if (pokemon.gvs && val !== null) { pokemon.gvs[i] = val; onChange?.(); } }}
                  style={{ width: 55 }} />
              </span>
            ))}
          </Space>
        </div>
      )}

      {isGen8Plus && (
        <div style={{ marginTop: 12 }}>
          <div style={sectionTitle}>扩展属性</div>
          <Space wrap>
            <span>极巨化等级 <InputNumber size="small" min={0} max={10} value={pokemon.dynamaxLevel || 0}
              onChange={(v) => { if (v !== null) { pokemon.dynamaxLevel = v; onChange?.(); } }} /></span>
            <span>超极巨化 <Switch checked={pokemon.canGigantamax} size="small"
              onChange={(v) => { pokemon.canGigantamax = v; onChange?.(); }} /></span>
            <span>头目 <Switch checked={pokemon.isAlpha} size="small"
              onChange={(v) => { pokemon.isAlpha = v; onChange?.(); }} /></span>
          </Space>
        </div>
      )}

      {isGen9Plus && (
        <div style={{ marginTop: 8 }}>
          <span>太晶属性: 原始 </span>
          <InputNumber size="small" min={0} max={18} value={pokemon.teraTypeOriginal || 0}
            onChange={(v) => { if (v !== null) { pokemon.teraTypeOriginal = v; onChange?.(); } }} style={{ width: 55 }} />
          <span style={{ marginLeft: 8 }}>覆盖 </span>
          <InputNumber size="small" min={0} max={18} value={pokemon.teraTypeOverride || 0}
            onChange={(v) => { if (v !== null) { pokemon.teraTypeOverride = v; onChange?.(); } }} style={{ width: 55 }} />
        </div>
      )}
    </div>
  );
};

const thStyle: React.CSSProperties = {
  padding: '8px 12px', textAlign: 'center', fontWeight: 600,
  borderBottom: '2px solid #e8e8e8', fontSize: 12,
};

const tdStyle: React.CSSProperties = {
  padding: '6px 12px', textAlign: 'center',
};

const sectionTitle: React.CSSProperties = {
  fontWeight: 500, marginBottom: 6, fontSize: 12, color: '#666',
};

export default StatsTab;
