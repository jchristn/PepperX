/**
 * Transient notifications.
 *
 * Success confirmations belong here rather than in the page body, where they would push content
 * around; failures also stay longer, since they usually need reading twice.
 */

import React from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';

import { useApp } from '@context/AppContext.jsx';
import { AlertIcon, CheckIcon, CloseIcon, InfoIcon } from './Icons.jsx';

const ICONS = {
  success: CheckIcon,
  danger: AlertIcon,
  warning: AlertIcon,
  info: InfoIcon,
};

export default function ToastHost() {
  const { t } = useTranslation();
  const { toasts, dismissToast } = useApp();

  if (toasts.length === 0) return null;

  return createPortal(
    <div className="toast-host" role="status" aria-live="polite">
      {toasts.map((toast) => {
        const Icon = ICONS[toast.tone] ?? InfoIcon;
        return (
          <div key={toast.id} className={`toast toast-${toast.tone}`}>
            <Icon size={16} />
            <span className="toast-message">{toast.message}</span>
            <button
              type="button"
              className="button-icon"
              onClick={() => dismissToast(toast.id)}
              title={t('common.close')}
              aria-label={t('common.close')}
            >
              <CloseIcon size={14} />
            </button>
          </div>
        );
      })}
    </div>,
    document.body,
  );
}
