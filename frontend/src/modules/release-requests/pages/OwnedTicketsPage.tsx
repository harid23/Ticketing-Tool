import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { releaseApi } from '../api';
import type { AllocationStatus, AllocationTicket, EvaluationStatus } from '../types';
import { Empty, ErrorBox, fmtDateTime, Loading } from '../components/Common';

const labels: Record<EvaluationStatus, string> = {
  PENDING: 'Pending interest',
  NOT_INTERESTED: 'Not interested',
  INTERESTED: 'Interested',
  ASSIGNED: 'Interested',
  IN_PROGRESS: 'Interview scheduled',
  INTERVIEW_SCHEDULED: 'Interview scheduled',
  WAITING_FOR_RESULTS: 'Waiting for results',
  SELECTED: 'Selected',
  NOT_SELECTED: 'Rejected',
  REJECTED: 'Rejected',
  CLOSED: 'Closed by another cluster',
};

function nextStatuses(status: EvaluationStatus): AllocationStatus[] {
  if (status === 'INTERESTED' || status === 'ASSIGNED') return ['INTERVIEW_SCHEDULED', 'REJECTED'];
  if (status === 'IN_PROGRESS' || status === 'INTERVIEW_SCHEDULED') return ['WAITING_FOR_RESULTS', 'REJECTED'];
  if (status === 'WAITING_FOR_RESULTS') return ['SELECTED', 'REJECTED'];
  return [];
}

function remaining(deadline: string | null, now: number): string {
  if (!deadline) return '—';
  const milliseconds = new Date(deadline).getTime() - now;
  if (milliseconds <= 0) return 'SLA expired';
  const hours = Math.floor(milliseconds / 3_600_000);
  const days = Math.floor(hours / 24);
  const remainder = hours % 24;
  return days > 0 ? `${days}d ${remainder}h` : `${remainder}h`;
}

export default function OwnedTicketsPage() {
  const [items, setItems] = useState<AllocationTicket[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [now, setNow] = useState(Date.now());
  const [view, setView] = useState<'open' | 'closed'>('open');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try { setItems(await releaseApi.ownedAllocationTickets()); }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not load allocation tickets.'); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 60_000);
    return () => window.clearInterval(timer);
  }, []);

  async function updateStatus(ticket: AllocationTicket, status: AllocationStatus) {
    let rejectionReason: string | undefined;
    if (status === 'SELECTED' && !window.confirm(`Select ${ticket.employeeName}? This closes other active cluster allocation tickets for this resource.`)) return;
    if (status === 'REJECTED') {
      rejectionReason = window.prompt('Enter a reason for rejecting this resource:')?.trim();
      if (!rejectionReason) return;
    }

    setBusyId(ticket.evaluationId);
    setError('');
    setNotice('');
    try {
      await releaseApi.updateAllocationStatus(ticket.ticketId, ticket.evaluationId, status, rejectionReason);
      setNotice(`${ticket.ticketNumber}: status updated to ${labels[status]}.`);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not update allocation status.');
    } finally {
      setBusyId(null);
    }
  }

  const allGroups = [...(items ?? []).reduce((tickets, item) => {
    const allocations = tickets.get(item.ticketId) ?? [];
    allocations.push(item);
    tickets.set(item.ticketId, allocations);
    return tickets;
  }, new Map<number, AllocationTicket[]>()).values()];
  const isOpenAllocation = (item: AllocationTicket) =>
    ['OPEN', 'WIP', 'REOPENED'].includes(item.ticketStatus) &&
    !['SELECTED', 'REJECTED', 'NOT_SELECTED', 'CLOSED'].includes(item.status);
  const openGroups = allGroups.filter(allocations => allocations.some(isOpenAllocation));
  const closedGroups = allGroups.filter(allocations => !allocations.some(isOpenAllocation));
  const groups = view === 'open' ? openGroups : closedGroups;

  return (
    <section className="rr-list-page">
      <header className="rr-row">
        <div><p className="rr-inbox-eyebrow">Cluster workspace</p><h1>My Owned Tickets</h1></div>
        <button type="button" onClick={() => void load()} disabled={loading}>Refresh</button>
      </header>
      <p className="rr-muted">Open allocations are still being worked. Closed allocations have been selected, rejected, or closed with the request.</p>
      <div className="rr-owned-tabs" role="tablist" aria-label="Owned ticket status">
        <button type="button" role="tab" aria-selected={view === 'open'} onClick={() => setView('open')}>Open ({openGroups.length})</button>
        <button type="button" role="tab" aria-selected={view === 'closed'} onClick={() => setView('closed')}>Closed ({closedGroups.length})</button>
      </div>
      {notice && <p role="status" className="rr-success">{notice}</p>}
      {error && <ErrorBox message={error} />}
      {loading && !items ? <Loading /> : !groups.length ? <Empty text={view === 'open' ? 'No open allocation tickets.' : 'No closed allocation tickets.'} /> : (
        <div className="rr-table-wrap">
          <table className="rr-table">
            <thead><tr><th>Ticket / Resource</th><th>Project &amp; Skills</th><th>Cluster progress</th><th>SLA deadline</th><th>Time remaining</th><th>Actions</th></tr></thead>
            <tbody>{groups.map(allocations => {
              const ticket = allocations[0];
              const manageable = allocations.filter(item => item.canUpdateStatus);
              return <tr key={ticket.ticketId}>
                <td><Link to={`/release-requests/${ticket.ticketId}`}>{ticket.ticketNumber} · View process</Link><div><strong>{ticket.employeeName}</strong></div><div className="rr-muted">{ticket.employeeId} · {ticket.grade} · {ticket.location}</div></td>
                <td>{ticket.currentProjectName}<div className="rr-muted">{ticket.employeeSkills}</div></td>
                <td><div className="rr-ticket-allocation-list">{allocations.map(allocation => <div className="rr-ticket-allocation" key={allocation.evaluationId}>
                  <strong>{allocation.clusterCode}</strong>
                  <span className={`rr-status rr-status-${allocation.status.toLowerCase().replace(/_/g, '-')}`}>{labels[allocation.status]}</span>
                  {allocation.rejectionReason && <span className="rr-muted">{allocation.rejectionReason}</span>}
                </div>)}</div></td>
                <td>{ticket.evaluationDeadlineAt ? fmtDateTime(ticket.evaluationDeadlineAt) : '—'}</td>
                <td>{remaining(ticket.evaluationDeadlineAt, now)}</td>
                <td><div className="rr-ticket-allocation-list">{manageable.map(allocation => {
                  const options = nextStatuses(allocation.status);
                  return <div className="rr-ticket-allocation" key={allocation.evaluationId}>
                    <span className="rr-muted">{allocation.clusterCode}</span>
                    {options.length ? <select
                      aria-label={`Update ${ticket.ticketNumber} ${allocation.clusterCode} allocation status`}
                      value=""
                      disabled={busyId !== null}
                      onChange={e => { const status = e.target.value as AllocationStatus; if (status) void updateStatus(allocation, status); }}
                    >
                      <option value="">Update status…</option>
                      {options.map(status => <option key={status} value={status}>{labels[status]}</option>)}
                    </select> : <span className="rr-muted">No further action</span>}
                  </div>;
                })}{!manageable.length && <span className="rr-muted">View only</span>}</div></td>
              </tr>;
            })}</tbody>
          </table>
        </div>
      )}
    </section>
  );
}
