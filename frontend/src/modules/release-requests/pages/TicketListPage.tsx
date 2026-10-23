import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { releaseApi } from '../api';
import type { Cluster, Paged, TicketSummary } from '../types';
import { AllocationCountdown, Empty, ErrorBox, fmtDate, Loading, SlaBadge, StatusBadge } from '../components/Common';

/** Used for "Release Requests" (mine=false) and "My tickets" (mine=true). All filtering/paging happens on the server. */
export default function TicketListPage({ mine = false }: { mine?: boolean }) {
  const [filters, setFilters] = useState({ ticketNumber: '', employeeId: '', employeeName: '', clusterId: '', status: '', slaStatus: '' });
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState({ sortBy: 'createdAt', sortDirection: 'desc' });
  const [data, setData] = useState<Paged<TicketSummary> | null>(null);
  const [clusters, setClusters] = useState<Cluster[]>([]);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(true);
  const pageSize = 20;

  useEffect(() => { releaseApi.clusters().then(setClusters).catch(() => undefined); }, []);
  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setData(await releaseApi.list({ ...filters, clusterId: filters.clusterId || undefined, page, pageSize, ...sort, mine: mine || undefined })); }
    catch (e) { setError((e as Error).message); } finally { setLoading(false); }
  }, [filters, page, sort, mine]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    const refresh = () => { void load(); };
    const timer = window.setInterval(refresh, 10_000);
    window.addEventListener('focus', refresh);
    return () => {
      window.clearInterval(timer);
      window.removeEventListener('focus', refresh);
    };
  }, [load]);

  const f = (k: keyof typeof filters, v: string) => { setPage(1); setFilters(p => ({ ...p, [k]: v })); };
  const th = (label: string, key: string) => (
    <th><button className="rr-link" onClick={() => setSort(s => ({ sortBy: key, sortDirection: s.sortBy === key && s.sortDirection === 'asc' ? 'desc' : 'asc' }))}>
      {label}{sort.sortBy === key ? (sort.sortDirection === 'asc' ? ' ▲' : ' ▼') : ''}</button></th>);
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div>
      <div className="rr-row"><h2>{mine ? 'My tickets' : 'Release requests'}</h2><Link to="../new" className="rr-button">New request</Link></div>
      <div className="rr-filters">
        <input placeholder="Ticket no." value={filters.ticketNumber} onChange={e => f('ticketNumber', e.target.value)} aria-label="Ticket number" />
        <input placeholder="Employee ID" value={filters.employeeId} onChange={e => f('employeeId', e.target.value)} aria-label="Employee ID" />
        <input placeholder="Employee name" value={filters.employeeName} onChange={e => f('employeeName', e.target.value)} aria-label="Employee name" />
        <select value={filters.clusterId} onChange={e => f('clusterId', e.target.value)} aria-label="Cluster"><option value="">All clusters</option>{clusters.map(c => <option key={c.id} value={c.id}>{c.code}</option>)}</select>
        <select value={filters.status} onChange={e => f('status', e.target.value)} aria-label="Status"><option value="">All statuses</option>{['OPEN', 'WIP', 'CLOSED', 'CANCELLED', 'REOPENED'].map(s => <option key={s}>{s}</option>)}</select>
        <select value={filters.slaStatus} onChange={e => f('slaStatus', e.target.value)} aria-label="SLA"><option value="">Any SLA</option><option value="WITHIN_SLA">Within SLA</option><option value="BREACHED">Breached</option></select>
      </div>
      {error && <ErrorBox message={error} />}
      {loading && !data ? <Loading /> : !data?.items.length ? <Empty text="No release requests match these filters." /> : (
        <div className="rr-table-wrap"><table className="rr-table">
          <thead><tr>{th('Ticket', 'ticketNumber')}{th('Employee', 'employeeName')}<th>Owner</th>{th('Status', 'status')}{th('SLA', 'slaStatus')}{th('Release date', 'releaseDate')}<th>Timer</th></tr></thead>
          <tbody>{data.items.map(t => (
            <tr key={t.id}>
              <td><Link to={`../${t.id}`}>{t.ticketNumber}</Link></td><td>{t.employeeName}<div className="rr-muted">{t.employeeId}</div></td>
              <td>{t.assignedToHead ? 'Head' : t.ownerCluster ?? '—'}</td>
              <td><StatusBadge status={t.status} /></td><td><SlaBadge sla={t.slaStatus} deadline={t.evaluationDeadlineAt} /></td>
              <td>{fmtDate(t.releaseDate)}</td>
              <td><AllocationCountdown deadline={t.evaluationDeadlineAt} active={t.status === 'OPEN' || t.status === 'WIP' || t.status === 'REOPENED'} compact /></td>
            </tr>))}</tbody>
        </table></div>)}
      {data && <div className="rr-row"><button disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</button>
        <span>Page {page} of {pages} · {data.totalCount} tickets</span><button disabled={page >= pages} onClick={() => setPage(p => p + 1)}>Next</button></div>}
    </div>
  );
}
