import { Route, Routes } from 'react-router-dom';
import Layout from './components/Layout';
import HomePage from './pages/HomePage';
import HealthPage from './pages/HealthPage';
import ConnectorsPage from './pages/ConnectorsPage';
import ConnectionsPage from './pages/ConnectionsPage';
import FlowsPage from './pages/FlowsPage';
import FlowEditorPage from './pages/FlowEditorPage';
import RunsPage from './pages/RunsPage';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="connectors" element={<ConnectorsPage />} />
        <Route path="connections" element={<ConnectionsPage />} />
        <Route path="flows" element={<FlowsPage />} />
        <Route path="flows/new" element={<FlowEditorPage />} />
        <Route path="flows/:id" element={<FlowEditorPage />} />
        <Route path="runs" element={<RunsPage />} />
        <Route path="health" element={<HealthPage />} />
        <Route path="*" element={<NotFound />} />
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
