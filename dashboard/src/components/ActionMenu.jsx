/**
 * Per-row action menu.
 *
 * The dropdown is portaled and fixed-positioned so it escapes the table's `overflow: auto` wrapper.
 * Scroll and resize close it rather than repositioning it — cheaper, and a menu that follows the
 * page while it scrolls reads as stuck.
 */

import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';

import { MoreIcon } from './Icons.jsx';

const MENU_WIDTH = 190;
const ITEM_HEIGHT = 34;
const MENU_PADDING = 8;
const VIEWPORT_MARGIN = 8;

export default function ActionMenu({ items = [], label }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [position, setPosition] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const menuRef = useRef(null);

  const visible = items.filter((item) => !item.hidden);

  useLayoutEffect(() => {
    if (!open || !triggerRef.current) return;

    const rect = triggerRef.current.getBoundingClientRect();
    // Estimated rather than measured: one pre-paint pass, no flicker from a measure-then-move.
    const height = visible.length * ITEM_HEIGHT + MENU_PADDING;
    const openUpward = window.innerHeight - rect.bottom < height + 16;

    let left = rect.right - MENU_WIDTH;
    if (left < VIEWPORT_MARGIN) left = VIEWPORT_MARGIN;
    if (left + MENU_WIDTH > window.innerWidth - VIEWPORT_MARGIN) {
      left = window.innerWidth - MENU_WIDTH - VIEWPORT_MARGIN;
    }

    setPosition({ top: openUpward ? rect.top - height - 4 : rect.bottom + 4, left });
  }, [open, visible.length]);

  useEffect(() => {
    if (!open) return undefined;

    const handlePointerDown = (event) => {
      if (triggerRef.current?.contains(event.target)) return;
      if (menuRef.current?.contains(event.target)) return;
      setOpen(false);
    };
    const close = () => setOpen(false);

    window.addEventListener('mousedown', handlePointerDown);
    // Capture phase so scrolling inside any ancestor container closes the menu, not just the window.
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    return () => {
      window.removeEventListener('mousedown', handlePointerDown);
      window.removeEventListener('scroll', close, true);
      window.removeEventListener('resize', close);
    };
  }, [open]);

  const handleTriggerClick = useCallback((event) => {
    event.stopPropagation();
    setOpen((previous) => !previous);
  }, []);

  if (visible.length === 0) return null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="button-icon action-menu-trigger"
        onClick={handleTriggerClick}
        aria-haspopup="menu"
        aria-expanded={open}
        title={label || t('common.actions')}
        aria-label={label || t('common.actions')}
      >
        <MoreIcon size={16} />
      </button>

      {open
        ? createPortal(
            <div
              ref={menuRef}
              className="action-menu"
              role="menu"
              style={{ position: 'fixed', top: position.top, left: position.left, width: MENU_WIDTH }}
            >
              {visible.map((item, index) => (
                <button
                  key={item.key ?? index}
                  type="button"
                  role="menuitem"
                  className={`action-menu-item${item.variant === 'danger' ? ' is-danger' : ''}`}
                  title={item.title}
                  disabled={item.disabled}
                  onClick={(event) => {
                    event.stopPropagation();
                    setOpen(false);
                    item.onClick?.();
                  }}
                >
                  {item.label}
                </button>
              ))}
            </div>,
            document.body,
          )
        : null}
    </>
  );
}
