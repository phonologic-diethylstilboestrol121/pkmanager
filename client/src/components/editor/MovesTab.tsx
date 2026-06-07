import React, { useEffect, useState } from 'react';
import { Select, InputNumber, Tag, Space } from 'antd';
import type { PokemonDto } from '../../api/saveFile';
import { useResourceStore } from '../../stores/resourceStore';
import { resourceApi, type ResourceItem } from '../../api/resource';
import { useDiagnosticStore } from '../../stores/diagnosticStore';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
}

const MOVE_CATEGORY_ICONS = ['🔄', '⚔️', '🔮'];
const labelStyle: React.CSSProperties = { fontSize: 11, color: '#8c8c8c', marginBottom: 2 };

const MovesTab: React.FC<Props> = ({ pokemon, generation, onChange }) => {
  const { moves } = useResourceStore();
  const isGen6Plus = generation >= 6;
  const ch = () => onChange?.();

  const [speciesMoves, setSpeciesMoves] = useState<ResourceItem[]>([]);
  useEffect(() => {
    if (pokemon.species > 0) {
      resourceApi.speciesMoves(pokemon.species, generation, pokemon.form).then(res => {
        setSpeciesMoves(res.data || []);
      }).catch((err: any) => {
        setSpeciesMoves([]);
        useDiagnosticStore.getState().log({
          category: 'api', level: 'error',
          message: `加载招式列表失败 (species=${pokemon.species})`,
          stack: err?.message,
        });
      });
    } else {
      setSpeciesMoves([]);
    }
  }, [pokemon.species, pokemon.form, generation]);

  const baseMoves = speciesMoves.length > 0 ? speciesMoves : (moves || []);
  const moveOptions = [{ value: 0, label: '— 无 —' }, ...baseMoves.map(m => ({ value: m.id, label: m.name }))];

  const movesArr = pokemon.moves || [];
  const ppArr = pokemon.movePPs || [0,0,0,0];
  const ppUpArr = pokemon.movePPUps || [0,0,0,0];

  return (
    <div>
      <div style={{ fontWeight: 500, marginBottom: 8 }}>当前招式</div>
      {[0,1,2,3].map(i => {
        const m = movesArr[i] || { moveId: 0, moveName: '', moveType: 0, moveTypeName: '', moveCategory: 0, basePP: 0 };
        return (
          <div key={i} style={{ marginBottom: 10, padding: 8, background: '#fafafa', borderRadius: 6, border: '1px solid #f0f0f0' }}>
            <Space style={{ width: '100%' }}>
              <div style={{ flex: 1 }}>
                <div style={labelStyle}>招式 {i + 1}</div>
                <Select size="small" showSearch allowClear placeholder="选择招式" style={{ minWidth: 180 }}
                  value={m.moveId || undefined}
                  options={moveOptions}
                  onChange={(v) => { if (pokemon.moves) { pokemon.moves[i] = { ...m, moveId: v || 0 }; ch(); } }}
                  filterOption={(input, option) =>
                    (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
                  }
                />
              </div>
              {m.moveId > 0 && (
                <>
                  {m.moveTypeName && <Tag color="blue" style={{ fontSize: 11, marginTop: 14 }}>{m.moveTypeName}</Tag>}
                  <span style={{ fontSize: 12, marginTop: 14 }}>{MOVE_CATEGORY_ICONS[m.moveCategory] || ''}</span>
                </>
              )}
            </Space>
            {m.moveId > 0 && (
              <div style={{ marginTop: 4, display: 'flex', gap: 8, alignItems: 'center', fontSize: 12, color: '#666' }}>
                <span>PP:
                  <InputNumber size="small" min={0} max={m.basePP} style={{ width: 55 }}
                    value={ppArr[i]} onChange={(v) => {
                      if (pokemon.movePPs) { pokemon.movePPs[i] = v ?? 0; ch(); }
                    }} />
                </span>
                <span>UP:
                  <InputNumber size="small" min={0} max={3} style={{ width: 50 }}
                    value={ppUpArr[i]} onChange={(v) => {
                      if (pokemon.movePPUps) { pokemon.movePPUps[i] = v ?? 0; ch(); }
                    }} />
                </span>
                {m.basePP > 0 && (
                  <span>Max PP: {Math.floor(m.basePP + (m.basePP * ppUpArr[i]) / 5)}</span>
                )}
              </div>
            )}
          </div>
        );
      })}

      {isGen6Plus && pokemon.relearnMoves && (
        <div style={{ marginTop: 16 }}>
          <div style={{ fontWeight: 500, marginBottom: 8 }}>回忆招式 (Relearn Moves)</div>
          {pokemon.relearnMoves.map((rmId, i) => (
            <div key={i} style={{ marginBottom: 8 }}>
              <div style={labelStyle}>回忆 {i + 1}</div>
              <Select size="small" showSearch allowClear placeholder="选择招式" style={{ minWidth: 200 }}
                value={rmId || undefined}
                options={moveOptions}
                onChange={(v) => {
                  if (pokemon.relearnMoves) { pokemon.relearnMoves[i] = v || 0; ch(); }
                }}
                filterOption={(input, option) =>
                  (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
                }
              />
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default MovesTab;
