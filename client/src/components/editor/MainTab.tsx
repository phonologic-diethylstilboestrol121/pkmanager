import React, { useEffect, useState } from 'react';
import { Select, Input, InputNumber, Switch, Space, Tag, Button } from 'antd';
import { RocketOutlined } from '@ant-design/icons';
import type { TFunction } from 'i18next';
import { useTranslation } from 'react-i18next';
import type { ApiError } from '../../api/axios';
import type { PokemonDto } from '../../api/saveFile';
import type { EvolveResultDto } from '../../api/evolution';
import { getPokemonLanguageOptions } from '../../i18n/locale';
import { useResourceStore } from '../../stores/resourceStore';
import { resourceApi, type ResourceItem } from '../../api/resource';
import { useDiagnosticStore } from '../../stores/diagnosticStore';
import EvolutionModal from './EvolutionModal';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
  saveFileId?: string;
  boxIndex?: number;
  slotIndex?: number;
  isParty?: boolean;
  editSnapshot?: Record<string, unknown>;
  onEvolved?: (result: EvolveResultDto) => void;
}

const labelStyle: React.CSSProperties = { fontSize: 11, color: '#8c8c8c', marginBottom: 2 };

const getLevelFromExp = (exp: number, table: number[]) => {
  if (table.length === 0) return 1;
  if (exp >= table[table.length - 1]) return 100;
  let level = 1;
  while (level < table.length && exp >= table[level]) level += 1;
  return level;
};

function setPokemonField<K extends keyof PokemonDto>(pokemon: PokemonDto, key: K, val: PokemonDto[K]): void {
  pokemon[key] = val;
}

const MainTab: React.FC<Props> = ({ pokemon, generation, onChange, saveFileId, boxIndex, slotIndex, isParty, editSnapshot, onEvolved }) => {
  const { t, i18n } = useTranslation('editor');
  const ct = (key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(key, { ns: 'common', defaultValue, ...(options ?? {}) });
  const { species, abilities, natures, items, balls } = useResourceStore();
  const isGen12 = generation <= 2;
  const isGen8Plus = generation >= 8;
  const ch = () => onChange?.();
  const [showEvolution, setShowEvolution] = useState(false);
  const languageOptions = getPokemonLanguageOptions(t).map((option) => ({
    value: option.value,
    label: option.label.replace(/ \([A-Z]{3}\)$/, ''),
  }));

  const [speciesAbilities, setSpeciesAbilities] = useState<ResourceItem[]>([]);
  const [expTable, setExpTable] = useState<number[]>([]);
  useEffect(() => {
    if (pokemon.species > 0) {
      resourceApi.speciesAbilities(pokemon.species, generation, pokemon.form, i18n.language).then(res => {
        setSpeciesAbilities(res.data || []);
      }).catch((err: unknown) => {
        setSpeciesAbilities([]);
        useDiagnosticStore.getState().log({
          category: 'api', level: 'error',
          message: t('main.loadSpeciesAbilitiesFailed', { species: pokemon.species, defaultValue: 'Failed to load species abilities (species={{species}})' }),
          stack: (err as ApiError).message,
        });
      });

      resourceApi.speciesExperience(pokemon.species, generation, pokemon.form, i18n.language).then(res => {
        setExpTable(res.data?.expTable || []);
      }).catch((err: unknown) => {
        setExpTable([]);
        useDiagnosticStore.getState().log({
          category: 'api', level: 'error',
          message: t('main.loadExpTableFailed', { species: pokemon.species, defaultValue: 'Failed to load experience table (species={{species}})' }),
          stack: (err as ApiError).message,
        });
      });
    } else {
      setSpeciesAbilities([]);
      setExpTable([]);
    }
  }, [pokemon.species, pokemon.form, generation, i18n.language]);

  const abilityOptions = (speciesAbilities.length > 0 ? speciesAbilities : abilities)
    .map((a, i) => ({ value: a.id, label: a.name, slot: a.slot, key: `${a.id}_${i}` }));
  const hiddenAbilityId = abilityOptions.find(option => option.label.includes('(H)'))?.value;
  const selectedAbilityKey = (() => {
    if (pokemon.ability <= 0) return 0;
    const exact = abilityOptions.find(option => option.value === pokemon.ability && option.slot === pokemon.abilitySlot);
    if (exact) return exact.key;
    const fallback = abilityOptions.find(option => option.value === pokemon.ability);
    return fallback?.key ?? pokemon.ability;
  })();
  const itemOptions = [{ value: 0, label: ct('no', 'No') }, ...items.filter(i => i.id > 0).map(i => ({ value: i.id, label: i.name }))];
  const ballOptions = balls.map(b => ({ value: b.id, label: b.name }));

  const natureMod = getNatureModifier(pokemon.nature, t);

  const set = <K extends keyof PokemonDto>(key: K, val: PokemonDto[K]) => { setPokemonField(pokemon, key, val); ch(); };

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between' }}>
        <div style={{ flex: 1 }}>
          <div style={labelStyle}>{t('showdown.field.species', 'Species')}</div>
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
          <div style={labelStyle}>{t('met.form', 'Form')}</div>
          <Space.Compact>
            <InputNumber size="small" min={0} max={63} value={pokemon.form} style={{ width: 70 }}
              onChange={(v) => set('form', v ?? 0)} />
            <Tag>{pokemon.formName || (pokemon.form > 0 ? `F${pokemon.form}` : ct('current', 'Default'))}</Tag>
          </Space.Compact>
        </div>
      </Space>

      <div style={{ marginTop: 8, borderTop: '1px solid #f0f0f0', paddingTop: 8 }}>
        <Button
          danger
          size="small"
          icon={<RocketOutlined />}
          disabled={!saveFileId || !pokemon.pkmDataBase64}
          onClick={() => setShowEvolution(true)}
        >
          {t('main.quickEvolveNoRollback', 'Quick Evolve (cannot undo)')}
        </Button>
        <span style={{ fontSize: 11, color: '#bbb', marginLeft: 8 }}>
          {t('main.pkhexEvolutionAnalysis', 'PKHeX evolution tree analysis')}
        </span>
      </div>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div>
          <div style={labelStyle}>{t('showdown.field.nickname', 'Nickname')}</div>
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
          <div style={labelStyle}>{t('trainer.language', 'Language')}</div>
          <Select size="small" value={pokemon.language} options={languageOptions} style={{ width: 120 }}
            onChange={(v) => set('language', v)} disabled={isGen12} />
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>{t('showdown.field.level', 'Level')}</div>
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
        <div><div style={labelStyle}>{t('otmisc.otFriendship', 'OT Friendship')}</div>
          <InputNumber size="small" min={0} max={255} value={pokemon.originalTrainerFriendship} style={{ width: 80 }}
            onChange={(v) => set('originalTrainerFriendship', v ?? 0)} /></div>
        {isGen8Plus && (
          <div><div style={labelStyle}>{t('otmisc.htFriendship', 'HT Friendship')}</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.handlingTrainerFriendship} style={{ width: 80 }}
              onChange={(v) => set('handlingTrainerFriendship', v ?? 0)} /></div>
        )}
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }} wrap>
        <div>
          <div style={labelStyle}>{t('showdown.field.nature', 'Nature')}</div>
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
          <div style={labelStyle}>{t('showdown.field.ability', 'Ability')}</div>
          <Space.Compact>
            <Select size="small" showSearch value={selectedAbilityKey} style={{ width: 180 }}
              onChange={(key) => {
                if (key === 0) {
                  pokemon.ability = 0;
                  pokemon.abilitySlot = undefined;
                  ch();
                  return;
                }
                const option = abilityOptions.find(item => item.key === key);
                if (!option) return;
                pokemon.ability = option.value;
                pokemon.abilitySlot = option.slot;
                ch();
              }} disabled={isGen12}>
              <Select.Option value={0} key="abi_0">{t('main.defaultAbilityOption', { defaultValue: 'Default' })}</Select.Option>
              {abilityOptions.map((a) => (
                <Select.Option value={a.key} key={`abi_${a.key}`}>{a.label}</Select.Option>
              ))}
            </Select>
            {pokemon.ability > 0 && abilityOptions.some(a => a.slot !== undefined) && (
              <Select
                size="small"
                value={pokemon.abilitySlot}
                style={{ width: 88 }}
                onChange={(slot) => {
                  const option = abilityOptions.find(item => item.slot === slot && item.value === pokemon.ability)
                    ?? abilityOptions.find(item => item.slot === slot);
                  if (!option) return;
                  pokemon.ability = option.value;
                  pokemon.abilitySlot = option.slot;
                  ch();
                }}
                disabled={isGen12}
                options={abilityOptions.map(option => ({
                  value: option.slot,
                  label: option.slot === 2
                    ? t('main.hiddenAbility', 'Hidden')
                    : option.slot === 1
                      ? t('main.abilitySlot2', 'Ability 2')
                      : t('main.abilitySlot1', 'Ability 1'),
                }))}
              />
            )}
            {hiddenAbilityId != null && pokemon.ability === hiddenAbilityId && (
              <Tag color="purple">{t('main.hiddenAbility', 'Hidden')}</Tag>
            )}
          </Space.Compact>
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>{t('showdown.field.gender', 'Gender')}</div>
          <Select size="small" value={pokemon.gender} style={{ width: 100 }}
            onChange={(v) => set('gender', v)}
            options={[
              { value: 0, label: '♂' },
              { value: 1, label: '♀' },
              { value: 2, label: t('search.genderless', 'Genderless') },
            ]} /></div>
        <div><div style={labelStyle}>{t('showdown.field.shiny', 'Shiny')}</div>
          <Switch size="small" checked={pokemon.isShiny} onChange={(v) => set('isShiny', v)} /></div>
        <div><div style={labelStyle}>{t('met.typeEgg', 'Egg')}</div>
          <Switch size="small" checked={pokemon.isEgg} onChange={(v) => set('isEgg', v)} /></div>
        <div><div style={labelStyle}>{t('main.fatefulEncounter', 'Fateful Encounter')}</div>
          <Switch size="small" checked={pokemon.fatefulEncounter} onChange={(v) => set('fatefulEncounter', v)} /></div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div>
          <div style={labelStyle}>{t('showdown.field.item', 'Held Item')}</div>
          <Select size="small" showSearch value={pokemon.heldItem} options={itemOptions}
            style={{ width: 180 }} disabled={isGen12}
            onChange={(v) => set('heldItem', v)}
            filterOption={(input, option) =>
              (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
        </div>
        <div>
          <div style={labelStyle}>{t('bankEdit.ball', 'Ball')}</div>
          <Select size="small" value={pokemon.ball} options={ballOptions} style={{ width: 130 }}
            onChange={(v) => set('ball', v)} disabled={isGen12} />
        </div>
      </Space>

      <Space style={{ width: '100%', marginTop: 8 }}>
        <div><div style={labelStyle}>{t('main.pokerusStrain', 'Pokerus Strain')}</div>
          <InputNumber size="small" min={0} max={15} value={pokemon.pokerusStrain} style={{ width: 65 }}
            onChange={(v) => set('pokerusStrain', v ?? 0)} /></div>
        <div><div style={labelStyle}>{t('main.pokerusDays', 'Pokerus Days')}</div>
          <InputNumber size="small" min={0} max={15} value={pokemon.pokerusDays} style={{ width: 65 }}
            onChange={(v) => set('pokerusDays', v ?? 0)} /></div>
      </Space>

      {isGen8Plus && (
        <Space style={{ width: '100%', marginTop: 8 }}>
          <div><div style={labelStyle}>{t('main.heightScalar', 'Height Scalar')}</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.heightScalar} style={{ width: 75 }}
              onChange={(v) => set('heightScalar', v ?? 0)} /></div>
          <div><div style={labelStyle}>{t('main.weightScalar', 'Weight Scalar')}</div>
            <InputNumber size="small" min={0} max={255} value={pokemon.weightScalar} style={{ width: 75 }}
              onChange={(v) => set('weightScalar', v ?? 0)} /></div>
          <div><div style={labelStyle}>{t('main.size', 'Size')}</div>
            <Space.Compact>
              <InputNumber size="small" min={0} max={3} value={pokemon.scale} style={{ width: 55 }}
                onChange={(v) => set('scale', v ?? 0)} />
              <Tag>{['XS','S','M','L','XL'][pokemon.scale]||'?'}</Tag>
            </Space.Compact>
          </div>
        </Space>
      )}

      <EvolutionModal
        open={showEvolution}
        pokemon={pokemon}
        saveFileId={saveFileId}
        boxIndex={boxIndex}
        slotIndex={slotIndex}
        isParty={isParty}
        editSnapshot={editSnapshot ?? {}}
        onClose={() => setShowEvolution(false)}
        onEvolved={(result) => {
          if (result.evolvedPokemon) {
            Object.assign(pokemon, result.evolvedPokemon);
          }
          setShowEvolution(false);
          onEvolved?.(result);
        }}
      />
    </div>
  );
};

function getNatureModifier(
  nature: number,
  t: TFunction<'editor'>,
): { up: string; down: string } | null {
  if (nature < 0 || nature > 24) return null;
  const up = Math.floor(nature / 5);
  const down = nature % 5;
  if (up === down) return null;

  // Nature IDs use the game-mechanics order: ATK, DEF, SPE, SPA, SPD.
  // Keep display labels translated, but map them in mechanic order.
  const labelsByNatureOrder = [
    t('stats.atkShort', 'ATK'),
    t('stats.defShort', 'DEF'),
    t('stats.speShort', 'SPE'),
    t('stats.spaShort', 'SPA'),
    t('stats.spdShort', 'SPD'),
  ];

  return { up: labelsByNatureOrder[up], down: labelsByNatureOrder[down] };
}

export default MainTab;
