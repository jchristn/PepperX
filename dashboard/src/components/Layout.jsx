/**
 * Application shell: sidebar, topbar, and the routed view.
 *
 * Guards the whole app on having an endpoint — every view needs a client, so there is no useful
 * state to render without one.
 */

import React, { useEffect, useState } from 'react';
import { Navigate, Outlet } from 'react-router-dom';

import { useApp } from '@context/AppContext.jsx';
import Sidebar from './Sidebar.jsx';
import Topbar from './Topbar.jsx';

const COLLAPSE_KEY = 'pepperx.sidebarCollapsed';

export default function Layout() {
  const { endpoint } = useApp();
  const [collapsed, setCollapsed] = useState(() => {
    try {
      return window.localStorage.getItem(COLLAPSE_KEY) === 'true';
    } catch {
      return false;
    }
  });

  useEffect(() => {
    try {
      window.localStorage.setItem(COLLAPSE_KEY, collapsed ? 'true' : 'false');
    } catch {
      // Layout preference only; not worth surfacing a failure.
    }
  }, [collapsed]);

  if (!endpoint) return <Navigate to="/connect" replace />;

  return (
    <div className={`app-shell${collapsed ? ' is-collapsed' : ''}`}>
      <Sidebar collapsed={collapsed} onToggle={() => setCollapsed((previous) => !previous)} />
      <div className="app-main">
        <Topbar />
        <main className="app-content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
