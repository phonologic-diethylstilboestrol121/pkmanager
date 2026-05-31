import React, { useEffect, useState, useCallback } from 'react';
import {
  Typography, Table, Button, Upload, Popconfirm, App, Tag, Space, Card,
} from 'antd';
import { UploadOutlined, DeleteOutlined, EyeOutlined, FileAddOutlined, ArrowLeftOutlined, PlayCircleOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { saveFileApi, type SaveFileInfo } from '../api/saveFile';

const { Title } = Typography;

const GENERATION_MAP: Record<number, string> = {
  1: 'Gen1 (GB)',
  2: 'Gen2 (GBC)',
  3: 'Gen3 (GBA)',
  4: 'Gen4 (NDS)',
  5: 'Gen5 (NDS)',
  6: 'Gen6 (3DS)',
  7: 'Gen7 (3DS)',
  8: 'Gen8 (Switch)',
  9: 'Gen9 (Switch)',
};

const GENERATION_COLORS: Record<number, string> = {
  1: 'default', 2: 'default',
  3: 'green', 4: 'blue', 5: 'cyan',
  6: 'orange', 7: 'purple', 8: 'red', 9: 'volcano',
};

const SavesPage: React.FC = () => {
  const [saves, setSaves] = useState<SaveFileInfo[]>([]);
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const navigate = useNavigate();
  const { message } = App.useApp();

  const fetchSaves = useCallback(async () => {
    setLoading(true);
    try {
      const res = await saveFileApi.list();
      setSaves(res.data);
    } catch {
      message.error('加载存档列表失败');
    } finally {
      setLoading(false);
    }
  }, [message]);

  useEffect(() => {
    fetchSaves();
  }, [fetchSaves]);

  const handleUpload = async (file: File) => {
    setUploading(true);
    try {
      await saveFileApi.upload(file);
      message.success('存档上传并解析成功！');
      fetchSaves();
    } catch (err: any) {
      message.error(err.response?.data?.message || '上传失败，请检查文件格式');
    } finally {
      setUploading(false);
    }
    return false; // Prevent default upload behavior
  };

  const handleDelete = async (id: string) => {
    try {
      await saveFileApi.delete(id);
      message.success('存档已删除');
      fetchSaves();
    } catch {
      message.error('删除失败');
    }
  };

  const formatPlayTime = (seconds: number) => {
    const h = Math.floor(seconds / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    return `${h}h ${m}m`;
  };

  const columns: ColumnsType<SaveFileInfo> = [
    {
      title: '文件名',
      dataIndex: 'filename',
      key: 'filename',
      ellipsis: true,
    },
    {
      title: '世代',
      dataIndex: 'generation',
      key: 'generation',
      width: 90,
      render: (gen: number) => (
        <Tag color={GENERATION_COLORS[gen] || 'default'}>
          {GENERATION_MAP[gen] || `Gen${gen}`}
        </Tag>
      ),
    },
    {
      title: '版本',
      dataIndex: 'gameVersionName',
      key: 'gameVersionName',
      width: 100,
    },
    {
      title: '训练家',
      dataIndex: 'trainerName',
      key: 'trainerName',
      width: 90,
    },
    {
      title: '宝可梦',
      dataIndex: 'pokemonCount',
      key: 'pokemonCount',
      width: 70,
      align: 'center',
    },
    {
      title: '时间',
      dataIndex: 'playTime',
      key: 'playTime',
      width: 80,
      render: (t: number) => formatPlayTime(t),
    },
    {
      title: '状态',
      dataIndex: 'isModified',
      key: 'isModified',
      width: 80,
      render: (modified: boolean) =>
        modified ? <Tag color="orange">已修改</Tag> : <Tag>原始</Tag>,
    },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      render: (date: string) => new Date(date).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'actions',
      width: 180,
      render: (_, record) => (
        <Space>
          {(record.generation === 3) && (
            <Button type="link" size="small" icon={<PlayCircleOutlined />}
              onClick={() => window.open(`/play/${record.saveFileId}`, '_blank')}
              style={{ color: '#52c41a' }}>游玩</Button>
          )}
          <Button
            type="link"
            size="small"
            icon={<EyeOutlined />}
            onClick={() => navigate(`/saves/${record.saveFileId}`)}
          >
            查看
          </Button>
          <Popconfirm
            title="确定删除此存档？"
            description="删除后数据不可恢复"
            onConfirm={() => handleDelete(record.saveFileId)}
            okText="确定"
            cancelText="取消"
          >
            <Button type="link" size="small" danger icon={<DeleteOutlined />}>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div style={{ padding: 32, maxWidth: 1200, margin: '0 auto' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 24 }}>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/dashboard')}>返回</Button>
          <Title level={2} style={{ margin: 0 }}>存档管理</Title>
        </Space>
        <Upload
          accept=".sav,.dat,.dsv,.gci"
          showUploadList={false}
          beforeUpload={handleUpload}
        >
          <Button type="primary" icon={<UploadOutlined />} loading={uploading} size="large">
            上传存档
          </Button>
        </Upload>
      </div>

      <Card>
        <Table
          columns={columns}
          dataSource={saves}
          rowKey="saveFileId"
          loading={loading}
          scroll={{ x: 860 }}
          pagination={{ pageSize: 10 }}
          locale={{
            emptyText: (
              <div style={{ padding: 48 }}>
                <FileAddOutlined style={{ fontSize: 48, color: '#ccc' }} />
                <p style={{ marginTop: 16, color: '#999' }}>
                  暂无存档，点击「上传存档」开始
                </p>
              </div>
            ),
          }}
        />
      </Card>
    </div>
  );
};

export default SavesPage;
