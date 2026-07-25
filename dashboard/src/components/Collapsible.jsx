/**
 * A titled section that expands and collapses.
 *
 * The toggle is the whole header row, so the hit target is large; `actions` (a copy button, status
 * pills) sit on the right and stop click propagation so pressing them does not also fold the section.
 */

import React, { useState } from 'react';

import { ChevronDownIcon } from './Icons.jsx';

export default function Collapsible({ title, meta = null, actions = null, defaultOpen = true, children }) {
  const [open, setOpen] = useState(defaultOpen);

  return (
    <div className={`collapsible${open ? ' is-open' : ''}`}>
      <div className="collapsible-header">
        <button
          type="button"
          className="collapsible-toggle"
          onClick={() => setOpen((previous) => !previous)}
          aria-expanded={open}
        >
          <ChevronDownIcon size={16} className="collapsible-chevron" />
          <span className="collapsible-title">{title}</span>
          {meta ? <span className="collapsible-meta">{meta}</span> : null}
        </button>
        {actions ? (
          <div className="collapsible-actions" onClick={(event) => event.stopPropagation()}>
            {actions}
          </div>
        ) : null}
      </div>
      {open ? <div className="collapsible-body">{children}</div> : null}
    </div>
  );
}
