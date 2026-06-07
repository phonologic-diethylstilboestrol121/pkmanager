import React, { useEffect, useState } from 'react';
import { Select, Input, InputNumber, Switch, Space, Tag } from 'antd';
import type { PokemonDto } from '../../api/saveFile';
import { useResourceStore } from '../../stores/resourceStore';
import { resourceApi, type ResourceItem } from '../../api/resource';
import { useDiagnosticStore } from '../../stores/diagnosticStore';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
}

const LANGUAGES = [
  { value: 1, label: '日本語' }, { value: 2, label: 'English' },
  { value: 3, label: 'Français' }, { value: 4, label: 'Italiano' },
  { value: 5, label: 'Deutsch' }, { value: 7, label: 'Español' },
  { value: 8, label: '한국어' }, { value: 9, label: '简体中文' },
  { value: 10, label: '繁體中文' },
];

const labelStyle: React.CSSProperties = { fontSize: 11, color: '#8c8c8c', marginBottom: 2 };

const getLevelFromExp = (exp: number, table: number[]) => {
  if (table.length === 0) return 1;
  if (exp >= table[table.length - 1]) return 100;
  let level = 1;
  while (level < table.length && exp >= table[level]) level += 1;
  return level;
};

const MainTab: React.FC<Props> = ({ pokemon, generation, onChange }) => {
  const { species, abilities, natures, items, balls } = useResourceStore();
  const isGen12 = generation <= 2;
  const isGen8Plus = generation >= 8;
  const ch = () => onChange?.();

  const [speciesAbilities, setSpeciesAbilities] = useState<ResourceItem[]>([]);
  const [expTable, setExpTable] = useState<number[]>([]);
  useEffect(() => {
    if (pokemon.species > 0) {
      resourceApi.speciesAbilities(pokemon.species, generation, pokemon.form).then(res => {
        setSpeciesAbilities(res.data || []);
      }).catch((err: any) => {
        setSpeciesAbilities([]);
        useDiagnosticStore.getState().log({
          category: 'api', level: 'error',
          message: `加载物种特性失败 (species=${pokemon.species})`,
          stack: err?.message,
        });
      });

      resourceApi.speciesExperience(pokemon.species, generation, pokemon.form).then(res => {
        setExpTable(res.data?.expTable || []);
      }).catch((err: any) => {
        setExpTable([]);
        useDiagnosticStore.getState().log({
          category: 'api', level: 'error',
          message: `加载经验成长表失败 (species=${pokemon.species})`,
          stack: err?.message,
        });
      });
    } else {
      setSpeciesAbilities([]);
      setExpTable([]);
    }
  }, [pokemon.species, pokemon.form, generation]);

  const abilityOptions = (speciesAbilities.length > 0 ? speciesAbilities : abilities)
    .map((a, i) => ({ value: a.id, label: a.name, key: `${a.id}_${i}` }));
  const itemOptions = [{ value: 0, label: '无' }, ...items.filter(i => i.id > 0).map(i => ({ value: i.id, label: i.name }))];
  const ballOptions = balls.map(b => ({ value: b.id, label: b.name }));

  const natureName = natures.find(n => n.id === pokemon.nature)?.name;
  const natureMod = natureName ? natureModifiers[natureName] : null;

  const set = (key: keyof PokemonDto, val: any) => { (pokemon as any)[key] = val; ch(); };

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between' }}>
        <div style={{ flex: 1 }}>
          <div style={labelStyle}>物种</div>
          <Select showSearch size="small" value={pokemon.species}
            onChange={(v) => { pokemon.species = v; ch(); }}
            filterOption={(input, option) =>
              (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
            }>
            {species.map(s => (
              <Select.Option key={s.id} value={s.id}>{s.name} (#{s.id})</Select.Option>
            ))}
          </Select>
        </div>
        <div>
          <div style={labelStyle}>形态</div>
          <Space.Compact>
            <InputNumber size="small" min={0} max={63} value={pokemon.form} style={{ width: 70 }}
              onChange={(v) => set('form', v ?? 0)} />
            <Tag>{pokemon.formName || (pokemon.form > 0 ? `F${pokemon.form}` : '默认')}</Tag>
          </Space.Compact>
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div>
          <div style={labelStyle}>昵称</div>
          <Space.Compact>
            <Switch size="small" checked={pokemon.isNicknamed}
              onChange={(v) => {
                pokemon.isNicknamed = v;
                if (!v) pokemon.nickname = pokemon.speciesName;
                ch();
              }} />
            <Input size="small" maxLength={12}
              value={pokemon.isNicknamed ? (pokemon.nickname || '') : pokemon.speciesName}
              disabled={!pokemon.isNicknamed}
              style={{ width: 120 }}
              onChange={(e) => { pokemon.nickname = e.target.value; ch(); }} />
          </Space.Compact>
        </div>
        <div>
          <div style={labelStyle}>语言</div>
          <Select size="small" value={pokemon.language} options={LANGUAGES} style={{ width: 110 }}
            onChange={(v) => set('language', v)} disabled={isGen12} />
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>等级</div>
          <InputNumber size="small" min={1} max={100} value={pokemon.level} style={{ width: 75 }}
            onChange={(v) => {
              const level = v ?? 1;
              pokemon.level = level;
              if (expTable.length >= level)
                pokemon.exp = expTable[Math.max(0, level - 1)];
              ch();
            }} /></div>
        <div><div style={labelStyle}>EXP</div>
          <InputNumber size="small" min={0} value={pokemon.exp} style={{ width: 110 }}
            onChange={(v) => {
              const exp = v ?? 0;
              pokemon.exp = exp;
              if (expTable.length > 0)
                pokemon.level = getLevelFromExp(exp, expTable);
              ch();
            }} /></div>
        <div><div style={labelStyle}>亲密度</div>
          <InputNumber size="small" min={0} max={255} value={pokemon.originalTrainerFriendship} style={{ width: 80 }}
            onChange={(v) => set('originalTrainerFriendship', v ?? 0)} /></div>
        {isGen8Plus && (
          <div><div style={labelStyle}>HT亲密度</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.handlingTrainerFriendship} style={{ width: 80 }}
              onChange={(v) => set('handlingTrainerFriendship', v ?? 0)} /></div>
        )}
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }} wrap>
        <div>
          <div style={labelStyle}>性格</div>
          <Select size="small" showSearch value={pokemon.nature} style={{ width: 130 }}
            onChange={(v) => { pokemon.nature = v as number; ch(); }}
            filterOption={(input, option) =>
              (option?.label as string)?.includes(input)
            }>
            {natures.map(n => (
              <Select.Option key={n.id} value={n.id}>{n.name}</Select.Option>
            ))}
          </Select>
        </div>
        {natureMod
          ? <><Tag color="red" style={{ fontSize: 11, marginTop: 18 }}>↑{natureMod.up}</Tag>
             <Tag color="blue" style={{ fontSize: 11, marginTop: 18 }}>↓{natureMod.down}</Tag></>
          : <Tag color="default" style={{ fontSize: 11, marginTop: 18 }}>—</Tag>
        }
        <div>
          <div style={labelStyle}>特性</div>
          <Select size="small" showSearch value={pokemon.ability} style={{ width: 180 }}
            onChange={(v) => set('ability', v)} disabled={isGen12}>
            <Select.Option value={0} key="abi_0">— (默认)</Select.Option>
            {abilityOptions.map((a) => (
              <Select.Option value={a.value} key={`abi_${a.key}`}>{a.label}</Select.Option>
            ))}
          </Select>
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>性别</div>
          <Select size="small" value={pokemon.gender} style={{ width: 100 }}
            onChange={(v) => set('gender', v)}
            options={[{ value: 0, label: '♂' }, { value: 1, label: '♀' }, { value: 2, label: '无' }]} /></div>
        <div><div style={labelStyle}>闪光</div>
          <Switch size="small" checked={pokemon.isShiny} onChange={(v) => set('isShiny', v)} /></div>
        <div><div style={labelStyle}>蛋</div>
          <Switch size="small" checked={pokemon.isEgg} onChange={(v) => set('isEgg', v)} /></div>
        <div><div style={labelStyle}>命运邂逅</div>
          <Switch size="small" checked={pokemon.fatefulEncounter} onChange={(v) => set('fatefulEncounter', v)} /></div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div>
          <div style={labelStyle}>持有道具</div>
          <Select size="small" showSearch value={pokemon.heldItem} options={itemOptions}
            style={{ width: 180 }} disabled={isGen12}
            onChange={(v) => set('heldItem', v)}
            filterOption={(input, option) =>
              (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
        </div>
        <div>
          <div style={labelStyle}>精灵球</div>
          <Select size="small" value={pokemon.ball} options={ballOptions} style={{ width: 130 }}
            onChange={(v) => set('ball', v)} disabled={isGen12} />
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>病毒株</div>
          <InputNumber size="small" min={0} max={15} value={pokemon.pokerusStrain} style={{ width: 65 }}
            onChange={(v) => set('pokerusStrain', v ?? 0)} /></div>
        <div><div style={labelStyle}>感染天数</div>
          <InputNumber size="small" min={0} max={15} value={pokemon.pokerusDays} style={{ width: 65 }}
            onChange={(v) => set('pokerusDays', v ?? 0)} /></div>
      </Space>

      {isGen8Plus && (
        <Space style={{ width: '100%', marginTop: 8 }}>
          <div><div style={labelStyle}>身高标量</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.heightScalar} style={{ width: 75 }}
              onChange={(v) => set('heightScalar', v ?? 0)} /></div>
          <div><div style={labelStyle}>体重标量</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.weightScalar} style={{ width: 75 }}
              onChange={(v) => set('weightScalar', v ?? 0)} /></div>
          <div><div style={labelStyle}>尺寸</div>
            <Space.Compact>
              <InputNumber size="small" min={0} max={3} value={pokemon.scale} style={{ width: 55 }}
                onChange={(v) => set('scale', v ?? 0)} />
              <Tag>{['XS','S','M','L','XL'][pokemon.scale]||'?'}</Tag>
            </Space.Compact>
          </div>
        </Space>
      )}
    </div>
  );
};

const natureModifiers: Record<string, { up: string; down: string }> = {
  '怕寂寞': { up: '攻击', down: '防御' }, '勇敢': { up: '攻击', down: '速度' },
  '固执': { up: '攻击', down: '特攻' }, '顽皮': { up: '攻击', down: '特防' },
  '大胆': { up: '防御', down: '攻击' }, '悠闲': { up: '防御', down: '速度' },
  '淘气': { up: '防御', down: '特攻' }, '乐天': { up: '防御', down: '特防' },
  '胆小': { up: '速度', down: '攻击' }, '急躁': { up: '速度', down: '防御' },
  '爽朗': { up: '速度', down: '特攻' }, '天真': { up: '速度', down: '特防' },
  '内敛': { up: '特攻', down: '攻击' }, '慢吞吞': { up: '特攻', down: '防御' },
  '冷静': { up: '特攻', down: '速度' }, '马虎': { up: '特攻', down: '特防' },
  '温和': { up: '特防', down: '攻击' }, '温顺': { up: '特防', down: '防御' },
  '自大': { up: '特防', down: '速度' }, '慎重': { up: '特防', down: '特攻' },
};

export default MainTab;
