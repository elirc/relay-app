import { NavLink, Outlet } from 'react-router-dom';
import type { CSSProperties } from 'react';

const nav: { to: string; label: string }[] = [
  { to: '/', label: 'Home' },
  { to: '/connectors', label: 'Connectors' },
  { to: '/connections', label: 'Connections' },
  { to: '/flows', label: 'Flows' },
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
      </header>
      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
