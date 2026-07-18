import { Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import RequireAuth from './auth/RequireAuth';
import LoginPage from './pages/LoginPage';
import HomePage from './pages/HomePage';
import HealthPage from './pages/HealthPage';
import ConnectorsPage from './pages/ConnectorsPage';
import ConnectionsPage from './pages/ConnectionsPage';
import FlowsPage from './pages/FlowsPage';
import FlowEditorPage from './pages/FlowEditorPage';
import RunsPage from './pages/RunsPage';
import DeadLetterPage from './pages/DeadLetterPage';

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<Layout />}>
          <Route index element={<HomePage />} />
          <Route path="connectors" element={<ConnectorsPage />} />
          <Route path="connections" element={<ConnectionsPage />} />
          <Route path="flows" element={<FlowsPage />} />
          <Route path="flows/new" element={<FlowEditorPage />} />
          <Route path="flows/:id" element={<FlowEditorPage />} />
          <Route path="runs" element={<RunsPage />} />
          <Route path="dead-letter" element={<DeadLetterPage />} />
          <Route path="health" element={<HealthPage />} />
          <Route path="*" element={<NotFound />} />
        </Route>
      </Route>
    </Routes>
  );
}

function NotFound() {
  return (
    <section>
      <h1>Not found</h1>
      <p>That page does not exist.</p>
    </section>
  );
}
