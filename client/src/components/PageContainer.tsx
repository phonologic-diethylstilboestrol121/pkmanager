// ── PageContainer ────────────────────────────────────────────────
// 统一的页面容器组件，提供一致的 header（返回按钮 + 标题 + 操作区）
// 和内容区布局，消除各页面重复的 wrapper pattern。
//
// 用法:
//   <PageContainer title="存档管理" backTo="/dashboard" extra={<Button>操作</Button>}>
//     {page content}
//   </PageContainer>

import React from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Typography, Space } from 'antd';
import { ArrowLeftOutlined } from '@ant-design/icons';

const { Title } = Typography;

interface PageContainerProps {
  title?: React.ReactNode;
  extra?: React.ReactNode;
  /** 返回路由，不传则不显示返回按钮 */
  backTo?: string;
  maxWidth?: number;
  children: React.ReactNode;
}

const PageContainer: React.FC<PageContainerProps> = ({
  title,
  extra,
  backTo,
  maxWidth = 1200,
  children,
}) => {
  const navigate = useNavigate();

  return (
    <div style={{ padding: '24px 32px', maxWidth, margin: '0 auto', width: '100%', boxSizing: 'border-box' }}>
      {/* Header */}
      <div style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        flexWrap: 'wrap',
        gap: 12,
        marginBottom: 24,
        paddingBottom: 16,
        borderBottom: '1px solid var(--border-color, #e8e8e8)',
      }}>
        <Space align="center" size={8}>
          {backTo && (
            <Button
              icon={<ArrowLeftOutlined />}
              onClick={() => navigate(backTo)}
              size="small"
            >
              返回
            </Button>
          )}
          {title && (
            <Title level={3} style={{ margin: 0 }}>{title}</Title>
          )}
        </Space>
        {extra && <div>{extra}</div>}
      </div>

      {/* Content */}
      {children}
    </div>
  );
};

export default PageContainer;
