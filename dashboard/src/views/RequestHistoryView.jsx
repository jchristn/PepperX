/**
 * Every REST request the node served, with full request and response detail.
 *
 * Clicking a bar in the chart writes that bucket's boundaries into the time filters and reloads, so
 * "what happened during that spike" is one click rather than a manual timestamp transcription.
 */

import React, { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ActionMenu from '@components/ActionMenu.jsx';
import ActivityChart, { getTimeRange, rangeWindow } from '@components/ActivityChart.jsx';
import ConfirmModal from '@components/ConfirmModal.jsx';
import Modal from '@components/Modal.jsx';
import PageHeader, { Card, Metric } from '@components/PageHeader.jsx';
import TableFrame from '@components/TableFrame.jsx';
import { CopyableId } from '@components/CopyButton.jsx';
import { ErrorBanner } from '@components/EmptyState.jsx';
import { Field, FilterActions, FilterGrid } from '@components/FilterBar.jsx';
import { JsonViewerModal } from '@components/JsonViewer.jsx';
import { MethodBadge, StatusBadge } from '@components/Badges.jsx';
import { persistedPageSize } from '@components/TablePagination.jsx';

const METHODS = ['GET', 'HEAD', 'POST', 'PUT', 'DELETE', 'PATCH'];
const DEFAULT_FILTERS = { method: '', statusCode: '', pathContains: '', fromUtc: '', toUtc: '' };

/** Convert a `datetime-local` value to an ISO instant, treating it as local time. */
function toIso(value) {
  if (!value) return undefined;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

/** Convert an ISO instant to the `datetime-local` format, which has no timezone and no seconds. */
function toInputValue(iso) {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

export default function RequestHistoryView() {
  const { t } = useTranslation();
  const { client, notify } = useApp();
  const formatters = useFormatters();

  const [rangeId, setRangeId] = useState('day');
  const [filters, setFilters] = useState(DEFAULT_FILTERS);
  const [entries, setEntries] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [summary, setSummary] = useState(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(() => persistedPageSize('requests', 25));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [detail, setDetail] = useState(null);
  const [jsonTarget, setJsonTarget] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [bulkOpen, setBulkOpen] = useState(false);

  const load = useCallback(
    async (nextFilters = filters, nextPageNumber = pageNumber, nextPageSize = pageSize, nextRangeId = rangeId) => {
      if (!client) return;
      setLoading(true);
      setError(null);

      const window = rangeWindow(nextRangeId);
      // An explicit time filter overrides the range tabs; otherwise the tab defines the window.
      const fromUtc = toIso(nextFilters.fromUtc) ?? window.from.toISOString();
      const toUtc = toIso(nextFilters.toUtc) ?? window.to.toISOString();
      const shared = {
        method: nextFilters.method || undefined,
        statusCode: nextFilters.statusCode || undefined,
        pathContains: nextFilters.pathContains || undefined,
        fromUtc,
        toUtc,
      };

      const [pageResult, summaryResult] = await Promise.allSettled([
        client.requestHistory({ ...shared, pageNumber: nextPageNumber, pageSize: nextPageSize }),
        client.requestHistorySummary({ ...shared, bucketMinutes: getTimeRange(nextRangeId).bucketMinutes }),
      ]);

      if (pageResult.status === 'fulfilled') {
        setEntries(pageResult.value?.Entries ?? []);
        setTotalCount(pageResult.value?.TotalCount ?? 0);
      } else {
        setError(pageResult.reason);
      }
      setSummary(summaryResult.status === 'fulfilled' ? summaryResult.value : null);
      setLoading(false);
    },
    [client, filters, pageNumber, pageSize, rangeId],
  );

  useEffect(() => {
    setPageNumber(1);
    void load(filters, 1, pageSize, rangeId);
    // Filters are applied explicitly; only the endpoint and the range window reload on their own.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [client, rangeId]);

  const applyBucket = (bucket) => {
    const next = {
      ...filters,
      fromUtc: toInputValue(bucket.bucketStartUtc),
      toUtc: toInputValue(bucket.bucketEndUtc),
    };
    setFilters(next);
    setPageNumber(1);
    void load(next, 1, pageSize, rangeId);
  };

  const submitDelete = async () => {
    try {
      await client.deleteRequestHistoryEntry(deleteTarget.Id);
      setDeleteTarget(null);
      notify(t('common.delete'), 'success');
      await load();
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const submitBulkDelete = async () => {
    try {
      const window = rangeWindow(rangeId);
      await client.deleteRequestHistoryBulk({
        method: filters.method || undefined,
        statusCode: filters.statusCode || undefined,
        pathContains: filters.pathContains || undefined,
        fromUtc: toIso(filters.fromUtc) ?? window.from.toISOString(),
        toUtc: toIso(filters.toUtc) ?? window.to.toISOString(),
      });
      setBulkOpen(false);
      notify(t('common.delete'), 'success');
      setPageNumber(1);
      await load(filters, 1, pageSize, rangeId);
    } catch (caught) {
      notify(caught.message, 'danger');
    }
  };

  const successRate = summary?.TotalCount > 0 ? summary.TotalSuccess / summary.TotalCount : null;

  const columns = [
    {
      key: 'CreatedUtc',
      label: t('requests.when'),
      render: (item) => (
        <div className="cell-stack">
          <span>{formatters.relative(item.CreatedUtc)}</span>
          <span className="cell-secondary">{formatters.dateTimeShort(item.CreatedUtc)}</span>
        </div>
      ),
    },
    { key: 'Method', label: t('requests.method'), render: (item) => <MethodBadge method={item.Method} /> },
    { key: 'StatusCode', label: t('requests.status'), render: (item) => <StatusBadge status={item.StatusCode} /> },
    { key: 'Path', label: t('requests.path'), render: (item) => <span className="mono">{item.Path}</span> },
    {
      key: 'DurationMs',
      label: t('requests.duration'),
      align: 'right',
      render: (item) => formatters.duration(item.DurationMs),
    },
    { key: 'SourceIp', label: t('requests.source'), render: (item) => item.SourceIp || '—' },
    {
      key: 'actions',
      label: '',
      style: { width: '48px' },
      render: (item) => (
        <ActionMenu
          items={[
            { key: 'view', label: t('common.view'), onClick: () => setDetail(item) },
            { key: 'json', label: t('common.viewJson'), onClick: () => setJsonTarget(item) },
            { key: 'delete', label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(item) },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="page">
      <PageHeader
        title={t('requests.title')}
        subtitle={t('requests.subtitle')}
        actions={
          <button type="button" className="button-danger" onClick={() => setBulkOpen(true)} disabled={totalCount === 0}>
            {t('requests.bulkDelete')}
          </button>
        }
      />

      <ErrorBanner error={error} onRetry={() => void load()} />

      <div className="metric-grid">
        <Metric label={t('requests.retained')} value={formatters.number(summary?.TotalCount ?? 0)} />
        <Metric
          label={t('requests.failures')}
          value={formatters.number(summary?.TotalFailure ?? 0)}
          tone={summary?.TotalFailure > 0 ? 'danger' : null}
        />
        <Metric label={t('requests.successRate')} value={successRate === null ? '—' : formatters.percent(successRate)} />
        <Metric label={t('requests.avgDuration')} value={formatters.duration(summary?.AverageDurationMs ?? 0)} />
      </div>

      <Card>
        <ActivityChart
          summary={summary}
          rangeId={rangeId}
          onRangeChange={setRangeId}
          onBucketClick={applyBucket}
          onRefresh={() => void load()}
          loading={loading}
          showStats={false}
        />
      </Card>

      <Card
        title={t('common.filters')}
        actions={
          <FilterActions
            disabled={loading}
            onApply={() => {
              setPageNumber(1);
              void load(filters, 1, pageSize, rangeId);
            }}
            onClear={() => {
              setFilters(DEFAULT_FILTERS);
              setPageNumber(1);
              void load(DEFAULT_FILTERS, 1, pageSize, rangeId);
            }}
          />
        }
      >
        <FilterGrid wide>
          <Field id="filter-method" label={t('requests.method')}>
            <select
              id="filter-method"
              value={filters.method}
              onChange={(event) => setFilters({ ...filters, method: event.target.value })}
            >
              <option value="">{t('requests.anyMethod')}</option>
              {METHODS.map((method) => (
                <option key={method} value={method}>
                  {method}
                </option>
              ))}
            </select>
          </Field>

          <Field id="filter-status" label={t('requests.status')}>
            <input
              id="filter-status"
              type="text"
              inputMode="numeric"
              value={filters.statusCode}
              placeholder={t('requests.statusPlaceholder')}
              onChange={(event) => setFilters({ ...filters, statusCode: event.target.value.replace(/[^\d]/g, '') })}
            />
          </Field>

          <Field id="filter-path" label={t('requests.pathContains')}>
            <input
              id="filter-path"
              type="text"
              value={filters.pathContains}
              spellCheck={false}
              onChange={(event) => setFilters({ ...filters, pathContains: event.target.value })}
            />
          </Field>

          <Field id="filter-from" label={t('requests.from')}>
            <input
              id="filter-from"
              type="datetime-local"
              value={filters.fromUtc}
              onChange={(event) => setFilters({ ...filters, fromUtc: event.target.value })}
            />
          </Field>

          <Field id="filter-to" label={t('requests.to')}>
            <input
              id="filter-to"
              type="datetime-local"
              value={filters.toUtc}
              onChange={(event) => setFilters({ ...filters, toUtc: event.target.value })}
            />
          </Field>
        </FilterGrid>
      </Card>

      <TableFrame
        columns={columns}
        items={entries}
        totalRecords={totalCount}
        pageNumber={pageNumber}
        pageSize={pageSize}
        onPageChange={(next) => {
          setPageNumber(next);
          void load(filters, next, pageSize, rangeId);
        }}
        onPageSizeChange={(size) => {
          setPageSize(size);
          setPageNumber(1);
          void load(filters, 1, size, rangeId);
        }}
        onRefresh={() => void load()}
        loading={loading}
        storageKey="requests"
        emptyMessage={totalCount === 0 && !filters.method && !filters.pathContains ? t('requests.empty') : t('requests.noMatches')}
        rowId={(item) => item.Id}
        onRowClick={(item) => setDetail(item)}
      />

      <RequestDetailModal detail={detail} onClose={() => setDetail(null)} />

      <JsonViewerModal
        open={Boolean(jsonTarget)}
        onClose={() => setJsonTarget(null)}
        title={t('requests.rawJson')}
        value={jsonTarget}
      />

      <ConfirmModal
        open={Boolean(deleteTarget)}
        danger
        title={t('common.delete')}
        message={t('requests.deleteConfirm')}
        confirmLabel={t('common.delete')}
        onConfirm={submitDelete}
        onCancel={() => setDeleteTarget(null)}
      />

      <ConfirmModal
        open={bulkOpen}
        danger
        title={t('requests.bulkDelete')}
        message={t('requests.bulkDeleteConfirm', {
          count: formatters.number(totalCount),
          filters: filters.method || filters.pathContains || t('requests.bulkDeleteAll'),
        })}
        confirmLabel={t('common.delete')}
        onConfirm={submitBulkDelete}
        onCancel={() => setBulkOpen(false)}
      />
    </div>
  );
}

/** Full request and response detail for one entry. */
function RequestDetailModal({ detail, onClose }) {
  const { t } = useTranslation();
  const formatters = useFormatters();

  if (!detail) return null;

  const headerRows = (headers) => {
    const entries = Object.entries(headers ?? {});
    if (entries.length === 0) return <p className="muted">{t('common.none')}</p>;
    return (
      <dl className="detail-grid is-compact">
        {entries.map(([key, value]) => (
          <React.Fragment key={key}>
            <dt>{key}</dt>
            <dd className="mono">{value}</dd>
          </React.Fragment>
        ))}
      </dl>
    );
  };

  const bodyBlock = (body, truncated, bytes) => {
    if (!body) return <p className="muted">{t('requests.noBody')}</p>;
    return (
      <>
        {truncated ? (
          <p className="field-hint">{t('requests.truncated', { shown: formatters.bytes(body.length), total: formatters.bytes(bytes) })}</p>
        ) : null}
        <pre className="body-block">{body}</pre>
      </>
    );
  };

  return (
    <Modal
      open={Boolean(detail)}
      onClose={onClose}
      title={t('requests.detailTitle')}
      subtitle={`${detail.Method} ${detail.Path}`}
      headerMeta={<CopyableId value={detail.Id} truncate={16} />}
      size="large"
    >
      <dl className="detail-grid">
        <dt>{t('requests.status')}</dt>
        <dd>
          <StatusBadge status={detail.StatusCode} />
        </dd>
        <dt>{t('requests.duration')}</dt>
        <dd>{formatters.duration(detail.DurationMs)}</dd>
        <dt>{t('requests.when')}</dt>
        <dd>{formatters.dateTime(detail.CreatedUtc)}</dd>
        <dt>{t('requests.source')}</dt>
        <dd>{detail.SourceIp || '—'}</dd>
        <dt>URL</dt>
        <dd className="mono break-all">{detail.Url}</dd>
      </dl>

      <h3 className="detail-heading">{t('requests.requestHeaders')}</h3>
      {headerRows(detail.RequestHeaders)}

      <h3 className="detail-heading">{t('requests.requestBody')}</h3>
      {bodyBlock(detail.RequestBody, detail.RequestBodyTruncated, detail.RequestBodyBytes)}

      <h3 className="detail-heading">{t('requests.responseHeaders')}</h3>
      {headerRows(detail.ResponseHeaders)}

      <h3 className="detail-heading">{t('requests.responseBody')}</h3>
      {bodyBlock(detail.ResponseBody, detail.ResponseBodyTruncated, detail.ResponseBodyBytes)}
    </Modal>
  );
}
