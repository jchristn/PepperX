/**
 * Confirmation for actions that cannot be undone.
 *
 * `requireText` exists for container deletion, where the blast radius is every object inside: making
 * someone type the name turns a misclick into a deliberate act.
 */

import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import Modal from './Modal.jsx';

export default function ConfirmModal({
  open,
  title,
  message,
  detail = null,
  confirmLabel,
  cancelLabel,
  danger = false,
  requireText = null,
  requireTextLabel = null,
  onConfirm,
  onCancel,
}) {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (open) {
      setTyped('');
      setBusy(false);
    }
  }, [open]);

  const satisfied = !requireText || typed === requireText;

  const handleConfirm = async () => {
    if (!satisfied || busy) return;
    setBusy(true);
    try {
      await onConfirm?.();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onCancel}
      title={title}
      size="small"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onCancel} disabled={busy}>
            {cancelLabel || t('common.cancel')}
          </button>
          <button
            type="button"
            className={danger ? 'button-danger' : 'button-primary'}
            onClick={handleConfirm}
            disabled={!satisfied || busy}
          >
            {busy ? t('common.loading') : confirmLabel || t('common.confirm')}
          </button>
        </>
      }
    >
      <p className="confirm-message">{message}</p>
      {detail ? <p className="confirm-detail">{detail}</p> : null}

      {requireText ? (
        <div className="form-field">
          <label htmlFor="confirm-text">{requireTextLabel || t('common.confirm')}</label>
          <input
            id="confirm-text"
            type="text"
            value={typed}
            autoComplete="off"
            spellCheck={false}
            onChange={(event) => setTyped(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter') void handleConfirm();
            }}
          />
        </div>
      ) : null}
    </Modal>
  );
}
