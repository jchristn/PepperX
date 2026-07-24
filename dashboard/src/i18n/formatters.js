/**
 * Locale-aware formatting helpers.
 *
 * Every helper takes an explicit locale. Relying on the browser default would make output drift from
 * the language the operator actually chose, so nothing here calls `toLocaleString` without one.
 */

import { DEFAULT_LOCALE, normalizeLocale } from './localeRegistry.js';

const numberCache = new Map();
const dateCache = new Map();

function numberFormatter(locale, options) {
  const key = `${locale}|${JSON.stringify(options)}`;
  if (!numberCache.has(key)) numberCache.set(key, new Intl.NumberFormat(locale, options));
  return numberCache.get(key);
}

function dateFormatter(locale, options) {
  const key = `${locale}|${JSON.stringify(options)}`;
  if (!dateCache.has(key)) dateCache.set(key, new Intl.DateTimeFormat(locale, options));
  return dateCache.get(key);
}

function toDate(value) {
  if (value instanceof Date) return value;
  if (typeof value === 'string' || typeof value === 'number') {
    const parsed = new Date(value);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }
  return null;
}

/** Format an integer or decimal. */
export function formatNumber(value, locale = DEFAULT_LOCALE, options = {}) {
  if (value === null || value === undefined) return '—';
  return numberFormatter(normalizeLocale(locale), options).format(value);
}

/** Format a date only. */
export function formatDate(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { dateStyle: 'medium' }).format(date);
}

/** Format a time only. */
export function formatTime(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { timeStyle: 'medium' }).format(date);
}

/** Format a time to the minute, for dense axis labels where seconds are noise. */
export function formatTimeShort(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { hour: 'numeric', minute: '2-digit' }).format(date);
}

/** Format a month and day only, for axis labels spanning more than a day. */
export function formatMonthDay(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { month: 'short', day: 'numeric' }).format(date);
}

/** Format a date and time together. */
export function formatDateTime(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { dateStyle: 'medium', timeStyle: 'medium' }).format(date);
}

/** Format a short date and time, for dense table cells. */
export function formatDateTimeShort(value, locale = DEFAULT_LOCALE) {
  const date = toDate(value);
  if (!date) return '—';
  return dateFormatter(normalizeLocale(locale), { dateStyle: 'short', timeStyle: 'medium' }).format(date);
}

/** Format a time relative to now, for example "5 minutes ago". */
export function formatRelativeTime(value, locale = DEFAULT_LOCALE, now = Date.now()) {
  const date = toDate(value);
  if (!date) return '—';

  const seconds = Math.round((date.getTime() - now) / 1000);
  const formatter = new Intl.RelativeTimeFormat(normalizeLocale(locale), { numeric: 'auto' });
  const thresholds = [
    ['year', 31536000],
    ['month', 2592000],
    ['day', 86400],
    ['hour', 3600],
    ['minute', 60],
  ];

  for (const [unit, size] of thresholds) {
    if (Math.abs(seconds) >= size) return formatter.format(Math.round(seconds / size), unit);
  }
  return formatter.format(seconds, 'second');
}

/** Format a duration given in milliseconds. */
export function formatDuration(milliseconds, locale = DEFAULT_LOCALE) {
  if (milliseconds === null || milliseconds === undefined) return '—';
  if (milliseconds < 1000) return `${formatNumber(Math.round(milliseconds), locale)} ms`;

  const seconds = milliseconds / 1000;
  if (seconds < 60) return `${formatNumber(seconds, locale, { maximumFractionDigits: 1 })} s`;

  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60);
  return `${formatNumber(minutes, locale)}m ${formatNumber(remainder, locale)}s`;
}

/** Format a byte count using binary units. */
export function formatBytes(bytes, locale = DEFAULT_LOCALE) {
  if (bytes === null || bytes === undefined) return '—';
  if (bytes < 1024) return `${formatNumber(bytes, locale)} B`;

  const units = ['KiB', 'MiB', 'GiB', 'TiB', 'PiB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${formatNumber(value, locale, { maximumFractionDigits: 1 })} ${units[unit]}`;
}

/** Format a ratio in the range 0..1 as a percentage. */
export function formatPercent(ratio, locale = DEFAULT_LOCALE, fractionDigits = 1) {
  if (ratio === null || ratio === undefined) return '—';
  return numberFormatter(normalizeLocale(locale), {
    style: 'percent',
    maximumFractionDigits: fractionDigits,
  }).format(ratio);
}

/** Join a list the way the locale does, rather than always with commas. */
export function formatList(items, locale = DEFAULT_LOCALE, type = 'conjunction') {
  if (!items?.length) return '';
  return new Intl.ListFormat(normalizeLocale(locale), { style: 'long', type }).format(items.map(String));
}
