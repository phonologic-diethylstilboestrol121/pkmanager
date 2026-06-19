import React, { useEffect, useState } from 'react';
import { Input, InputNumber, Select, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import type { ApiError } from '../../api/axios';
import type { PokemonDto } from '../../api/saveFile';
import { getPokemonLanguageOptions } from '../../i18n/locale';
import { resourceApi, type ResourceItem } from '../../api/resource';
import { useDiagnosticStore } from '../../stores/diagnosticStore';

interface Props {
  pokemon: PokemonDto;
  generation: number;
  onChange?: () => void;
}

const OTMiscTab: React.FC<Props> = ({ pokemon, generation, onChange }) => {
  const { i18n, t } = useTranslation('editor');
  const et = (key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(key, { defaultValue, ...(options ?? {}) });
  const g = generation;
  const ch = () => onChange?.();
  const languageOptions = getPokemonLanguageOptions(t).map((option) => ({
    value: option.value,
    label: option.label.replace(/ \([A-Z]{3}\)$/, ''),
  }));
  const isGen6Plus = g >= 6;
  const isGen7Plus = g >= 7;
  const isGen8Plus = g >= 8;
  const isGen67 = g === 6 || g === 7;
  const CONSOLE_OPTIONS = [
    { value: 0, label: et('otmisc.consoleJpn', '日本 (JPN)') },
    { value: 1, label: et('otmisc.consoleUsa', '美洲 (USA)') },
    { value: 2, label: et('otmisc.consoleEur', '欧洲 (EUR)') },
    { value: 3, label: et('otmisc.consoleAus', '澳洲 (AUS)') },
    { value: 4, label: et('otmisc.consoleChn', '中国 (CHN)') },
    { value: 5, label: et('otmisc.consoleKor', '韩国 (KOR)') },
    { value: 6, label: et('otmisc.consoleTwn', '台湾 (TWN)') },
  ];

  // Geo data
  const [countries, setCountries] = useState<ResourceItem[]>([]);
  const [regions, setRegions] = useState<ResourceItem[]>([]);

  useEffect(() => {
    resourceApi.geoCountries(i18n.language).then(r => setCountries(r.data || [])).catch((err: unknown) => {
      useDiagnosticStore.getState().log({ category: 'api', level: 'error', message: 'Failed to load country list', stack: (err as ApiError).message });
    });
  }, [i18n.language]);

  useEffect(() => {
    const cid = pokemon.country || 0;
    if (cid > 0) {
      resourceApi.geoRegions(cid, i18n.language).then(r => setRegions(r.data || [])).catch((err: unknown) => {
        setRegions([]);
        useDiagnosticStore.getState().log({ category: 'api', level: 'error', message: `Failed to load region list (country=${cid})`, stack: (err as ApiError).message });
      });
    } else {
      setRegions([]);
    }
  }, [pokemon.country, i18n.language]);

  return (
    <div>
      {/* 训练家 ID */}
      <div style={sectionStyle}>
        <div style={sectionTitle}>{et('otmisc.identity', '训练家身份')}</div>
        <Space wrap size="middle">
          <div>
            <div style={labelStyle}>{et('otmisc.tid16', '表ID (TID)')}</div>
            <InputNumber
              min={0} max={65535}
              value={pokemon.tid}
              onChange={(v) => { if (v !== null) { pokemon.tid = v; ch(); } }}
              style={{ width: 100 }}
            />
          </div>
          <div>
            <div style={labelStyle}>{et('otmisc.sid16', '里ID (SID)')}</div>
            <InputNumber
              min={0} max={65535}
              value={pokemon.sid}
              onChange={(v) => { if (v !== null) { pokemon.sid = v; ch(); } }}
              style={{ width: 100 }}
            />
          </div>
          {isGen7Plus && (
            <div>
              <div style={labelStyle}>{et('otmisc.displayTid', '显示TID (6位)')}</div>
              <Input
                readOnly
                value={String(pokemon.tid).padStart(6, '0')}
                style={{ width: 100, color: '#8c8c8c' }}
              />
            </div>
          )}
        </Space>
      </div>

      {/* 训练家名称 */}
      <div style={sectionStyle}>
        <div style={sectionTitle}>{et('otmisc.trainerNameSection', '训练家名称')}</div>
        <Space wrap size="middle">
          <div>
            <div style={labelStyle}>{et('otmisc.ot', '初训家 (OT)')}</div>
            <Input
              maxLength={12}
              value={pokemon.originalTrainerName || ''}
              onChange={(e) => { pokemon.originalTrainerName = e.target.value; ch(); }}
              style={{ width: 140 }}
            />
          </div>
          <div>
            <div style={labelStyle}>{et('otmisc.gender', '性别')}</div>
            <Select
              value={pokemon.originalTrainerGender}
              onChange={(v) => { pokemon.originalTrainerGender = v; ch(); }}
              style={{ width: 90 }}
              options={[
                { value: 0, label: et('otmisc.male', '男 ♂') },
                { value: 1, label: et('otmisc.female', '女 ♀') },
              ]}
            />
          </div>
          {isGen6Plus && (
            <>
              <div>
                <div style={labelStyle}>{et('otmisc.ht', '现持有人 (HT)')}</div>
                <Input
                  maxLength={12}
                  value={pokemon.handlingTrainerName || ''}
                  onChange={(e) => { pokemon.handlingTrainerName = e.target.value; ch(); }}
                  style={{ width: 140 }}
                />
              </div>
              <div>
                <div style={labelStyle}>{et('otmisc.htGender', 'HT性别')}</div>
                <Select
                  value={pokemon.handlingTrainerGender}
                  onChange={(v) => { pokemon.handlingTrainerGender = v; ch(); }}
                  style={{ width: 90 }}
                  options={[
                    { value: 0, label: et('otmisc.male', '男 ♂') },
                    { value: 1, label: et('otmisc.female', '女 ♀') },
                  ]}
                />
              </div>
              <div>
                <div style={labelStyle}>{et('otmisc.htLanguage', 'HT语言')}</div>
                <Select
                  value={pokemon.handlingTrainerLanguage}
                  onChange={(v) => { pokemon.handlingTrainerLanguage = v; ch(); }}
                  style={{ width: 120 }}
                  options={languageOptions}
                />
              </div>
            </>
          )}
        </Space>
      </div>

      {/* 亲密度 */}
      <div style={sectionStyle}>
        <div style={sectionTitle}>{et('otmisc.friendshipSection', '亲密度')}</div>
        <Space wrap size="middle">
          <div>
            <div style={labelStyle}>{et('otmisc.otFriendship', '初训家亲密度')}</div>
            <InputNumber
              min={0} max={255}
              value={pokemon.originalTrainerFriendship}
              onChange={(v) => { if (v !== null) pokemon.originalTrainerFriendship = v; ch(); }}
              style={{ width: 85 }}
            />
          </div>
          {isGen8Plus && (
            <div>
              <div style={labelStyle}>{et('otmisc.htFriendship', '现持有人亲密度')}</div>
              <InputNumber
                min={0} max={255}
                value={pokemon.handlingTrainerFriendship}
                onChange={(v) => { if (v !== null) pokemon.handlingTrainerFriendship = v; ch(); }}
                style={{ width: 85 }}
              />
            </div>
          )}
          {isGen6Plus && pokemon.affection !== null && pokemon.affection !== undefined && (
            <div>
              <div style={labelStyle}>{et('otmisc.affection', '好感度 (Amie)')}</div>
              <InputNumber
                min={0} max={255}
                value={pokemon.affection}
                onChange={(v) => { if (v !== null) pokemon.affection = v; ch(); }}
                style={{ width: 85 }}
              />
            </div>
          )}
        </Space>
      </div>

      {/* 3DS 区域 (Gen6-7) */}
      {isGen67 && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>{et('otmisc.regionSection', '3DS 区域信息 (Gen6-7)')}</div>
          <Space wrap size="middle">
            <div>
              <div style={labelStyle}>{et('otmisc.country', '国家')}</div>
              <Select size="small" showSearch
                value={pokemon.country || 0}
                onChange={(v) => { pokemon.country = v; pokemon.subRegion = 0; ch(); }}
                style={{ width: 140 }}
                options={[{value:0,label:'—'}, ...countries.map(c=>({value:c.id,label:c.name}))]}
              />
            </div>
            <div>
              <div style={labelStyle}>{et('otmisc.region', '地区')}</div>
              <Select size="small" showSearch
                value={pokemon.subRegion || 0}
                onChange={(v) => { pokemon.subRegion = v; ch(); }}
                style={{ width: 160 }}
                options={regions.length > 0 ? regions.map(r=>({value:r.id,label:r.name})) : [{value:0,label:'—'}]}
              />
            </div>
            <div>
              <div style={labelStyle}>{et('otmisc.consoleRegion', '3DS区域')}</div>
              <Select size="small"
                value={pokemon.consoleRegion || 0}
                onChange={(v) => { pokemon.consoleRegion = v; ch(); }}
                style={{ width: 140 }}
                options={CONSOLE_OPTIONS}
              />
            </div>
            <div>
              <div style={labelStyle}>{et('otmisc.favorite', '收藏')}</div>
              <Select
                value={pokemon.isFavorite ? 1 : 0}
                onChange={(v) => { pokemon.isFavorite = v === 1; ch(); }}
                style={{ width: 80 }}
                options={[
                  { value: 0, label: et('otmisc.favoriteNo', '否') },
                  { value: 1, label: et('otmisc.favoriteYes', '是 ★') },
                ]}
              />
            </div>
          </Space>
        </div>
      )}

      {/* 加密信息 */}
      <div style={sectionStyle}>
        <div style={sectionTitle}>{et('otmisc.encryptionSection', '加密信息')}</div>
        <Space direction="vertical" style={{ width: '100%' }}>
          <code style={{
            display: 'block', background: '#f5f5f5', padding: '6px 10px',
            borderRadius: 4, fontSize: 13, fontFamily: 'monospace',
          }}>
            PID: {pokemon.pid?.toString(16).toUpperCase().padStart(8, '0') || '—'}
          </code>
          <code style={{
            display: 'block', background: '#f5f5f5', padding: '6px 10px',
            borderRadius: 4, fontSize: 13, fontFamily: 'monospace',
          }}>
            EC:  {pokemon.ec?.toString(16).toUpperCase().padStart(8, '0') || '—'}
          </code>
        </Space>
      </div>

      {/* HOME追踪ID */}
      {isGen8Plus && pokemon.homeTracker && (
        <div style={sectionStyle}>
          <div style={sectionTitle}>{et('otmisc.homeTracker', 'HOME 追踪 ID')}</div>
          <code style={{
            display: 'block', background: '#f0f0f0', padding: '6px 10px',
            borderRadius: 4, fontSize: 13, fontFamily: 'monospace',
          }}>
            {pokemon.homeTracker}
          </code>
        </div>
      )}
    </div>
  );
};

const sectionStyle: React.CSSProperties = {
  marginBottom: 16,
  padding: '12px 16px',
  background: '#fafafa',
  borderRadius: 6,
  border: '1px solid #f0f0f0',
};

const sectionTitle: React.CSSProperties = {
  fontWeight: 600,
  fontSize: 13,
  marginBottom: 10,
  color: '#595959',
};

const labelStyle: React.CSSProperties = {
  fontSize: 11,
  color: '#8c8c8c',
  marginBottom: 2,
};

// ── 3DS Country/Region options (PKHeX Chinese names) ──

export default OTMiscTab;
