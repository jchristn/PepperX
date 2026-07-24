/**
 * Small status chips.
 *
 * Color alone never carries the meaning — each badge also shows its text — so they remain readable
 * to color-blind operators and in monochrome screenshots.
 */

import React from 'react';

/** Map an HTTP status code onto a semantic tone. */
export function toneForStatus(status) {
  const code = Number(status);
  if (!Number.isFinite(code)) return 'neutral';
  if (code >= 500) return 'danger';
  if (code >= 400) return 'warning';
  if (code >= 300) return 'info';
  if (code >= 200) return 'success';
  return 'neutral';
}

export function Badge({ tone = 'neutral', children, title = undefined }) {
  return (
    <span className={`badge badge-${tone}`} title={title}>
      {children}
    </span>
  );
}

/** HTTP status code, colored by class. */
export function StatusBadge({ status }) {
  return <Badge tone={toneForStatus(status)}>{status}</Badge>;
}

/** HTTP method, with destructive verbs called out. */
export function MethodBadge({ method }) {
  const verb = String(method || '').toUpperCase();
  const tone =
    verb === 'DELETE' ? 'danger' : verb === 'POST' || verb === 'PUT' ? 'warning' : verb === 'GET' ? 'info' : 'neutral';
  return <Badge tone={tone}>{verb}</Badge>;
}

/** Liveness of a cluster node. */
export function HealthBadge({ alive, aliveLabel, deadLabel }) {
  return <Badge tone={alive ? 'success' : 'danger'}>{alive ? aliveLabel : deadLabel}</Badge>;
}

export default Badge;
