/**
 * Copy-to-clipboard control.
 *
 * Container names, object keys, and extent IDs are all things operators paste into a terminal, so
 * they get a copy affordance rather than asking people to select text out of a table cell.
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { CheckIcon, CopyIcon } from './Icons.jsx';

/** Copy text, falling back to a hidden textarea where the async clipboard API is unavailable. */
export async function copyToClipboard(value) {
  const text = String(value ?? '');
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Permission denied or an insecure origin; fall through to the legacy path.
    }
  }

  const scratch = document.createElement('textarea');
  scratch.value = text;
  scratch.setAttribute('readonly', '');
  scratch.style.position = 'fixed';
  scratch.style.opacity = '0';
  document.body.appendChild(scratch);
  scratch.select();
  let copied = false;
  try {
    copied = document.execCommand('copy');
  } catch {
    copied = false;
  }
  document.body.removeChild(scratch);
  return copied;
}

export default function CopyButton({ value, title, size = 14, className = '' }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const timer = useRef(null);

  useEffect(() => () => window.clearTimeout(timer.current), []);

  const handleClick = useCallback(
    async (event) => {
      // Copy buttons frequently live inside clickable rows.
      event.stopPropagation();
      const ok = await copyToClipboard(value);
      if (!ok) return;

      setCopied(true);
      window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => setCopied(false), 1600);
    },
    [value],
  );

  const label = copied ? t('common.copied') : title || t('common.copy');

  return (
    <button
      type="button"
      className={`copy-button${copied ? ' is-copied' : ''}${className ? ` ${className}` : ''}`}
      onClick={handleClick}
      title={label}
      aria-label={label}
    >
      {copied ? <CheckIcon size={size} /> : <CopyIcon size={size} />}
    </button>
  );
}

/** A monospaced identifier with a copy button beside it. */
export function CopyableId({ value, truncate = 0, title }) {
  if (!value) return <span className="muted">—</span>;

  const text = String(value);
  const shown = truncate > 0 && text.length > truncate ? `${text.slice(0, truncate)}…` : text;

  return (
    <span className="copyable-id">
      <code title={text}>{shown}</code>
      <CopyButton value={text} title={title} />
    </span>
  );
}
