/**
 * Primary navigation.
 *
 * Grouped rather than flat because the nine routes split cleanly into what you store, what you
 * observe, and what you configure — and a flat list of nine is where scanning starts to cost time.
 */

import React from 'react';
import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

import {
  ActivityIcon,
  CapacityIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  ContainerIcon,
  ExplorerIcon,
  FileIcon,
  HistoryIcon,
  HomeIcon,
  SearchIcon,
  SettingsIcon,
  UploadIcon,
} from './Icons.jsx';

const GROUPS = [
  {
    labelKey: 'nav.groupStore',
    items: [
      { to: '/', labelKey: 'nav.home', Icon: HomeIcon, end: true },
      { to: '/containers', labelKey: 'nav.containers', Icon: ContainerIcon },
      { to: '/objects', labelKey: 'nav.objects', Icon: FileIcon },
      { to: '/uploads', labelKey: 'nav.uploads', Icon: UploadIcon },
      { to: '/search', labelKey: 'nav.search', Icon: SearchIcon },
    ],
  },
  {
    labelKey: 'nav.groupObservability',
    items: [
      { to: '/capacity', labelKey: 'nav.capacity', Icon: CapacityIcon },
      { to: '/requests', labelKey: 'nav.requests', Icon: HistoryIcon },
      { to: '/observability', labelKey: 'nav.observability', Icon: ActivityIcon },
    ],
  },
  {
    labelKey: 'nav.groupDeveloper',
    items: [{ to: '/explorer', labelKey: 'nav.explorer', Icon: ExplorerIcon }],
  },
  {
    labelKey: 'nav.groupSystem',
    items: [{ to: '/settings', labelKey: 'nav.settings', Icon: SettingsIcon }],
  },
];

export default function Sidebar({ collapsed, onToggle }) {
  const { t } = useTranslation();

  return (
    <aside className={`sidebar${collapsed ? ' is-collapsed' : ''}`}>
      <div className="sidebar-brand">
        <img className="sidebar-logo" src="/logo.png" alt="" width="26" height="26" />
        {!collapsed ? <span className="sidebar-brand-name">{t('app.name')}</span> : null}
      </div>

      <nav className="sidebar-nav">
        {GROUPS.map((group) => (
          <div className="sidebar-group" key={group.labelKey}>
            {!collapsed ? <div className="sidebar-group-label">{t(group.labelKey)}</div> : null}
            {group.items.map(({ to, labelKey, Icon, end }) => (
              <NavLink
                key={to}
                to={to}
                end={end}
                className={({ isActive }) => `sidebar-link${isActive ? ' is-active' : ''}`}
                title={collapsed ? t(labelKey) : undefined}
              >
                <Icon size={17} />
                {!collapsed ? <span>{t(labelKey)}</span> : null}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      <button
        type="button"
        className="sidebar-toggle"
        onClick={onToggle}
        title={collapsed ? t('nav.expand') : t('nav.collapse')}
        aria-label={collapsed ? t('nav.expand') : t('nav.collapse')}
      >
        {collapsed ? <ChevronRightIcon size={16} /> : <ChevronLeftIcon size={16} />}
      </button>
    </aside>
  );
}
