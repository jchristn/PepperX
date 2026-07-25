/**
 * Object view and edit modals.
 *
 * Shared between the container's object list and cross-container search, which is why they live here
 * rather than beside one view — both take an explicit container name, so the same components serve a
 * scoped listing and a search result identically.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import useFormatters from '@hooks/useFormatters.js';
import Modal from './Modal.jsx';
import JsonViewer from './JsonViewer.jsx';
import { CopyableId } from './CopyButton.jsx';
import { ChipInput, Field, TagEditor, cleanTags } from './FilterBar.jsx';
import { DownloadIcon } from './Icons.jsx';

/** Full metadata for one object, read-only, including its freeform JSON. */
export function ObjectDetailModal({ detail, container, onClose, onDelete = null }) {
  const { t } = useTranslation();
  const { client } = useApp();
  const formatters = useFormatters();

  if (!detail) return null;

  return (
    <Modal
      open={Boolean(detail)}
      onClose={onClose}
      title={t('objects.detailTitle')}
      subtitle={<CopyableId value={detail.Key} />}
      size="large"
      footer={
        <>
          <a
            className="button-secondary"
            href={client.objectUrl(container, detail.Key)}
            target="_blank"
            rel="noreferrer"
          >
            <DownloadIcon size={16} />
            {t('objects.downloadPayload')}
          </a>
          {onDelete ? (
            <button type="button" className="button-danger" onClick={() => onDelete(detail)}>
              {t('common.delete')}
            </button>
          ) : null}
        </>
      }
    >
      <dl className="detail-grid">
        <dt>{t('objects.key')}</dt>
        <dd className="mono">{detail.Key}</dd>
        <dt>{t('search.container')}</dt>
        <dd>{detail.ContainerName || container}</dd>
        <dt>{t('objects.size')}</dt>
        <dd>{formatters.bytes(detail.SizeBytes)}</dd>
        <dt>{t('objects.contentType')}</dt>
        <dd>{detail.ContentType || '—'}</dd>
        <dt>{t('objects.extentId')}</dt>
        <dd>
          <CopyableId value={detail.ExtentId} />
        </dd>
        <dt>{t('objects.checksum')}</dt>
        <dd>
          <CopyableId value={detail.Sha256} truncate={24} />
        </dd>
        <dt>{t('objects.created')}</dt>
        <dd title={formatters.dateTime(detail.CreatedUtc)}>{formatters.dateTime(detail.CreatedUtc)}</dd>
      </dl>

      <h3 className="detail-heading">{t('objects.labels')}</h3>
      {detail.Labels?.length ? (
        <div className="chip-list">
          {detail.Labels.map((label) => (
            <span className="chip is-static" key={label}>
              {label}
            </span>
          ))}
        </div>
      ) : (
        <p className="muted">{t('common.none')}</p>
      )}

      <h3 className="detail-heading">{t('containers.tags')}</h3>
      {Object.keys(detail.Tags ?? {}).length ? (
        <div className="chip-list">
          {Object.entries(detail.Tags).map(([key, value]) => (
            <span className="chip is-static" key={key}>
              {key}={value}
            </span>
          ))}
        </div>
      ) : (
        <p className="muted">{t('common.none')}</p>
      )}

      <h3 className="detail-heading">{t('objects.metadataObject')}</h3>
      <JsonViewer value={detail.Object} emptyMessage={t('objects.noMetadataObject')} maxHeight="320px" />
    </Modal>
  );
}

/**
 * Metadata-only update, which the server implements as a rewrite of the same payload.
 *
 * This is the Edit counterpart of {@link ObjectDetailModal}: same object, same layout intent, with
 * the fields that can change — labels, tags, and the freeform object — made writable.
 */
export function EditMetadataModal({ target, container, onClose, onSaved }) {
  const { t } = useTranslation();
  const { client, notify } = useApp();

  const [labels, setLabels] = useState([]);
  const [tags, setTags] = useState({});
  const [metadataText, setMetadataText] = useState('');
  const [jsonError, setJsonError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!target) return;
    setLabels(target.Labels ?? []);
    setTags({ ...(target.Tags ?? {}) });
    setMetadataText(target.Object ? JSON.stringify(target.Object, null, 2) : '');
    setJsonError(null);
  }, [target]);

  if (!target) return null;

  const submit = async () => {
    let metadataObject = null;
    if (metadataText.trim()) {
      try {
        metadataObject = JSON.parse(metadataText);
      } catch {
        setJsonError(t('objects.metadataObjectInvalid'));
        return;
      }
    }

    setBusy(true);
    try {
      await client.updateObjectMetadata(container, target.Key, {
        Labels: labels,
        Tags: cleanTags(tags),
        Object: metadataObject,
        // A null Object means "leave it alone" to the server, so emptying the box needs an explicit
        // clear instead.
        ClearObject: metadataObject === null,
      });
      notify(t('common.save'), 'success');
      await onSaved();
    } catch (caught) {
      notify(caught.message, 'danger');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={Boolean(target)}
      onClose={onClose}
      title={t('objects.editMetadataTitle', { key: target.Key })}
      subtitle={t('objects.editMetadataHint')}
      size="large"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy}>
            {t('common.save')}
          </button>
        </>
      }
    >
      <Field id="edit-labels" label={t('objects.labels')}>
        <ChipInput id="edit-labels" values={labels} onChange={setLabels} placeholder={t('objects.labelsPlaceholder')} />
      </Field>

      <Field id="edit-tags" label={t('containers.tags')}>
        <TagEditor
          tags={tags}
          onChange={setTags}
          keyLabel={t('containers.tagKey')}
          valueLabel={t('containers.tagValue')}
          addLabel={t('containers.addTag')}
        />
      </Field>

      <Field id="edit-object" label={t('objects.metadataObject')} error={jsonError}>
        <textarea
          id="edit-object"
          rows={8}
          value={metadataText}
          spellCheck={false}
          onChange={(event) => {
            setMetadataText(event.target.value);
            setJsonError(null);
          }}
        />
      </Field>
    </Modal>
  );
}
