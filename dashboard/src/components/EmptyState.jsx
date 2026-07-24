/**
 * Placeholder for a view with nothing to show.
 *
 * Every empty state says what to do next, because "no records" on a first visit is indistinguishable
 * from a broken page.
 */

import React from 'react';
import { useTranslation } from 'react-i18next';

import { AlertIcon } from './Icons.jsx';

export default function EmptyState({ icon = null, title, message = null, action = null }) {
  return (
    <div className="empty-state">
      {icon ? <div className="empty-state-icon">{icon}</div> : null}
      <p className="empty-state-title">{title}</p>
      {message ? <p className="empty-state-message">{message}</p> : null}
      {action ? <div className="empty-state-action">{action}</div> : null}
    </div>
  );
}

/** Inline failure banner with a retry affordance. */
export function ErrorBanner({ error, onRetry = null }) {
  const { t } = useTranslation();
  if (!error) return null;

  const message = error.isNetworkError ? t('errors.unreachable') : error.message || t('common.error');

  return (
    <div className="banner banner-danger" role="alert">
      <AlertIcon size={16} />
      <span>{message}</span>
      {onRetry ? (
        <button type="button" className="button-secondary" onClick={onRetry}>
          {t('common.retry')}
        </button>
      ) : null}
    </div>
  );
}

/** Centered spinner for a view that has nothing to render yet. */
export function LoadingState() {
  const { t } = useTranslation();
  return (
    <div className="empty-state">
      <span className="spinner" />
      <p className="empty-state-message">{t('common.loading')}</p>
    </div>
  );
}
