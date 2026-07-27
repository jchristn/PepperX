/**
 * Auto-refresh interval picker for a single table or chart.
 *
 * Re-runs the surface's data fetch on a fixed cadence. The chosen interval is persisted per
 * `storageKey` so it survives a reload, mirroring the pagination page-size control. The latest
 * `onRefresh` is held in a ref so a parent re-rendering with a fresh `() => load()` identity does
 * not reset the timer — only changing the selected interval (or unmounting) does.
 */

import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

/** Selectable intervals in seconds; 0 means off (no timer). */
export const AUTO_REFRESH_OPTIONS = [0, 15, 30, 60, 180, 300];

const DEFAULT_SECONDS = 30;

/** Read a persisted interval, so it survives a reload. */
export function persistedAutoRefresh(storageKey, fallback = DEFAULT_SECONDS) {
  if (!storageKey) return fallback;
  try {
    // A missing key must fall back to the default, not to 0 — and 0 ("Off") is itself a valid stored
    // value, so guard the null explicitly rather than relying on Number(null) === 0 as the page-size
    // control can (0 is not a valid page size there, but it is a valid interval here).
    const raw = window.localStorage.getItem(`pepperx.autoRefresh.${storageKey}`);
    if (raw === null) return fallback;
    const stored = Number(raw);
    return AUTO_REFRESH_OPTIONS.includes(stored) ? stored : fallback;
  } catch {
    return fallback;
  }
}

function persistAutoRefresh(storageKey, value) {
  if (!storageKey) return;
  try {
    window.localStorage.setItem(`pepperx.autoRefresh.${storageKey}`, String(value));
  } catch {
    // Persistence is a convenience; failing to store it must not break refreshing.
  }
}

export default function AutoRefresh({ onRefresh, storageKey = null, disabled = false }) {
  const { t } = useTranslation();
  const [seconds, setSeconds] = useState(() => persistedAutoRefresh(storageKey));

  // Hold the newest onRefresh so the interval always calls the latest closure without resetting.
  const onRefreshRef = useRef(onRefresh);
  useEffect(() => {
    onRefreshRef.current = onRefresh;
  }, [onRefresh]);

  useEffect(() => {
    if (seconds <= 0) return undefined;
    const id = window.setInterval(() => {
      onRefreshRef.current?.();
    }, seconds * 1000);
    return () => window.clearInterval(id);
  }, [seconds]);

  return (
    <label className="auto-refresh">
      <span className="visually-hidden">{t('common.autoRefresh')}</span>
      <select
        value={seconds}
        disabled={disabled}
        title={t('common.autoRefresh')}
        aria-label={t('common.autoRefresh')}
        onChange={(event) => {
          const next = Number(event.target.value);
          persistAutoRefresh(storageKey, next);
          setSeconds(next);
        }}
      >
        {AUTO_REFRESH_OPTIONS.map((option) => (
          <option key={option} value={option}>
            {option === 0 ? t('common.autoRefreshOff') : t('common.autoRefreshEvery', { count: option })}
          </option>
        ))}
      </select>
    </label>
  );
}
