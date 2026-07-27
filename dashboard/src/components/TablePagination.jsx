/**
 * Paging strip rendered above its table.
 *
 * Above rather than below so the controls stay put as row counts change, and so the record count is
 * visible before scrolling a long page.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import useFormatters from '@hooks/useFormatters.js';
import AutoRefresh from './AutoRefresh.jsx';
import {
  ChevronFirstIcon,
  ChevronLastIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  RefreshIcon,
} from './Icons.jsx';

export const PAGE_SIZES = [10, 25, 50, 100];

/** Read a persisted rows-per-page choice, so it survives a reload. */
export function persistedPageSize(storageKey, fallback = 25) {
  if (!storageKey) return fallback;
  try {
    const stored = Number(window.localStorage.getItem(`pepperx.pageSize.${storageKey}`));
    return PAGE_SIZES.includes(stored) ? stored : fallback;
  } catch {
    return fallback;
  }
}

function persistPageSize(storageKey, value) {
  if (!storageKey) return;
  try {
    window.localStorage.setItem(`pepperx.pageSize.${storageKey}`, String(value));
  } catch {
    // Persistence is a convenience; failing to store it must not break paging.
  }
}

export default function TablePagination({
  totalRecords = 0,
  pageNumber = 1,
  pageSize = 25,
  pageSizeOptions = PAGE_SIZES,
  onPageChange,
  onPageSizeChange,
  onRefresh = null,
  autoRefresh = true,
  disabled = false,
  storageKey = null,
  leftSlot = null,
  rightSlot = null,
}) {
  const { t } = useTranslation();
  const formatters = useFormatters();
  const [jumpValue, setJumpValue] = useState(String(pageNumber));

  // Keep the jump box in step when the page changes from elsewhere (arrows, filters, reload).
  useEffect(() => setJumpValue(String(pageNumber)), [pageNumber]);

  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
  const canPrevious = !disabled && pageNumber > 1;
  const canNext = !disabled && pageNumber < totalPages;

  const from = totalRecords === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
  const to = Math.min(totalRecords, pageNumber * pageSize);

  const commitJump = () => {
    const parsed = Number(jumpValue);
    if (!Number.isFinite(parsed) || parsed < 1) {
      setJumpValue(String(pageNumber));
      return;
    }
    const clamped = Math.max(1, Math.min(totalPages, Math.floor(parsed)));
    setJumpValue(String(clamped));
    if (clamped !== pageNumber) onPageChange?.(clamped);
  };

  return (
    <div className="table-pagination" role="group" aria-label={t('table.page', { page: pageNumber, pages: totalPages })}>
      <div className="table-pagination-summary">
        <span>
          {t('table.showing', {
            from: formatters.number(from),
            to: formatters.number(to),
            total: formatters.number(totalRecords),
          })}
        </span>
        {leftSlot}
      </div>

      <div className="table-pagination-controls">
        {rightSlot}

        <label className="table-pagination-size">
          <span className="visually-hidden">{t('table.pageSize')}</span>
          <select
            value={pageSize}
            disabled={disabled}
            onChange={(event) => {
              const next = Number(event.target.value);
              persistPageSize(storageKey, next);
              onPageSizeChange?.(next);
            }}
            title={t('table.pageSize')}
          >
            {pageSizeOptions.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </label>

        <button
          type="button"
          className="button-icon"
          onClick={() => onPageChange?.(1)}
          disabled={!canPrevious}
          title={t('table.first')}
          aria-label={t('table.first')}
        >
          <ChevronFirstIcon size={16} />
        </button>
        <button
          type="button"
          className="button-icon"
          onClick={() => onPageChange?.(pageNumber - 1)}
          disabled={!canPrevious}
          title={t('table.previous')}
          aria-label={t('table.previous')}
        >
          <ChevronLeftIcon size={16} />
        </button>

        <div className="table-pagination-jump">
          <input
            type="text"
            inputMode="numeric"
            value={jumpValue}
            disabled={disabled}
            title={t('table.jump')}
            aria-label={t('table.jump')}
            onChange={(event) => setJumpValue(event.target.value.replace(/[^\d]/g, ''))}
            onBlur={commitJump}
            onKeyDown={(event) => {
              if (event.key === 'Enter') commitJump();
            }}
          />
          <span className="muted">
            {t('common.of')} {formatters.number(totalPages)}
          </span>
        </div>

        <button
          type="button"
          className="button-icon"
          onClick={() => onPageChange?.(pageNumber + 1)}
          disabled={!canNext}
          title={t('table.next')}
          aria-label={t('table.next')}
        >
          <ChevronRightIcon size={16} />
        </button>
        <button
          type="button"
          className="button-icon"
          onClick={() => onPageChange?.(totalPages)}
          disabled={!canNext}
          title={t('table.last')}
          aria-label={t('table.last')}
        >
          <ChevronLastIcon size={16} />
        </button>

        {onRefresh && autoRefresh ? (
          <AutoRefresh onRefresh={onRefresh} storageKey={storageKey} disabled={disabled} />
        ) : null}

        {onRefresh ? (
          <button
            type="button"
            className="button-icon"
            onClick={onRefresh}
            disabled={disabled}
            title={t('common.refresh')}
            aria-label={t('common.refresh')}
          >
            <RefreshIcon size={16} />
          </button>
        ) : null}
      </div>
    </div>
  );
}
