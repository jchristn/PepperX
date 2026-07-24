/**
 * Dialog rendered into `document.body`.
 *
 * Portaling matters because modals are opened from inside scrollable table wrappers, which would
 * otherwise clip them. Body scroll is locked while open so the page behind does not drift.
 */

import React, { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';

import { CloseIcon } from './Icons.jsx';

export default function Modal({
  open,
  onClose,
  title,
  subtitle = null,
  headerMeta = null,
  children,
  footer = null,
  size = 'medium',
}) {
  const { t } = useTranslation();
  const panelRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;

    const handleKeyDown = (event) => {
      if (event.key === 'Escape') onClose?.();
    };

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    window.addEventListener('keydown', handleKeyDown);

    // Move focus into the dialog so keyboard users are not still on the trigger behind the overlay.
    const focusable = panelRef.current?.querySelector(
      'input, select, textarea, button:not(.modal-close)',
    );
    (focusable ?? panelRef.current)?.focus?.();

    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [open, onClose]);

  if (!open) return null;

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        ref={panelRef}
        className={`modal modal-${size}`}
        role="dialog"
        aria-modal="true"
        aria-label={typeof title === 'string' ? title : undefined}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        <header className="modal-header">
          <div className="modal-heading">
            <h2 className="modal-title">{title}</h2>
            {subtitle ? <p className="modal-subtitle">{subtitle}</p> : null}
          </div>
          <div className="modal-header-meta">
            {headerMeta}
            <button
              type="button"
              className="button-icon modal-close"
              onClick={onClose}
              title={t('common.close')}
              aria-label={t('common.close')}
            >
              <CloseIcon size={16} />
            </button>
          </div>
        </header>

        <div className="modal-body">{children}</div>

        {footer ? <footer className="modal-footer">{footer}</footer> : null}
      </div>
    </div>,
    document.body,
  );
}
