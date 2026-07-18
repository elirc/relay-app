import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import type { CSSProperties } from 'react';
import { useAuth } from '../auth/AuthContext';

const nav: { to: string; label: string }[] = [
  { to: '/', label: 'Home' },
  { to: '/dashboard', label: 'Dashboard' },
  { to: '/connectors', label: 'Connectors' },
  { to: '/connections', label: 'Connections' },
  { to: '/templates', label: 'Templates' },
  { to: '/flows', label: 'Flows' },
  { to: '/runs', label: 'Runs' },
  { to: '/dead-letter', label: 'Dead-letter' },
  { to: '/health', label: 'Health' },
];

function linkStyle({ isActive }: { isActive: boolean }): CSSProperties {
  return {
    padding: '0.4rem 0.8rem',
    borderRadius: 6,
    textDecoration: 'none',
    color: isActive ? '#fff' : '#cbd5e1',
    background: isActive ? '#6366f1' : 'transparent',
  };
}

export default function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  function onLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <span className="app-brand">relay</span>
        <nav className="app-nav">
          {nav.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.to === '/'} style={linkStyle}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        {user && (
          <div className="app-user">
            <span className="app-user-name">
              {user.displayName} · <span className="badge">{user.role}</span>
            </span>
            <button type="button" onClick={onLogout}>
              Sign out
            </button>
          </div>
        )}
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
