/**
 * Presentational table.
 *
 * Holds no data-fetching or paging state — `TableFrame` composes that around it — so the same
 * component renders inline card tables and full paged views.
 */

import React from 'react';
import { useTranslation } from 'react-i18next';

/**
 * @param {object[]} columns `{ key, label, style, cellClass, align, render(item) }`
 */
export default function DataTable({
  columns,
  items = [],
  loading = false,
  emptyMessage,
  onRowClick = null,
  rowId = (item) => item.id,
  selectable = false,
  selected = null,
  onSelectedChange = null,
  attached = false,
}) {
  const { t } = useTranslation();

  const ids = items.map(rowId);
  const allSelected = ids.length > 0 && ids.every((id) => selected?.has(id));
  const someSelected = ids.some((id) => selected?.has(id));

  const toggleAll = () => {
    const next = new Set(selected);
    if (allSelected) ids.forEach((id) => next.delete(id));
    else ids.forEach((id) => next.add(id));
    onSelectedChange?.(next);
  };

  const toggleOne = (id) => {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onSelectedChange?.(next);
  };

  const columnCount = columns.length + (selectable ? 1 : 0);

  return (
    <div className={`table-wrapper${attached ? ' is-attached' : ''}`}>
      <table className="data-table">
        <thead>
          <tr>
            {selectable ? (
              <th className="column-select">
                <input
                  type="checkbox"
                  checked={allSelected}
                  onChange={toggleAll}
                  aria-label={t('table.selected', { count: selected?.size ?? 0 })}
                  // Indeterminate is a DOM property, not an attribute, so it needs a ref.
                  ref={(element) => {
                    if (element) element.indeterminate = !allSelected && someSelected;
                  }}
                />
              </th>
            ) : null}
            {columns.map((column) => (
              <th key={column.key} style={column.style} className={column.align === 'right' ? 'align-right' : undefined}>
                {column.label}
              </th>
            ))}
          </tr>
        </thead>

        <tbody>
          {loading ? (
            <tr>
              <td colSpan={columnCount} className="table-state">
                <span className="spinner" />
                <span>{t('common.loading')}</span>
              </td>
            </tr>
          ) : items.length === 0 ? (
            <tr>
              <td colSpan={columnCount} className="table-state">
                {emptyMessage || t('table.empty')}
              </td>
            </tr>
          ) : (
            items.map((item, index) => {
              const id = rowId(item) ?? index;
              return (
                <tr
                  key={id}
                  className={onRowClick ? 'is-clickable' : undefined}
                  onClick={onRowClick ? () => onRowClick(item) : undefined}
                >
                  {selectable ? (
                    <td className="column-select" onClick={(event) => event.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={selected?.has(id) ?? false}
                        onChange={() => toggleOne(id)}
                        aria-label={String(id)}
                      />
                    </td>
                  ) : null}
                  {columns.map((column) => (
                    <td
                      key={column.key}
                      className={[column.cellClass, column.align === 'right' ? 'align-right' : null]
                        .filter(Boolean)
                        .join(' ') || undefined}
                    >
                      {column.render ? column.render(item) : item[column.key]}
                    </td>
                  ))}
                </tr>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
