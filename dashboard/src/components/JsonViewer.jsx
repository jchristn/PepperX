/**
 * Read-only JSON display with syntax coloring and a copy button.
 *
 * Object metadata is freeform, so operators inspect arbitrary shapes here; coloring is what makes a
 * deeply nested payload scannable rather than a wall of text.
 */

import React, { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import CopyButton, { CopyableId } from './CopyButton.jsx';
import Modal from './Modal.jsx';

/** Tokenize a serialized JSON document into spans, so no HTML is ever injected. */
function tokenize(text) {
  const pattern = /("(?:\\.|[^"\\])*"\s*:)|("(?:\\.|[^"\\])*")|(\b-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)|(\btrue\b|\bfalse\b)|(\bnull\b)/g;
  const tokens = [];
  let lastIndex = 0;
  let match = pattern.exec(text);

  while (match !== null) {
    if (match.index > lastIndex) tokens.push({ type: 'plain', value: text.slice(lastIndex, match.index) });

    if (match[1]) tokens.push({ type: 'key', value: match[1] });
    else if (match[2]) tokens.push({ type: 'string', value: match[2] });
    else if (match[3]) tokens.push({ type: 'number', value: match[3] });
    else if (match[4]) tokens.push({ type: 'boolean', value: match[4] });
    else tokens.push({ type: 'null', value: match[5] });

    lastIndex = pattern.lastIndex;
    match = pattern.exec(text);
  }

  if (lastIndex < text.length) tokens.push({ type: 'plain', value: text.slice(lastIndex) });
  return tokens;
}

export default function JsonViewer({ value, emptyMessage = null, maxHeight = null }) {
  const { t } = useTranslation();

  const text = useMemo(() => {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string') {
      try {
        return JSON.stringify(JSON.parse(value), null, 2);
      } catch {
        // Not JSON after all; show it verbatim rather than an error.
        return value;
      }
    }
    return JSON.stringify(value, null, 2);
  }, [value]);

  if (!text) return <p className="muted">{emptyMessage || t('common.none')}</p>;

  const tokens = tokenize(text);

  return (
    <div className="json-viewer" style={maxHeight ? { maxHeight } : undefined}>
      <CopyButton value={text} className="json-viewer-copy" />
      <pre>
        {tokens.map((token, index) => (
          // eslint-disable-next-line react/no-array-index-key
          <span key={index} className={`json-${token.type}`}>
            {token.value}
          </span>
        ))}
      </pre>
    </div>
  );
}

/**
 * The viewer inside a dialog, for row-level "View JSON" actions.
 *
 * `type` gives the heading ("Container Details", "Object Details", …) and `id` renders the object's
 * identifier beneath it with a copy button — the same affordance operators use to paste an ID into a
 * terminal. The body carries its own copy button, so both the whole document and its ID are one
 * click away.
 */
export function JsonViewerModal({ open, onClose, title, type, id, value }) {
  const { t } = useTranslation();
  const heading = title || (type ? t('common.typeDetails', { type }) : t('common.viewJson'));
  return (
    <Modal
      open={open}
      onClose={onClose}
      title={heading}
      subtitle={id ? <CopyableId value={id} /> : null}
      size="large"
    >
      <JsonViewer value={value} />
    </Modal>
  );
}
