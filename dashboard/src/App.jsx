/**
 * Routes.
 *
 * Everything except `/connect` sits behind `Layout`, which redirects to the connect screen when no
 * endpoint is set — there is no useful state to render without a node.
 */

import React from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';

import { AppProvider } from '@context/AppContext.jsx';
import Layout from '@components/Layout.jsx';
import SetupWizard from '@components/SetupWizard.jsx';
import ToastHost from '@components/Toast.jsx';
import ApiExplorerView from '@views/ApiExplorerView.jsx';
import CapacityView from '@views/CapacityView.jsx';
import ConnectView from '@views/ConnectView.jsx';
import ContainersView from '@views/ContainersView.jsx';
import HomeView from '@views/HomeView.jsx';
import ObjectsView from '@views/ObjectsView.jsx';
import ObservabilityView from '@views/ObservabilityView.jsx';
import RequestHistoryView from '@views/RequestHistoryView.jsx';
import SearchView from '@views/SearchView.jsx';
import SettingsView from '@views/SettingsView.jsx';
import UploadsView from '@views/UploadsView.jsx';

export default function App() {
  return (
    <AppProvider>
      <Routes>
        <Route path="/connect" element={<ConnectView />} />
        <Route element={<Layout />}>
          <Route index element={<HomeView />} />
          <Route path="containers" element={<ContainersView />} />
          <Route path="containers/:container" element={<ObjectsView />} />
          <Route path="objects" element={<ObjectsView />} />
          <Route path="uploads" element={<UploadsView />} />
          <Route path="search" element={<SearchView />} />
          <Route path="capacity" element={<CapacityView />} />
          <Route path="requests" element={<RequestHistoryView />} />
          <Route path="observability" element={<ObservabilityView />} />
          <Route path="explorer" element={<ApiExplorerView />} />
          <Route path="settings" element={<SettingsView />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>

      <SetupWizard />
      <ToastHost />
    </AppProvider>
  );
}
