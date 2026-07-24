/**
 * Formatting helpers bound to the active locale.
 *
 * Components would otherwise have to thread the locale into every call site; this keeps the
 * explicit-locale rule in `formatters.js` without making it tedious.
 */

import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import * as format from '../i18n/formatters.js';
import { normalizeLocale } from '../i18n/localeRegistry.js';

export default function useFormatters() {
  const { i18n } = useTranslation();
  const locale = normalizeLocale(i18n.resolvedLanguage || i18n.language);

  return useMemo(
    () => ({
      locale,
      number: (value, options) => format.formatNumber(value, locale, options),
      date: (value) => format.formatDate(value, locale),
      time: (value) => format.formatTime(value, locale),
      timeShort: (value) => format.formatTimeShort(value, locale),
      monthDay: (value) => format.formatMonthDay(value, locale),
      dateTime: (value) => format.formatDateTime(value, locale),
      dateTimeShort: (value) => format.formatDateTimeShort(value, locale),
      relative: (value) => format.formatRelativeTime(value, locale),
      duration: (value) => format.formatDuration(value, locale),
      bytes: (value) => format.formatBytes(value, locale),
      percent: (value, digits) => format.formatPercent(value, locale, digits),
      list: (items, type) => format.formatList(items, locale, type),
    }),
    [locale],
  );
}
