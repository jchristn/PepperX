/**
 * Title, one-line explanation, and page-level actions.
 *
 * The subtitle is not decoration: it is where each view explains what the operator is looking at,
 * which is how the console stays usable without a manual open beside it.
 */

import React from 'react';

export default function PageHeader({ title, subtitle = null, actions = null, breadcrumb = null }) {
  return (
    <header className="page-header">
      <div className="page-header-text">
        {breadcrumb ? <div className="page-breadcrumb">{breadcrumb}</div> : null}
        <h1 className="page-title">{title}</h1>
        {subtitle ? <p className="page-subtitle">{subtitle}</p> : null}
      </div>
      {actions ? <div className="page-header-actions">{actions}</div> : null}
    </header>
  );
}

/** A titled card, the unit every view is built from. */
export function Card({ title = null, kicker = null, actions = null, help = null, children, className = '' }) {
  return (
    <section className={`card${className ? ` ${className}` : ''}`}>
      {title || actions || kicker ? (
        <div className="card-header">
          <div>
            {kicker ? <div className="card-kicker">{kicker}</div> : null}
            {title ? <h2 className="card-title">{title}</h2> : null}
            {help ? <p className="card-help">{help}</p> : null}
          </div>
          {actions ? <div className="card-actions">{actions}</div> : null}
        </div>
      ) : null}
      {children}
    </section>
  );
}

/** One number with a label, used in the KPI rows. */
export function Metric({ label, value, note = null, tone = null, title = undefined }) {
  return (
    <article className="metric" title={title}>
      <div className="metric-label">{label}</div>
      <div className={`metric-value${tone ? ` is-${tone}` : ''}`}>{value}</div>
      {note ? <div className="metric-note">{note}</div> : null}
    </article>
  );
}
