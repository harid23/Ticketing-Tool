import { Navigate, Route } from 'react-router-dom';
import CreateReleaseRequestPage from './pages/CreateReleaseRequestPage';
import DashboardPage from './pages/DashboardPage';
import ClusterEvaluationInboxPage from './pages/ClusterEvaluationInboxPage';
import TicketDetailPage from './pages/TicketDetailPage';
import TicketListPage from './pages/TicketListPage';
import AvailableResourcesPage from './pages/AvailableResourcesPage';
import OwnedTicketsPage from './pages/OwnedTicketsPage';
import './release-requests.css';

export { configureReleaseApi } from './api';

/**
 * Add inside the existing <Routes> of the APEX app (react-router v6), e.g.:
 *   {releaseRequestRoutes}
 * and add ONE link in the existing sidebar pointing to /release-requests.
 */
export function createReleaseRequestRoutes(isAdmin: boolean | null = false) {
  return (
  <Route path="release-requests">
    <Route index element={<DashboardPage />} />
    <Route path="list" element={<TicketListPage />} />
    <Route path="mine" element={<TicketListPage mine />} />
    <Route path="cluster-inbox" element={<ClusterEvaluationInboxPage />} />
    <Route path="available" element={<AvailableResourcesPage />} />
    <Route path="owned" element={isAdmin === null
      ? <p role="status">Loading access…</p>
      : isAdmin
        ? <Navigate to="/release-requests/available" replace />
        : <OwnedTicketsPage />} />
    <Route path="new" element={<CreateReleaseRequestPage />} />
    <Route path=":ticketId" element={<TicketDetailPage />} />
  </Route>
  );
}

export const releaseRequestRoutes = createReleaseRequestRoutes();
