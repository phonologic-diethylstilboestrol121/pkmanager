import React, { useEffect, useState, useCallback } from 'react';
import {
  Card, Input, InputNumber, Select, Radio, Button, App, Spin, Typography,
  Row, Col, Tooltip, Space,
} from 'antd';
import { SaveOutlined, CopyOutlined, ReloadOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { saveFileApi, type TrainerInfoDto } from '../../api/saveFile';
import { formatLocaleNumber, getPokemonLanguageOptions } from '../../i18n/locale';

const { Text } = Typography;

interface Props {
  saveFileId: string;
}

const TrainerPanel: React.FC<Props> = ({ saveFileId }) => {
  const { t, i18n } = useTranslation(['editor', 'messages', 'common']);
  const et = React.useCallback((key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(key, { ns: 'editor', defaultValue, ...(options ?? {}) }), [t]);
  const mt = React.useCallback((key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(key, { ns: 'messages', defaultValue, ...(options ?? {}) }), [t]);
  const ct = React.useCallback((key: string, defaultValue: string, options?: Record<string, unknown>) =>
    t(key, { ns: 'common', defaultValue, ...(options ?? {}) }), [t]);
  const LANGUAGE_OPTIONS = getPokemonLanguageOptions(t);
  const GENDER_OPTIONS = [
    { value: 0, label: et('otmisc.male', '男 ♂') },
    { value: 1, label: et('otmisc.female', '女 ♀') },
  ];
  const [info, setInfo] = useState<TrainerInfoDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const { message } = App.useApp();

  const fetchInfo = useCallback(async () => {
    setLoading(true);
    try {
      const res = await saveFileApi.getTrainerInfo(saveFileId);
      setInfo(res.data);
    } catch {
      message.error(et('trainer.loadFailed', '加载训练家信息失败'));
    } finally {
      setLoading(false);
    }
  }, [saveFileId, et, message]);

  useEffect(() => { fetchInfo(); }, [fetchInfo]);

  const updateField = <K extends keyof TrainerInfoDto>(key: K, value: TrainerInfoDto[K]) => {
    if (!info) return;
    setInfo({ ...info, [key]: value });
  };

  const toggleBadge = (bitIndex: number) => {
    if (info?.badges == null) return;
    const mask = 1 << bitIndex;
    updateField('badges', info.badges ^ mask);
  };

  const handleSave = async () => {
    if (!info) return;
    setSaving(true);
    try {
      await saveFileApi.saveTrainerInfo(saveFileId, info);
      message.success(et('trainer.saved', '训练家信息已保存'));
    } catch {
      message.error(mt('saveFailed', '保存失败'));
    } finally {
      setSaving(false);
    }
  };

  const copyGameSync = () => {
    if (info?.gameSyncID) {
      navigator.clipboard.writeText(info.gameSyncID).then(
        () => message.success(et('trainer.copyGameSync', '已复制 GameSync ID')),
        () => message.error(mt('saveFailed', '保存失败')),
      );
    }
  };

  if (loading) return <div style={{ padding: 48, textAlign: 'center' }}><Spin /></div>;
  if (!info) return <div style={{ padding: 48, textAlign: 'center' }}><Text type="secondary">{et('trainer.loadFailed', '加载训练家信息失败')}</Text></div>;

  const cap = info.capability;

  return (
    <div style={{ maxWidth: 800, margin: '0 auto', paddingBottom: 60 }}>
      {/* 基本信息 */}
      <Card title={et('trainer.basic', '基本信息')} size="small" style={{ marginBottom: 12 }}>
        <Row gutter={[16, 8]}>
          <Col span={12}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.name', '训练家名称')}</Text>
              <Input
                value={info.ot}
                onChange={e => updateField('ot', e.target.value)}
                maxLength={cap.maxStringLengthTrainer}
                style={{ maxWidth: 200 }}
              />
            </div>
          </Col>
          <Col span={12}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.gender', '性别')}</Text>
              <Radio.Group
                options={GENDER_OPTIONS}
                value={info.gender}
                onChange={e => updateField('gender', e.target.value)}
                optionType="button" size="small"
              />
            </div>
          </Col>
          <Col span={6}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>TID (16-bit)</Text>
              <InputNumber
                value={info.tid16} min={0} max={65535}
                onChange={v => updateField('tid16', v ?? 0)}
                style={{ width: '100%' }}
              />
            </div>
          </Col>
          <Col span={6}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>SID (16-bit)</Text>
              <InputNumber
                value={info.sid16} min={0} max={65535}
                onChange={v => updateField('sid16', v ?? 0)}
                style={{ width: '100%' }}
              />
            </div>
          </Col>
          {cap.trainerIDFormat === 3 && (
            <>
              <Col span={6}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.displayTid', '显示 TID (只读)')}</Text>
                  <Input
                    value={String(info.displayTID).padStart(6, '0')}
                    readOnly
                    style={{ width: '100%', fontFamily: 'monospace' }}
                  />
                </div>
              </Col>
              <Col span={6}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.displaySid', '显示 SID (只读)')}</Text>
                  <Input
                    value={String(info.displaySID).padStart(4, '0')}
                    readOnly
                    style={{ width: '100%', fontFamily: 'monospace' }}
                  />
                </div>
              </Col>
            </>
          )}
          <Col span={12}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.language', '语言')}</Text>
              <Select
                value={info.language}
                onChange={v => updateField('language', v)}
                options={LANGUAGE_OPTIONS}
                style={{ maxWidth: 200 }}
                showSearch
              />
            </div>
          </Col>
          <Col span={12}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.gameVersion', '游戏版本')}</Text>
              <Input value={info.gameVersionName || `Gen ${info.generation}`} readOnly style={{ maxWidth: 200 }} />
            </div>
          </Col>
          <Col span={4}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.hours', '时')}</Text>
              <InputNumber value={info.playedHours} min={0} max={999} onChange={v => updateField('playedHours', v ?? 0)} style={{ width: '100%' }} />
            </div>
          </Col>
          <Col span={4}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.minutes', '分')}</Text>
              <InputNumber value={info.playedMinutes} min={0} max={59} onChange={v => updateField('playedMinutes', v ?? 0)} style={{ width: '100%' }} />
            </div>
          </Col>
          <Col span={4}>
            <div style={{ marginBottom: 8 }}>
              <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.seconds', '秒')}</Text>
              <InputNumber value={info.playedSeconds} min={0} max={59} onChange={v => updateField('playedSeconds', v ?? 0)} style={{ width: '100%' }} />
            </div>
          </Col>
        </Row>
      </Card>

      {/* 货币 */}
      {(info.money != null || cap.hasCoins || cap.hasBP || cap.hasLeaguePoints) && (
        <Card title={et('trainer.currency', '货币')} size="small" style={{ marginBottom: 12 }}>
          <Row gutter={[16, 8]}>
            {info.money != null && (
              <Col span={8}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.money', '金钱 (Max: {{max}})', { max: formatLocaleNumber(cap.maxMoney, i18n.language) })}</Text>
                  <InputNumber
                    value={info.money} min={0} max={cap.maxMoney}
                    onChange={v => updateField('money', v ?? 0)}
                    style={{ width: '100%' }} formatter={v => `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                  />
                </div>
              </Col>
            )}
            {cap.hasCoins && info.coins != null && (
              <Col span={8}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.coins', '代币 (Max: {{max}})', { max: formatLocaleNumber(cap.maxCoins, i18n.language) })}</Text>
                  <InputNumber
                    value={info.coins} min={0} max={cap.maxCoins ?? 99999}
                    onChange={v => updateField('coins', v ?? 0)}
                    style={{ width: '100%' }}
                  />
                </div>
              </Col>
            )}
            {cap.hasBP && info.bp != null && (
              <Col span={8}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.bp', '战斗点数 (BP)')}</Text>
                  <InputNumber
                    value={info.bp} min={0} max={99999}
                    onChange={v => updateField('bp', v ?? 0)}
                    style={{ width: '100%' }}
                  />
                </div>
              </Col>
            )}
            {cap.hasLeaguePoints && info.leaguePoints != null && (
              <Col span={8}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.lp', '联盟点数 (LP)')}</Text>
                  <InputNumber
                    value={info.leaguePoints} min={0} max={99999999}
                    onChange={v => updateField('leaguePoints', v ?? 0)}
                    style={{ width: '100%' }}
                  />
                </div>
              </Col>
            )}
          </Row>
        </Card>
      )}

      {/* 徽章 */}
      {cap.hasBadges && info.badges != null && cap.badgeCount > 0 && (
        <Card title={et('trainer.badges', '徽章')} size="small" style={{ marginBottom: 12 }}>
          <div style={{ display: 'grid', gridTemplateColumns: `repeat(${Math.min(cap.badgeCount, 8)}, 1fr)`, gap: 8, maxWidth: cap.badgeCount <= 8 ? 480 : 640 }}>
            {cap.badgeNames.map((name, i) => {
              const obtained = (info.badges! >> i) & 1;
              return (
                <Tooltip key={i} title={name}>
                  <div
                    onClick={() => toggleBadge(i)}
                    style={{
                      aspectRatio: '1', borderRadius: 12, cursor: 'pointer',
                      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
                      background: obtained ? '#faad14' : '#f0f0f0',
                      border: obtained ? '2px solid #d48806' : '2px solid #d9d9d9',
                      transition: 'all 0.2s',
                      userSelect: 'none',
                    }}
                  >
                    <Text style={{
                      fontSize: 16, fontWeight: 'bold',
                      color: obtained ? '#fff' : '#bbb',
                    }}>
                      {obtained ? '★' : '☆'}
                    </Text>
                    <Text style={{
                      fontSize: 9, textAlign: 'center',
                      color: obtained ? '#fff' : '#999',
                      lineHeight: 1.1, maxWidth: '90%', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                    }}>
                      {name.length > 4 ? name.slice(0, 4) : name}
                    </Text>
                  </div>
                </Tooltip>
              );
            })}
          </div>
          <div style={{ marginTop: 8 }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {et('trainer.badgesProgress', '已获得 {{count}} / {{total}} 枚徽章', {
                count: (info.badges ?? 0).toString(2).split('1').length - 1,
                total: cap.badgeCount,
              })}
            </Text>
          </div>
        </Card>
      )}

      {/* 训练家卡片 (Gen8 SwSh) */}
      {cap.hasTrainerCard && (
        <Card title={et('trainer.card', '训练家卡片')} size="small" style={{ marginBottom: 12 }}>
          <Row gutter={[16, 8]}>
            {cap.hasCardNumber && (
              <Col span={8}>
                <div style={{ marginBottom: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>{et('trainer.cardNumber', '卡片编号')}</Text>
                  <Input
                    value={info.cardNumber || ''}
                    onChange={e => updateField('cardNumber', e.target.value)}
                    maxLength={3}
                    style={{ maxWidth: 100 }}
                  />
                </div>
              </Col>
            )}
          </Row>
        </Card>
      )}

      {/* GameSync ID */}
      {cap.hasGameSync && info.gameSyncID != null && (
        <Card title="Game Sync ID" size="small" style={{ marginBottom: 12 }}>
          <Space>
            <Input value={info.gameSyncID} readOnly style={{ fontFamily: 'monospace', width: 300 }} />
            <Tooltip title={et('trainer.copy', '复制')}>
              <Button icon={<CopyOutlined />} size="small" onClick={copyGameSync} />
            </Tooltip>
            <Text type="secondary" style={{ fontSize: 11 }}>{et('trainer.gameSyncReadonly', '只读 — 修改会导致在线服务同步失败')}</Text>
          </Space>
        </Card>
      )}

      {/* 保存按钮 */}
      <div style={{
        position: 'fixed', bottom: 0, left: 0, right: 0,
        background: '#fff', borderTop: '1px solid #e8e8e8',
        padding: '8px 24px', display: 'flex', justifyContent: 'center', gap: 12,
        zIndex: 10,
      }}>
        <Button icon={<ReloadOutlined />} onClick={fetchInfo} loading={loading}>{ct('reset', '重置')}</Button>
        <Button type="primary" icon={<SaveOutlined />} loading={saving} onClick={handleSave}>
          {et('trainer.save', '保存训练家信息')}
        </Button>
      </div>
    </div>
  );
};

export default TrainerPanel;
