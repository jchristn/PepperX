/**
 * Pagination strip, optional bulk-action bar, and table as one unit.
 *
 * Views that need paging use this instead of wiring the pieces together themselves, which keeps the
 * paging behavior identical everywhere.
 */

import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';

import ConfirmModal from './ConfirmModal.jsx';
import DataTable from './DataTable.jsx';
import TablePagination from './TablePagination.jsx';

export default function TableFrame({
  columns,
  items,
  totalRecords,
  pageNumber,
  pageSize,
  onPageChange,
  onPageSizeChange,
  onRefresh,
  loading = false,
  emptyMessage,
  onRowClick,
  rowId = (item) => item.id,
  selectable = false,
  onBulkDelete = null,
  bulkDeleteLabel = null,
  bulkDeleteMessage = null,
  storageKey = null,
  leftSlot = null,
  rightSlot = null,
}) {
  const { t } = useTranslation();
  const [selected, setSelected] = useState(() => new Set());
  const [confirmOpen, setConfirmOpen] = useState(false);

  const selectedIds = Array.from(selected);
  const showBulkBar = selectable && onBulkDelete && selectedIds.length > 0;

  const runBulkDelete = async () => {
    await onBulkDelete(selectedIds);
    setSelected(new Set());
    setConfirmOpen(false);
  };

  return (
    <>
      <TablePagination
        totalRecords={totalRecords}
        pageNumber={pageNumber}
        pageSize={pageSize}
        onPageChange={onPageChange}
        onPageSizeChange={onPageSizeChange}
        onRefresh={onRefresh}
        disabled={loading}
        storageKey={storageKey}
        leftSlot={leftSlot}
        rightSlot={rightSlot}
      />

      {showBulkBar ? (
        <div className="bulk-action-bar">
          <span>{t('table.selected', { count: selectedIds.length })}</span>
          <div className="row">
            <button type="button" className="button-secondary" onClick={() => setSelected(new Set())}>
              {t('table.clearSelection')}
            </button>
            <button type="button" className="button-danger" onClick={() => setConfirmOpen(true)}>
              {bulkDeleteLabel || t('common.delete')}
            </button>
          </div>
        </div>
      ) : null}

      <DataTable
        columns={columns}
        items={items}
        loading={loading}
        emptyMessage={emptyMessage}
        onRowClick={onRowClick}
        rowId={rowId}
        selectable={selectable}
        selected={selected}
        onSelectedChange={setSelected}
        attached
      />

      <ConfirmModal
        open={confirmOpen}
        danger
        title={bulkDeleteLabel || t('common.delete')}
        message={bulkDeleteMessage?.(selectedIds.length) || t('table.selected', { count: selectedIds.length })}
        confirmLabel={t('common.delete')}
        onConfirm={runBulkDelete}
        onCancel={() => setConfirmOpen(false)}
      />
    </>
  );
}
