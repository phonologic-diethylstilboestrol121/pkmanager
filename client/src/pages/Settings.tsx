import React, { useEffect, useState } from 'react';
import { Card, Form, Input, Button, App, Typography, Space, Divider } from 'antd';
import { SaveOutlined, DesktopOutlined, ThunderboltOutlined, DownloadOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useSettingsStore } from '../stores/settingsStore';
import PageContainer from '../components/PageContainer';

const { Text } = Typography;

// ── Form field keys ────────────────────────────────────────────────

const DESMUME_EXE = 'desmume.exe_path';
const DESMUME_SAVE = 'desmume.save_dir';
const AZAHAR_EXE = 'azahar.exe_path';
const AZAHAR_DATA = 'azahar.data_dir';

const SettingsPage: React.FC = () => {
  const { fetch, save } = useSettingsStore();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [form] = Form.useForm();
  const [saving, setSaving] = useState(false);
  const { t } = useTranslation(['pages', 'messages']);

  useEffect(() => {
    fetch().then((settings) => {
      form.setFieldsValue({
        [DESMUME_EXE]: settings[DESMUME_EXE] || '',
        [DESMUME_SAVE]: settings[DESMUME_SAVE] || '',
        [AZAHAR_EXE]: settings[AZAHAR_EXE] || '',
        [AZAHAR_DATA]: settings[AZAHAR_DATA] || '',
      });
    });
  }, [fetch, form]);

  const handleSave = async () => {
    const values = await form.validateFields();
    setSaving(true);
    try {
      await save({
        [DESMUME_EXE]: values[DESMUME_EXE] || '',
        [DESMUME_SAVE]: values[DESMUME_SAVE] || '',
        [AZAHAR_EXE]: values[AZAHAR_EXE] || '',
        [AZAHAR_DATA]: values[AZAHAR_DATA] || '',
      });
      message.success(t('settingsSaved', { ns: 'messages', defaultValue: '设置已保存' }));
    } catch {
      message.error(t('saveFailed', { ns: 'messages', defaultValue: '保存失败' }));
    } finally {
      setSaving(false);
    }
  };

  return (
    <PageContainer title={t('settings.title', { ns: 'pages', defaultValue: '设置' })} backTo="/dashboard" maxWidth={800}>

      <Form form={form} layout="vertical">
        {/* ── DeSmuME (NDS) ── */}
        <Card
          size="small"
          title={
            <Space>
              <DesktopOutlined />
              <span>{t('settings.desmumeTitle', { ns: 'pages', defaultValue: 'DeSmuME — NDS 模拟器' })}</span>
            </Space>
          }
          style={{ marginBottom: 16 }}
        >
          <Form.Item
            name={DESMUME_EXE}
            label={t('settings.executablePath', { ns: 'pages', defaultValue: '可执行文件路径' })}
            extra={t('settings.desmumeExeExtra', { ns: 'pages', defaultValue: 'Linux: /usr/bin/desmume | Windows: C:\\Program Files\\DeSmuME\\DeSmuME.exe' })}
          >
            <Input placeholder={navigator.platform.includes('Win') ? 'C:\\Program Files\\DeSmuME\\DeSmuME.exe' : '/usr/bin/desmume'} />
          </Form.Item>
          <Form.Item
            name={DESMUME_SAVE}
            label={t('settings.saveDirectory', { ns: 'pages', defaultValue: '存档目录' })}
            extra={t('settings.desmumeSaveExtra', { ns: 'pages', defaultValue: 'Linux: ~/.config/desmume/ | Windows: %APPDATA%\\DeSmuME' })}
          >
            <Input placeholder={navigator.platform.includes('Win') ? 'C:\\Users\\...\\AppData\\Roaming\\DeSmuME' : '~/.config/desmume'} />
          </Form.Item>
        </Card>

        {/* ── Azahar (3DS) ── */}
        <Card
          size="small"
          title={
            <Space>
              <ThunderboltOutlined />
              <span>{t('settings.azaharTitle', { ns: 'pages', defaultValue: 'Azahar — 3DS 模拟器' })}</span>
            </Space>
          }
          style={{ marginBottom: 24 }}
        >
          <Form.Item
            name={AZAHAR_EXE}
            label={t('settings.executablePath', { ns: 'pages', defaultValue: '可执行文件路径' })}
            extra={t('settings.azaharExeExtra', { ns: 'pages', defaultValue: 'Linux: /usr/bin/azahar | Windows: C:\\Program Files\\Azahar\\azahar.exe' })}
          >
            <Input placeholder={navigator.platform.includes('Win') ? 'C:\\Program Files\\Azahar\\azahar.exe' : '/usr/bin/azahar'} />
          </Form.Item>
          <Form.Item
            name={AZAHAR_DATA}
            label={t('settings.userDataDirectory', { ns: 'pages', defaultValue: '用户数据目录' })}
            extra={t('settings.azaharDataExtra', { ns: 'pages', defaultValue: '包含 sdmc/ 的目录（Linux: ~/.local/share/azahar-emu/ | Windows: %APPDATA%\\azahar-emu）' })}
          >
            <Input placeholder={navigator.platform.includes('Win') ? 'C:\\Users\\...\\AppData\\Roaming\\azahar-emu' : '~/.local/share/azahar-emu'} />
          </Form.Item>
        </Card>

        {/* ── 一键启动协议安装 ── */}
        <Card
          size="small"
          title={
            <Space>
              <ThunderboltOutlined />
              <span>{t('settings.protocolTitle', { ns: 'pages', defaultValue: '一键启动协议（可选）' })}</span>
            </Space>
          }
          style={{ marginBottom: 16 }}
        >
          <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
            {t('settings.protocolDescription', { ns: 'pages', defaultValue: '安装后，在存档管理页点击「本机」即可直接启动本地模拟器，无需手动下载脚本。只需安装一次。' })}
          </Text>
          <Button
            type="primary"
            ghost
            icon={<DownloadOutlined />}
            href="/scripts/install-pkmanager-protocol.bat"
            download
          >
            {t('settings.protocolInstallButton', { ns: 'pages', defaultValue: '下载安装工具 (.bat)' })}
          </Button>
          <Text type="secondary" style={{ display: 'block', marginTop: 8, fontSize: 12 }}>
            {t('settings.protocolInstallHint', { ns: 'pages', defaultValue: '下载后双击运行（会自动提权），安装完成后回到存档管理页点「本机」即可。' })}
          </Text>
        </Card>

        <Space>
          <Button
            type="primary"
            icon={<SaveOutlined />}
            onClick={handleSave}
            loading={saving}
          >
            {t('settings.saveSettings', { ns: 'pages', defaultValue: '保存设置' })}
          </Button>
          <Button onClick={() => navigate('/dashboard')}>{t('settings.backToDashboard', { ns: 'pages', defaultValue: '返回工作台' })}</Button>
        </Space>
      </Form>

      <Divider />
      <Text type="secondary" style={{ fontSize: 12 }}>
        {t('settings.deviceScopedHint', { ns: 'pages', defaultValue: '这些设置按设备独立存储。换电脑后需要重新配置。' })}
      </Text>
    </PageContainer>
  );
};

export default SettingsPage;
