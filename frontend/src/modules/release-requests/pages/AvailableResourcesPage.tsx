import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { releaseApi } from '../api';
import type { AvailableResource, Paged } from '../types';
import { Empty, ErrorBox, fmtDate, fmtDateTime, Loading } from '../components/Common';

export default function AvailableResourcesPage() {
  const [filters, setFilters] = useState({ search: '', grade: '', location: '' });
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<AvailableResource> | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setData(await releaseApi.availableResources({ ...filters, page, pageSize }));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load available resources.');
    } finally {
      setLoading(false);
    }
  }, [filters, page]);

  useEffect(() => { void load(); }, [load]);

  function updateFilter(key: keyof typeof filters, value: string) {
    setPage(1);
    setFilters(current => ({ ...current, [key]: value }));
  }

  async function showInterest(resource: AvailableResource) {
    if (resource.evaluationId === null) return;
    setBusyId(resource.ticketId);
    setError('');
    setNotice('');
    try {
      await releaseApi.respond(resource.ticketId, resource.evaluationId, 'YES_INTERESTED');
      setNotice(`Interest recorded for ${resource.ticketNumber}. Its original SLA deadline is unchanged.`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not record interest.');
    } finally {
      setBusyId(null);
    }
  }

  const pageCount = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <section className="rr-list-page">
      <header className="rr-row">
        <div><p className="rr-inbox-eyebrow">Resourcing</p><h1>Available Resources</h1></div>
        <Link to="/release-requests/new" className="rr-button">Create request</Link>
      </header>
      <p className="rr-muted">Browse released employees available for allocation. The SLA is based on the original request date.</p>
      <div className="rr-filters" aria-label="Filter available resources">
        <input value={filters.search} onChange={e => updateFilter('search', e.target.value)} placeholder="Search name, ID, skills, project or ticket" aria-label="Search resources" />
        <input value={filters.grade} onChange={e => updateFilter('grade', e.target.value)} placeholder="Grade" aria-label="Filter by grade" />
        <input value={filters.location} onChange={e => updateFilter('location', e.target.value)} placeholder="Location" aria-label="Filter by location" />
      </div>
      {notice && <p role="status" className="rr-success">{notice}</p>}
      {error && <ErrorBox message={error} />}
      {loading && !data ? <Loading /> : !data?.items.length ? <Empty text="No resources are currently available." /> : (
        <div className="rr-table-wrap">
          <table className="rr-table">
            <thead><tr><th>Resource</th><th>Skills / Technology</th><th>Grade &amp; Location</th><th>Current project</th><th>Request details</th><th>SLA deadline</th><th>Action</th></tr></thead>
            <tbody>{data.items.map(resource => (
              <tr key={resource.ticketId}>
                <td><strong>{resource.employeeName}</strong><div className="rr-muted">{resource.employeeId}</div></td>
                <td>{resource.employeeSkills}</td>
                <td>{resource.grade}<div className="rr-muted">{resource.location}</div></td>
                <td>{resource.currentProjectName}</td>
                <td><Link to={`/release-requests/${resource.ticketId}`}>{resource.ticketNumber}</Link><div className="rr-muted">{resource.category} · {resource.initiatingCluster}</div><div className="rr-muted">Release {fmtDate(resource.releaseDate)}</div></td>
                <td>{resource.evaluationDeadlineAt ? fmtDateTime(resource.evaluationDeadlineAt) : '—'}<div className="rr-muted">Created {fmtDate(resource.createdAt)}</div></td>
                <td>{resource.canShowInterest && resource.evaluationId !== null
                  ? <button type="button" disabled={busyId !== null} onClick={() => void showInterest(resource)}>{busyId === resource.ticketId ? 'Saving…' : 'Show Interest'}</button>
                  : resource.myAllocationStatus ? <span>{resource.myAllocationStatus.replace(/_/g, ' ')}</span> : <span className="rr-muted">View only</span>}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {data && <div className="rr-row rr-pagination">
        <button type="button" disabled={page <= 1 || loading} onClick={() => setPage(p => p - 1)}>Previous</button>
        <span>Page {page} of {pageCount} · {data.totalCount} resources</span>
        <button type="button" disabled={page >= pageCount || loading} onClick={() => setPage(p => p + 1)}>Next</button>
      </div>}
    </section>
  );
}
