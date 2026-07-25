/**
 * Run any endpoint on the connected node and inspect the exact response.
 *
 * Operations come from the node's own `/openapi.json` rather than a hardcoded list, so the explorer
 * cannot drift out of step with the server it is pointed at. Destructive verbs ask for confirmation
 * first — this is a live node, not a sandbox.
 */

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import ConfirmModal from '@components/ConfirmModal.jsx';
import PageHeader, { Card } from '@components/PageHeader.jsx';
import CopyButton from '@components/CopyButton.jsx';
import JsonViewer from '@components/JsonViewer.jsx';
import { LoadingState } from '@components/EmptyState.jsx';
import { Field } from '@components/FilterBar.jsx';
import { MethodBadge, StatusBadge } from '@components/Badges.jsx';
import { PlayIcon } from '@components/Icons.jsx';

const DESTRUCTIVE = new Set(['DELETE', 'PUT', 'POST', 'PATCH']);
const METHOD_ORDER = ['get', 'head', 'post', 'put', 'patch', 'delete'];

/** Flatten an OpenAPI paths object into a sorted operation list. */
function toOperations(spec) {
  const operations = [];
  for (const [path, pathItem] of Object.entries(spec?.paths ?? {})) {
    for (const method of METHOD_ORDER) {
      const operation = pathItem?.[method];
      if (!operation) continue;

      operations.push({
        id: `${method}:${path}`,
        method: method.toUpperCase(),
        path,
        summary: operation.summary || operation.description || '',
        tag: operation.tags?.[0] ?? 'Other',
        parameters: operation.parameters ?? [],
        hasBody: Boolean(operation.requestBody),
      });
    }
  }
  return operations.sort((a, b) => a.tag.localeCompare(b.tag) || a.path.localeCompare(b.path));
}

export default function ApiExplorerView() {
  const { t } = useTranslation();
  const { client, endpoint } = useApp();
  const formatters = useFormatters();

  const [spec, setSpec] = useState(null);
  const [loading, setLoading] = useState(true);
  const [specError, setSpecError] = useState(null);

  const [selectedId, setSelectedId] = useState(null);
  const [pathValues, setPathValues] = useState({});
  const [queryValues, setQueryValues] = useState({});
  const [headerText, setHeaderText] = useState('');
  const [bodyText, setBodyText] = useState('');

  const [result, setResult] = useState(null);
  const [running, setRunning] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [responseTab, setResponseTab] = useState('body');

  useEffect(() => {
    if (!client) return;
    setLoading(true);
    client
      .openApiSpec()
      .then((document) => {
        setSpec(document);
        setSpecError(null);
      })
      .catch((caught) => setSpecError(caught))
      .finally(() => setLoading(false));
  }, [client]);

  const operations = useMemo(() => toOperations(spec), [spec]);
  const selected = operations.find((operation) => operation.id === selectedId) ?? null;

  // Grouped by tag for the dropdown's optgroups. `operations` is already sorted by tag then path.
  const groups = useMemo(() => {
    const byTag = new Map();
    for (const operation of operations) {
      if (!byTag.has(operation.tag)) byTag.set(operation.tag, []);
      byTag.get(operation.tag).push(operation);
    }
    return Array.from(byTag, ([tag, ops]) => ({ tag, ops }));
  }, [operations]);

  const selectOperation = useCallback((operation) => {
    setSelectedId(operation?.id ?? null);
    setPathValues({});
    setQueryValues({});
    setBodyText('');
    setResult(null);
    setResponseTab('body');
  }, []);

  const resolvedPath = selected
    ? selected.path.replace(/\{([^}]+)\}/g, (match, name) => encodeURIComponent(pathValues[name] ?? match))
    : '';

  const parsedHeaders = useMemo(() => {
    const headers = {};
    for (const line of headerText.split('\n')) {
      const index = line.indexOf(':');
      if (index <= 0) continue;
      headers[line.slice(0, index).trim()] = line.slice(index + 1).trim();
    }
    return headers;
  }, [headerText]);

  const execute = async () => {
    if (!selected) return;

    setConfirmOpen(false);
    setRunning(true);
    const started = performance.now();
    try {
      const response = await client.executeExplorer({
        method: selected.method,
        path: resolvedPath,
        query: queryValues,
        headers: parsedHeaders,
        body: bodyText.trim() || undefined,
      });

      const text = await response.text();
      setResult({
        status: response.status,
        durationMs: performance.now() - started,
        headers: Object.fromEntries(response.headers.entries()),
        body: text,
      });
    } catch (caught) {
      setResult({ status: 0, durationMs: performance.now() - started, headers: {}, body: caught.message });
    } finally {
      setRunning(false);
      // Land on the Body tab for each new response — it is what a caller looks at first.
      setResponseTab('body');
    }
  };

  if (loading) return <LoadingState />;

  if (specError) {
    return (
      <div className="page">
        <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />
        <Card>
          <p className="card-padded">{t('explorer.specMissing')}</p>
        </Card>
      </div>
    );
  }

  const pathParams = selected?.parameters.filter((parameter) => parameter.in === 'path') ?? [];
  const queryParams = selected?.parameters.filter((parameter) => parameter.in === 'query') ?? [];

  return (
    <div className="page">
      <PageHeader title={t('explorer.title')} subtitle={t('explorer.subtitle')} />

      <Card title={t('explorer.operations')}>
        <div className="explorer-operation-picker">
          <select
            className="explorer-operation-select"
            value={selectedId ?? ''}
            onChange={(event) => selectOperation(operations.find((operation) => operation.id === event.target.value))}
            aria-label={t('explorer.operations')}
          >
            <option value="">{t('explorer.selectOperation')}</option>
            {groups.map((group) => (
              <optgroup key={group.tag} label={group.tag}>
                {group.ops.map((operation) => (
                  <option key={operation.id} value={operation.id}>
                    {operation.method} {operation.path}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
        </div>
      </Card>

      {!selected ? null : (
        <>
          <Card
            title={
              <span className="row">
                <MethodBadge method={selected.method} />
                <span className="mono">{selected.path}</span>
              </span>
            }
            help={selected.summary || null}
            actions={
              <button
                type="button"
                className="button-primary"
                disabled={running}
                onClick={() => (DESTRUCTIVE.has(selected.method) ? setConfirmOpen(true) : void execute())}
              >
                <PlayIcon size={14} />
                {running ? t('explorer.executing') : t('explorer.execute')}
              </button>
            }
          >
            {pathParams.length > 0 ? (
              <>
                <h3 className="detail-heading">{t('explorer.pathParams')}</h3>
                {pathParams.map((parameter) => (
                  <Field key={parameter.name} id={`path-${parameter.name}`} label={parameter.name} hint={parameter.description}>
                    <input
                      id={`path-${parameter.name}`}
                      type="text"
                      spellCheck={false}
                      value={pathValues[parameter.name] ?? ''}
                      onChange={(event) => setPathValues({ ...pathValues, [parameter.name]: event.target.value })}
                    />
                  </Field>
                ))}
              </>
            ) : null}

            {queryParams.length > 0 ? (
              <>
                <h3 className="detail-heading">{t('explorer.queryParams')}</h3>
                {queryParams.map((parameter) => (
                  <Field key={parameter.name} id={`query-${parameter.name}`} label={parameter.name} hint={parameter.description}>
                    <input
                      id={`query-${parameter.name}`}
                      type="text"
                      spellCheck={false}
                      value={queryValues[parameter.name] ?? ''}
                      onChange={(event) => setQueryValues({ ...queryValues, [parameter.name]: event.target.value })}
                    />
                  </Field>
                ))}
              </>
            ) : null}

            <Field id="explorer-headers" label={t('explorer.headers')} hint="Name: value, one per line">
              <textarea
                id="explorer-headers"
                rows={3}
                value={headerText}
                spellCheck={false}
                onChange={(event) => setHeaderText(event.target.value)}
              />
            </Field>

            {selected.hasBody ? (
              <Field id="explorer-body" label={t('explorer.body')}>
                <textarea
                  id="explorer-body"
                  rows={8}
                  value={bodyText}
                  spellCheck={false}
                  onChange={(event) => setBodyText(event.target.value)}
                />
              </Field>
            ) : null}

            <div className="explorer-url">
              <span className="field-hint">{t('explorer.resolvedUrl')}</span>
              <code className="break-all">{endpoint + resolvedPath}</code>
              <CopyButton value={endpoint + resolvedPath} />
            </div>
          </Card>

          {result ? (
            <Card
              title={t('explorer.response')}
              actions={
                <span className="row">
                  <StatusBadge status={result.status || '—'} />
                  <span className="muted">{formatters.duration(result.durationMs)}</span>
                </span>
              }
            >
              <div className="tabs" role="tablist">
                {[
                  ['body', t('explorer.responseBody')],
                  ['headers', t('explorer.responseHeaders')],
                  ['metadata', t('explorer.responseMetadata')],
                ].map(([id, label]) => (
                  <button
                    key={id}
                    type="button"
                    role="tab"
                    aria-selected={responseTab === id}
                    className={`tab${responseTab === id ? ' is-active' : ''}`}
                    onClick={() => setResponseTab(id)}
                  >
                    {label}
                  </button>
                ))}
              </div>

              <div className="tab-panel">
                {responseTab === 'body' ? (
                  result.body ? (
                    <JsonViewer value={result.body} maxHeight="460px" />
                  ) : (
                    <p className="muted">{t('requests.noBody')}</p>
                  )
                ) : null}

                {responseTab === 'headers' ? (
                  Object.keys(result.headers).length ? (
                    <dl className="detail-grid is-compact">
                      {Object.entries(result.headers).map(([key, value]) => (
                        <React.Fragment key={key}>
                          <dt>{key}</dt>
                          <dd className="mono">{value}</dd>
                        </React.Fragment>
                      ))}
                    </dl>
                  ) : (
                    <p className="muted">{t('common.none')}</p>
                  )
                ) : null}

                {responseTab === 'metadata' ? (
                  <dl className="detail-grid">
                    <dt>{t('requests.status')}</dt>
                    <dd>
                      <StatusBadge status={result.status || '—'} />
                    </dd>
                    <dt>{t('explorer.responseTime')}</dt>
                    <dd>{formatters.duration(result.durationMs)}</dd>
                    <dt>{t('objects.contentType')}</dt>
                    <dd className="mono">{result.headers['content-type'] || '—'}</dd>
                    <dt>{t('objects.size')}</dt>
                    <dd>{formatters.bytes(new Blob([result.body ?? '']).size)}</dd>
                  </dl>
                ) : null}
              </div>
            </Card>
          ) : null}
        </>
      )}

      <ConfirmModal
        open={confirmOpen}
        danger
        title={t('explorer.execute')}
        message={selected ? t('explorer.destructiveConfirm', { method: selected.method, path: resolvedPath }) : ''}
        confirmLabel={t('explorer.execute')}
        onConfirm={execute}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
