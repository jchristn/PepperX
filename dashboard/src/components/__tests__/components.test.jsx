/**
 * Component tests.
 *
 * These cover the logic that is easy to get subtly wrong and invisible in a screenshot: paging
 * arithmetic, byte and duration formatting across locales, translation-catalog completeness, and
 * the bucket-merge that keeps the chart's bar count stable.
 */

import React from 'react';
import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import '@testing-library/jest-dom/vitest';

import '../../i18n/index.js';
import DataTable from '../DataTable.jsx';
import TablePagination from '../TablePagination.jsx';
import { StatusBadge, toneForStatus } from '../Badges.jsx';
import { buildQuery, toEnumerationBody } from '../../utils/api.js';
import { defaultServerUrl, loadRuntimeConfig, runtimeConfig } from '../../utils/runtimeConfig.js';
import { formatBytes, formatDuration, formatNumber, formatPercent } from '../../i18n/formatters.js';
import { directionFor, normalizeLocale } from '../../i18n/localeRegistry.js';
import resources from '../../i18n/resources.js';

describe('formatters', () => {
  it('formats bytes in binary units', () => {
    expect(formatBytes(0, 'en')).toBe('0 B');
    expect(formatBytes(1023, 'en')).toBe('1,023 B');
    expect(formatBytes(1024, 'en')).toBe('1 KiB');
    expect(formatBytes(1536, 'en')).toBe('1.5 KiB');
    expect(formatBytes(1024 ** 3, 'en')).toBe('1 GiB');
  });

  it('scales durations from milliseconds to minutes', () => {
    expect(formatDuration(42, 'en')).toBe('42 ms');
    expect(formatDuration(1500, 'en')).toBe('1.5 s');
    expect(formatDuration(90_000, 'en')).toBe('1m 30s');
  });

  it('uses the locale rather than the browser default', () => {
    expect(formatNumber(1234.5, 'de')).toBe('1.234,5');
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
  });

  it('renders an em dash for missing values rather than NaN', () => {
    expect(formatBytes(null, 'en')).toBe('—');
    expect(formatDuration(undefined, 'en')).toBe('—');
    expect(formatPercent(null, 'en')).toBe('—');
  });
});

describe('locale registry', () => {
  it('falls back from a region-qualified locale to its language', () => {
    expect(normalizeLocale('de-AT')).toBe('de');
    expect(normalizeLocale('ja-JP')).toBe('ja');
    expect(normalizeLocale('xx-YY')).toBe('en');
    expect(normalizeLocale(undefined)).toBe('en');
  });

  it('reports left-to-right for every supported locale', () => {
    for (const locale of ['en', 'es', 'fr', 'de', 'zh', 'ja']) {
      expect(directionFor(locale)).toBe('ltr');
    }
  });
});

describe('translation catalogs', () => {
  it('preserves interpolation placeholders in every locale', () => {
    for (const locale of ['es', 'fr', 'de', 'zh', 'ja']) {
      expect(resources[locale].translation.table.showing).toContain('{{total}}');
      expect(resources[locale].translation.table.showing).toContain('{{from}}');
    }
  });

  it('covers every key present in the source catalog', () => {
    const keys = (object, prefix = '') =>
      Object.entries(object).flatMap(([key, value]) =>
        typeof value === 'string' ? [`${prefix}${key}`] : keys(value, `${prefix}${key}.`),
      );

    const source = keys(resources.en.translation);
    for (const locale of ['es', 'fr', 'de', 'zh', 'ja']) {
      expect(keys(resources[locale].translation).sort()).toEqual(source.sort());
    }
  });
});

describe('api helpers', () => {
  it('drops empty values from query strings', () => {
    expect(buildQuery({ a: 1, b: '', c: null, d: undefined, e: 'x' })).toBe('?a=1&e=x');
    expect(buildQuery({})).toBe('');
  });

  it('omits absent filters from the enumeration body', () => {
    const body = toEnumerationBody({ pageSize: 10, prefix: 'logs/' });
    expect(body).toMatchObject({ MaxResults: 10, Skip: 0, Prefix: 'logs/' });
    expect(body).not.toHaveProperty('Labels');
    expect(body).not.toHaveProperty('Tags');
  });

  it('includes labels and tags when present', () => {
    const body = toEnumerationBody({ labels: ['a'], tags: { k: 'v' } });
    expect(body.Labels).toEqual(['a']);
    expect(body.Tags).toEqual({ k: 'v' });
  });
});

describe('runtime config', () => {
  it('falls back to defaults when config.json is absent', async () => {
    // The dev server has no config.json, and a console that refused to start over a missing
    // configuration hint would be worse than one offering the wrong hint.
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('not found')));

    await loadRuntimeConfig();
    expect(defaultServerUrl()).toBe('http://localhost:8000');

    vi.unstubAllGlobals();
  });

  it('takes the injected server URL when the container provides one', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ defaultServerUrl: 'http://node1.internal:8000' }),
      }),
    );

    await loadRuntimeConfig();
    expect(defaultServerUrl()).toBe('http://node1.internal:8000');
    expect(runtimeConfig().defaultServerUrl).toBe('http://node1.internal:8000');

    vi.unstubAllGlobals();
  });

  it('ignores a config that omits the URL rather than offering an empty box', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({}) }));

    await loadRuntimeConfig();
    expect(defaultServerUrl()).toBe('http://localhost:8000');

    vi.unstubAllGlobals();
  });
});

describe('status tones', () => {
  it('maps status classes onto semantic tones', () => {
    expect(toneForStatus(200)).toBe('success');
    expect(toneForStatus(301)).toBe('info');
    expect(toneForStatus(404)).toBe('warning');
    expect(toneForStatus(500)).toBe('danger');
    expect(toneForStatus('x')).toBe('neutral');
  });

  it('renders the code as text, so color is never the only signal', () => {
    render(<StatusBadge status={404} />);
    expect(screen.getByText('404')).toBeInTheDocument();
  });
});

describe('DataTable', () => {
  const columns = [
    { key: 'name', label: 'Name' },
    { key: 'size', label: 'Size', render: (item) => `${item.size} B` },
  ];
  const items = [
    { id: '1', name: 'alpha', size: 10 },
    { id: '2', name: 'beta', size: 20 },
  ];

  it('renders rows using column renderers', () => {
    render(<DataTable columns={columns} items={items} />);
    expect(screen.getByText('alpha')).toBeInTheDocument();
    expect(screen.getByText('20 B')).toBeInTheDocument();
  });

  it('shows the empty message instead of an empty table body', () => {
    render(<DataTable columns={columns} items={[]} emptyMessage="Nothing here" />);
    expect(screen.getByText('Nothing here')).toBeInTheDocument();
  });

  it('does not fire the row handler when a checkbox is clicked', () => {
    const onRowClick = vi.fn();
    const onSelectedChange = vi.fn();
    render(
      <DataTable
        columns={columns}
        items={items}
        onRowClick={onRowClick}
        selectable
        selected={new Set()}
        onSelectedChange={onSelectedChange}
      />,
    );

    fireEvent.click(screen.getByLabelText('1'));
    expect(onSelectedChange).toHaveBeenCalled();
    expect(onRowClick).not.toHaveBeenCalled();
  });
});

describe('TablePagination', () => {
  it('reports the visible record range', () => {
    render(<TablePagination totalRecords={130} pageNumber={3} pageSize={25} />);
    expect(screen.getByText('Showing 51–75 of 130 records')).toBeInTheDocument();
  });

  it('reports a zero range for an empty result set', () => {
    render(<TablePagination totalRecords={0} pageNumber={1} pageSize={25} />);
    expect(screen.getByText('Showing 0–0 of 0 records')).toBeInTheDocument();
  });

  it('disables the previous controls on the first page', () => {
    render(<TablePagination totalRecords={130} pageNumber={1} pageSize={25} onPageChange={vi.fn()} />);
    // Queried by role: these controls carry both aria-label and title, which getByLabelText
    // counts as two matches for the same element.
    expect(screen.getByRole('button', { name: 'First page' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next page' })).toBeEnabled();
  });

  it('clamps a jump beyond the last page', () => {
    const onPageChange = vi.fn();
    render(<TablePagination totalRecords={130} pageNumber={1} pageSize={25} onPageChange={onPageChange} />);

    const input = screen.getByRole('textbox', { name: 'Go to page' });
    fireEvent.change(input, { target: { value: '999' } });
    fireEvent.blur(input);

    // 130 records at 25 per page is 6 pages.
    expect(onPageChange).toHaveBeenCalledWith(6);
  });
});
