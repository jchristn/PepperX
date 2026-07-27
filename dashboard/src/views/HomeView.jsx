/**
 * Landing view: what this node holds, what it is serving, and what needs looking at.
 *
 * Every request is issued through `Promise.allSettled` so one failing endpoint degrades a single
 * card rather than blanking the page — statistics and request history are independent subsystems,
 * and a node with history disabled should still show its storage rollup.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ActivityChart, { getTimeRange, rangeWindow } from '@components/ActivityChart.jsx';
import AutoRefresh from '@components/AutoRefresh.jsx';
import DataTable from '@components/DataTable.jsx';
import PageHeader, { Card, Metric } from '@components/PageHeader.jsx';
import { ErrorBanner } from '@components/EmptyState.jsx';
import { AlertIcon } from '@components/Icons.jsx';

export default function HomeView() {
  const { t } = useTranslation();
  const { client } = useApp();
  const formatters = useFormatters();
  const navigate = useNavigate();

  const [rangeId, setRangeId] = useState('day');
  const [stats, setStats] = useState(null);
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const load = useCallback(
    async (nextRangeId = rangeId) => {
      if (!client) return;
      setLoading(true);

      const window = rangeWindow(nextRangeId);
      const [statsResult, summaryResult] = await Promise.allSettled([
        client.statistics(),
        client.requestHistorySummary({
          fromUtc: window.from.toISOString(),
          toUtc: window.to.toISOString(),
          bucketMinutes: getTimeRange(nextRangeId).bucketMinutes,
        }),
      ]);

      setStats(statsResult.status === 'fulfilled' ? statsResult.value : null);
      setSummary(summaryResult.status === 'fulfilled' ? summaryResult.value : null);
      setError(statsResult.status === 'rejected' ? statsResult.reason : null);
      setLoading(false);
    },
    [client, rangeId],
  );

  useEffect(() => {
    void load(rangeId);
  }, [load, rangeId]);

  const nodes = stats?.Nodes ?? [];
  const aliveNodes = nodes.filter((node) => node.IsAlive).length;
  const successRate = summary?.TotalCount > 0 ? summary.TotalSuccess / summary.TotalCount : null;

  const attention = [];
  for (const node of nodes) {
    if (!node.IsAlive) {
      attention.push({
        key: `node-${node.Id}`,
        message: t('home.attentionDeadNode', {
          id: node.Id,
          age: formatters.duration(node.HeartbeatAgeSeconds * 1000),
        }),
        to: '/capacity',
      });
    }
  }
  if (summary?.TotalFailure > 0) {
    attention.push({
      key: 'failures',
      message: t('home.attentionFailures', { count: summary.TotalFailure }),
      to: '/requests',
    });
  }

  const topContainers = [...(stats?.Containers ?? [])]
    .sort((a, b) => b.TotalBytes - a.TotalBytes)
    .slice(0, 5);

  return (
    <div className="page">
      <PageHeader title={t('home.title')} subtitle={t('home.subtitle')} />

      <ErrorBanner error={error} onRetry={() => void load()} />

      <div className="metric-grid">
        <Metric
          label={t('home.containers')}
          value={stats ? formatters.number(stats.ContainerCount) : '—'}
          note={t('home.objects') + ': ' + (stats ? formatters.number(stats.ObjectCount) : '—')}
        />
        <Metric
          label={t('home.stored')}
          value={stats ? formatters.bytes(stats.TotalBytes) : '—'}
          note={stats?.StorageFreeBytes > 0 ? `${formatters.bytes(stats.StorageFreeBytes)} ${t('capacity.storageFree').toLowerCase()}` : null}
        />
        <Metric
          label={t('home.requests')}
          value={summary ? formatters.number(summary.TotalCount) : '—'}
          note={successRate === null ? null : `${t('home.successRate')}: ${formatters.percent(successRate)}`}
          tone={summary?.TotalFailure > 0 ? 'warning' : null}
        />
        <Metric
          label={t('home.nodes')}
          value={formatters.number(nodes.length)}
          note={t('home.nodesAlive', { alive: aliveNodes, total: nodes.length })}
          tone={aliveNodes < nodes.length ? 'danger' : null}
        />
      </div>

      <Card>
        <ActivityChart
          summary={summary}
          rangeId={rangeId}
          onRangeChange={setRangeId}
          onRefresh={() => void load()}
          autoRefreshKey="home-chart"
          onBucketClick={() => navigate('/requests')}
          loading={loading}
          title={t('home.activity')}
        />
      </Card>

      <div className="split-grid">
        <Card title={t('home.attention')}>
          {attention.length === 0 ? (
            <p className="muted card-padded">{t('home.attentionNone')}</p>
          ) : (
            <ul className="attention-list">
              {attention.map((item) => (
                <li key={item.key}>
                  <AlertIcon size={16} />
                  <span>{item.message}</span>
                  <Link to={item.to}>{t('common.view')}</Link>
                </li>
              ))}
            </ul>
          )}
        </Card>

        <Card title={t('home.quickActions')}>
          <ul className="quick-actions">
            <li>
              <Link to="/containers">{t('home.quickCreateContainer')}</Link>
              <p className="muted">{t('home.quickCreateContainerHint')}</p>
            </li>
            <li>
              <Link to="/search">{t('home.quickSearch')}</Link>
              <p className="muted">{t('home.quickSearchHint')}</p>
            </li>
            <li>
              <Link to="/explorer">{t('home.quickExplorer')}</Link>
              <p className="muted">{t('home.quickExplorerHint')}</p>
            </li>
          </ul>
        </Card>
      </div>

      <Card
        title={t('capacity.byContainer')}
        actions={
          <>
            <AutoRefresh onRefresh={() => load()} storageKey="home-table" />
            <Link className="button-secondary" to="/containers">{t('nav.containers')}</Link>
          </>
        }
      >
        <DataTable
          columns={[
            { key: 'Name', label: t('common.name'), render: (item) => <strong>{item.Name}</strong> },
            { key: 'ObjectCount', label: t('containers.objects'), align: 'right', render: (item) => formatters.number(item.ObjectCount) },
            { key: 'TotalBytes', label: t('containers.size'), align: 'right', render: (item) => formatters.bytes(item.TotalBytes) },
          ]}
          items={topContainers}
          loading={loading && !stats}
          emptyMessage={t('containers.empty')}
          rowId={(item) => item.Id}
          onRowClick={(item) => navigate(`/containers/${encodeURIComponent(item.Name)}`)}
        />
      </Card>
    </div>
  );
}
