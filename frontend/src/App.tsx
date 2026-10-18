import { useEffect, useState } from 'react';
import { BrowserRouter, NavLink, Navigate, Route, Routes } from 'react-router-dom';
import { useMsal } from '@azure/msal-react';
import { configureReleaseApi, createReleaseRequestRoutes } from './modules/release-requests';
import { releaseApi } from './modules/release-requests/api';
import NotificationsBell from './modules/release-requests/components/NotificationsBell';
import type { Cluster, Me } from './modules/release-requests/types';
import { apiScope, getApiAccessToken, isEntraConfigured, isLocalDevelopment } from './auth';
import './modules/release-requests/release-requests.css';

const apiBase = import.meta.env.VITE_RELEASE_API_URL ?? (import.meta.env.DEV ? 'http://localhost:5000' : '');
const localRoles = ['PM', 'ASDM', 'Admin'] as const;
type LocalRole = (typeof localRoles)[number];
type LocalDemoSession = { role: LocalRole; clusterId: number | null };
const DEMO_ADMIN_PASSWORD = 'admin123';

function readLocalDemoSession(): LocalDemoSession | null {
  if (sessionStorage.getItem('release-local-authenticated') !== 'true') return null;
  const role = sessionStorage.getItem('release-local-role');
  const clusterId = sessionStorage.getItem('release-local-cluster');
  if (!localRoles.includes(role as LocalRole)) return null;
  if (role === 'ASDM' && (!clusterId || !/^[1-5]$/.test(clusterId))) return null;
  return { role: role as LocalRole, clusterId: role === 'ASDM' ? Number(clusterId) : null };
}

configureReleaseApi({
  baseUrl: apiBase,
  getAccessToken: getApiAccessToken,
  getHeaders: () => {
    if (!isLocalDevelopment) return {};
    const role = sessionStorage.getItem('release-local-role');
    const clusterId = sessionStorage.getItem('release-local-cluster');
    return role ? { 'X-Local-Dev-Role': role, ...(clusterId ? { 'X-Local-Dev-Cluster-Id': clusterId } : {}) } : {};
  },
});

function SetupRequired() {
  return <main className="rr-auth-page"><section className="rr-auth-card">
    <p className="rr-eyebrow">Release Ticketing</p><h1>Entra sign-in is not configured</h1>
    <p>Set the API URL, tenant ID, SPA client ID, and API delegated scope in the frontend environment, then restart or redeploy the app.</p>
    <p className="rr-auth-hint">Required: VITE_RELEASE_API_URL, VITE_ENTRA_TENANT_ID, VITE_ENTRA_SPA_CLIENT_ID, VITE_ENTRA_API_SCOPE</p>
  </section></main>;
}

function SignIn() {
  const { instance, inProgress } = useMsal();
  const [error, setError] = useState('');
  const signIn = async () => {
    setError('');
    try { await instance.loginRedirect({ scopes: [apiScope] }); }
    catch (e) { setError(e instanceof Error ? e.message : 'Sign-in failed.'); }
  };

  return <main className="rr-auth-page"><section className="rr-auth-card">
    <p className="rr-eyebrow">Release Ticketing</p><h1>Sign in to continue</h1>
    <p>Use your organization account. Your access and cluster actions are determined by your Entra app role.</p>
    {error && <p role="alert" className="rr-auth-error">{error}</p>}
    <button className="rr-auth-button" disabled={inProgress !== 'none'} onClick={signIn}>
      {inProgress === 'none' ? 'Sign in with Microsoft' : 'Redirecting to Microsoft…'}
    </button>
  </section></main>;
}

function LocalDemoSignIn({ onSignIn }: { onSignIn: (session: LocalDemoSession) => void }) {
  const [role, setRole] = useState<LocalRole | null>(null);
  const [clusters, setClusters] = useState<Cluster[]>([]);
  const [clusterId, setClusterId] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loadingClusters, setLoadingClusters] = useState(false);

  useEffect(() => {
    if (role !== 'ASDM') return;
    let cancelled = false;
    setLoadingClusters(true);
    setError('');
    releaseApi.clusters()
      .then(items => { if (!cancelled) setClusters(items); })
      .catch((loadError: unknown) => {
        if (!cancelled) setError(loadError instanceof Error ? loadError.message : 'Could not load clusters.');
      })
      .finally(() => { if (!cancelled) setLoadingClusters(false); });
    return () => { cancelled = true; };
  }, [role]);

  const selectRole = (nextRole: LocalRole) => {
    setRole(nextRole);
    setError('');
    setClusterId('');
    setPassword('');
  };

  const completeSignIn = () => {
    setError('');
    if (role === 'ASDM') {
      const selectedCluster = clusters.find(cluster => cluster.id === Number(clusterId));
      if (!selectedCluster) {
        setError('Choose a cluster to continue as ASDM.');
        return;
      }
      onSignIn({ role, clusterId: selectedCluster.id });
      return;
    }
    if (role === 'Admin') {
      if (password !== DEMO_ADMIN_PASSWORD) {
        setError('That admin password is incorrect.');
        return;
      }
      onSignIn({ role, clusterId: null });
      return;
    }
    if (role === 'PM') onSignIn({ role, clusterId: null });
  };

  return <main className="rr-auth-page"><section className="rr-auth-card rr-demo-auth-card">
    <p className="rr-eyebrow">Release Ticketing · Local demo</p>
    <h1>Sign in</h1>
    <p>Choose a role to open its local testing workspace.</p>
    <div className="rr-demo-role-options" role="group" aria-label="Choose a demo role">
      {localRoles.map(option => <button
        key={option}
        type="button"
        className={`rr-demo-role${role === option ? ' is-selected' : ''}`}
        aria-pressed={role === option}
        onClick={() => selectRole(option)}
      >
        <strong>{option}</strong>
        <span>{option === 'PM' ? 'Release request access' : option === 'ASDM' ? 'Cluster allocation access' : 'Admin access'}</span>
      </button>)}
    </div>
    {role === 'ASDM' && <label className="rr-demo-field">
      <span>Choose a cluster</span>
      <select aria-label="Choose a cluster" value={clusterId} onChange={event => setClusterId(event.target.value)} disabled={loadingClusters || clusters.length === 0}>
        <option value="">{loadingClusters ? 'Loading clusters…' : 'Select a cluster'}</option>
        {clusters.map(cluster => <option key={cluster.id} value={cluster.id}>{cluster.code} · {cluster.name}</option>)}
      </select>
    </label>}
    {role === 'Admin' && <label className="rr-demo-field">
      <span>Admin password</span>
      <input type="password" aria-label="Admin password" autoComplete="current-password" value={password}
        onChange={event => setPassword(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') completeSignIn(); }} />
      <small>Local demo password: admin123</small>
    </label>}
    {error && <p role="alert" className="rr-auth-error">{error}</p>}
    {role && <button type="button" className="rr-auth-button" disabled={role === 'ASDM' && (loadingClusters || clusters.length === 0)}
      onClick={completeSignIn}>
      {role === 'PM' ? 'Continue as PM' : `Sign in as ${role}`}
    </button>}
    <p className="rr-demo-disclaimer">This demo sign-in is only for local testing; it is not production authentication.</p>
  </section></main>;
}

function AuthenticatedApp({ localSession, onLocalSignOut }: {
  localSession: LocalDemoSession | null;
  onLocalSignOut: () => void;
}) {
  const { instance, accounts } = useMsal();
  const account = instance.getActiveAccount() ?? accounts[0];
  const [profile, setProfile] = useState<Me | null>(null);
  const [profileError, setProfileError] = useState('');

  useEffect(() => {
    if (!account && !isLocalDevelopment) return;
    if (account) instance.setActiveAccount(account);
    let cancelled = false;
    releaseApi.me().then((me: Me) => { if (!cancelled) setProfile(me); })
      .catch((error: unknown) => { if (!cancelled) setProfileError(error instanceof Error ? error.message : 'Could not load your profile.'); });
    return () => { cancelled = true; };
  }, [account, instance, localSession]);

  const signOut = () => {
    if (isLocalDevelopment) {
      onLocalSignOut();
      return;
    }
    void instance.logoutRedirect({ account: account ?? undefined });
  };

  return <BrowserRouter>
    <div className="rr-app-shell">
      <header className="rr-app-header">
        <div className="rr-brand">
          <span className="rr-brand-mark" aria-hidden="true">R</span>
          <div><strong>Release management</strong><span>Service operations workspace</span></div>
        </div>
        <div className="rr-app-user">
          {profile && <NotificationsBell profile={profile} />}
          <div className="rr-user-summary">
            <strong>{isLocalDevelopment ? 'Local demo' : (account?.name ?? account?.username)}</strong>
            <span>{profile ? `${profile.roles.join(', ') || 'No app roles'}${profile.clusterId ? ` · Cluster ${profile.clusterId}` : ''}` : profileError || 'Loading access…'}</span>
          </div>
          <button type="button" className="rr-auth-link" onClick={signOut}>Sign out</button>
        </div>
      </header>
      <div className="rr-main-layout">
        <nav className="rr-sidebar" aria-label="Main navigation">
          <p className="rr-nav-heading">WORKSPACE</p>
          <NavLink to="/release-requests" end><span aria-hidden="true">▦</span> Overview</NavLink>
          <NavLink to={profile?.roles.includes('Admin') ? '/release-requests/list' : '/release-requests/mine'}>
            <span aria-hidden="true">☷</span>{profile?.roles.includes('Admin') ? 'All requests' : 'My requests'}
          </NavLink>
          <NavLink to="/release-requests/new"><span aria-hidden="true">＋</span> Create request</NavLink>
          <p className="rr-nav-heading">RESOURCE MANAGEMENT</p>
          <NavLink to="/release-requests/available"><span aria-hidden="true">◫</span> Available resources</NavLink>
          {profile?.roles.some(role => role === 'PM' || role === 'ASDM') && <NavLink to="/release-requests/owned"><span aria-hidden="true">◷</span> My owned tickets</NavLink>}
          {profile?.roles.includes('ASDM') && <NavLink to="/release-requests/cluster-inbox"><span aria-hidden="true">▤</span> Cluster evaluation</NavLink>}
          <div className="rr-sidebar-footer"><span className="rr-sidebar-indicator" /> Workspace operational</div>
        </nav>
        <div className="rr-main-content"><BrowserRouterContent isAdmin={profile ? profile.roles.includes('Admin') : null} /></div>
      </div>
    </div>
  </BrowserRouter>;
}

function BrowserRouterContent({ isAdmin }: { isAdmin: boolean | null }) {
  return <Routes>
    <Route path="/" element={<Navigate to="/release-requests" replace />} />
    {createReleaseRequestRoutes(isAdmin)}
  </Routes>;
}

export default function App() {
  const { accounts } = useMsal();
  const [localSession, setLocalSession] = useState<LocalDemoSession | null>(() => isLocalDevelopment ? readLocalDemoSession() : null);

  const signInLocally = (session: LocalDemoSession) => {
    sessionStorage.setItem('release-local-authenticated', 'true');
    sessionStorage.setItem('release-local-role', session.role);
    if (session.clusterId === null) sessionStorage.removeItem('release-local-cluster');
    else sessionStorage.setItem('release-local-cluster', String(session.clusterId));
    window.history.replaceState({}, '', '/release-requests');
    setLocalSession(session);
  };

  const signOutLocally = () => {
    sessionStorage.removeItem('release-local-authenticated');
    sessionStorage.removeItem('release-local-role');
    sessionStorage.removeItem('release-local-cluster');
    window.history.replaceState({}, '', '/');
    setLocalSession(null);
  };

  if (isLocalDevelopment) {
    return localSession
      ? <AuthenticatedApp key={`${localSession.role}-${localSession.clusterId ?? 'none'}`} localSession={localSession} onLocalSignOut={signOutLocally} />
      : <LocalDemoSignIn onSignIn={signInLocally} />;
  }
  if (!isEntraConfigured || !apiBase) return <SetupRequired />;
  if (!accounts.length) return <SignIn />;
  return <AuthenticatedApp localSession={null} onLocalSignOut={() => undefined} />;
}
