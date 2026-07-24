/**
 * Filter form primitives.
 *
 * Apply and Clear are explicit rather than filtering as you type: enumeration hits the database, and
 * a keystroke-per-query pattern would put a request behind every character.
 */

import React from 'react';
import { useTranslation } from 'react-i18next';

export function FilterGrid({ children, wide = false }) {
  return <div className={`filter-grid${wide ? ' is-wide' : ''}`}>{children}</div>;
}

export function Field({ id, label, hint = null, error = null, children, span = 1 }) {
  return (
    <div className="form-field" style={span > 1 ? { gridColumn: `span ${span}` } : undefined}>
      <label htmlFor={id} title={hint || undefined}>
        {label}
      </label>
      {children}
      {error ? <p className="field-error">{error}</p> : null}
      {hint && !error ? <p className="field-hint">{hint}</p> : null}
    </div>
  );
}

export function FilterActions({ onApply, onClear, disabled = false }) {
  const { t } = useTranslation();
  return (
    <div className="row">
      <button type="button" className="button-secondary" onClick={onClear} disabled={disabled}>
        {t('common.clear')}
      </button>
      <button type="button" className="button-primary" onClick={onApply} disabled={disabled}>
        {t('common.apply')}
      </button>
    </div>
  );
}

/**
 * Free-form list of strings, entered one at a time.
 *
 * Used for object labels, which are a flat list rather than key-value pairs.
 */
export function ChipInput({ id, values = [], onChange, placeholder = '' }) {
  const [draft, setDraft] = React.useState('');

  const commit = () => {
    const value = draft.trim();
    if (!value || values.includes(value)) {
      setDraft('');
      return;
    }
    onChange([...values, value]);
    setDraft('');
  };

  return (
    <div className="chip-input">
      <div className="chip-list">
        {values.map((value) => (
          <span key={value} className="chip">
            {value}
            <button type="button" onClick={() => onChange(values.filter((entry) => entry !== value))} aria-label={value}>
              ×
            </button>
          </span>
        ))}
      </div>
      <input
        id={id}
        type="text"
        value={draft}
        placeholder={placeholder}
        onChange={(event) => setDraft(event.target.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') {
            event.preventDefault();
            commit();
          } else if (event.key === 'Backspace' && !draft && values.length > 0) {
            onChange(values.slice(0, -1));
          }
        }}
      />
    </div>
  );
}

/** Drop the placeholder rows a `TagEditor` leaves behind. */
export function cleanTags(tags = {}) {
  return Object.fromEntries(Object.entries(tags).filter(([key]) => key.trim() !== ''));
}

/** Key-value pair editor, used for container and object tags. */
export function TagEditor({ tags = {}, onChange, keyLabel, valueLabel, addLabel }) {
  const { t } = useTranslation();
  const entries = Object.entries(tags);

  const update = (index, field, value) => {
    const next = entries.map((entry, position) =>
      position === index ? (field === 'key' ? [value, entry[1]] : [entry[0], value]) : entry,
    );
    // Blank keys are kept so a half-typed row does not vanish while another row is edited; callers
    // strip them on submit via `cleanTags`.
    onChange(Object.fromEntries(next));
  };

  return (
    <div className="tag-editor">
      {entries.map(([key, value], index) => (
        // Index-keyed on purpose: the key text is what is being edited, so keying on it would
        // remount the input on every keystroke and lose focus.
        // eslint-disable-next-line react/no-array-index-key
        <div className="tag-editor-row" key={index}>
          <input
            type="text"
            value={key}
            aria-label={keyLabel}
            placeholder={keyLabel}
            onChange={(event) => update(index, 'key', event.target.value)}
          />
          <input
            type="text"
            value={value}
            aria-label={valueLabel}
            placeholder={valueLabel}
            onChange={(event) => update(index, 'value', event.target.value)}
          />
          <button
            type="button"
            className="button-icon"
            onClick={() => onChange(Object.fromEntries(entries.filter((_, position) => position !== index)))}
            title={t('common.delete')}
            aria-label={t('common.delete')}
          >
            ×
          </button>
        </div>
      ))}
      <button
        type="button"
        className="button-secondary"
        onClick={() => onChange({ ...tags, '': '' })}
        disabled={Object.prototype.hasOwnProperty.call(tags, '')}
      >
        {addLabel || t('common.add')}
      </button>
    </div>
  );
}

export default FilterGrid;
