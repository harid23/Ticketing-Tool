import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, releaseApi } from '../api';
import type { ClusterEvaluationInboxItem, ClusterResponse } from '../types';
import { Empty, ErrorBox, fmtDate, fmtDateTime, Loading } from '../components/Common';

export default function ClusterEvaluationInboxPage() {
  const [items, setItems] = useState<ClusterEvaluationInboxItem[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try { setItems(await releaseApi.pendingClusterEvaluations()); }
    catch (e) {
      setError(e instanceof ApiError && e.status === 403
        ? 'This account needs the ASDM role and a cluster assignment to view this inbox.'
        : (e as Error).message);
    } finally { setLoading(false); }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function respond(item: ClusterEvaluationInboxItem, response: ClusterResponse) {
    setBusyId(item.evaluationId);
    setError('');
    setNotice('');
    try {
      await releaseApi.respond(item.ticketId, item.evaluationId, response);
      setNotice(`${item.ticketNumber}: response saved. The ticket has been removed from your pending inbox.`);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusyId(null);
    }
  }

  return (
    <main className="rr-dashboard-page rr-inbox-page">
      <header className="rr-inbox-heading">
        <div>
          <p className="rr-inbox-eyebrow">Cluster workspace</p>
          <h1>Candidate evaluations</h1>
          <p>Review candidates submitted to your cluster and record whether your team is interested.</p>
        </div>
        <button type="button" onClick={() => void load()} disabled={loading}>Refresh</button>
      </header>

      <div className="rr-inbox-summary">
        <span className="rr-inbox-count">{items?.length ?? '—'}</span>
        <span><strong>Awaiting your response</strong><small>Only pending evaluations for your assigned cluster appear here.</small></span>
      </div>

      {notice && <p role="status" className="rr-inbox-notice">{notice}</p>}
      {error && <ErrorBox message={error} />}
      {loading && !items ? <Loading /> : !error && !items?.length ? (
        <section className="rr-inbox-empty"><span aria-hidden="true">✓</span><h2>You’re all caught up</h2><p>There are no candidates awaiting a response from your cluster.</p></section>
      ) : items?.length ? (
        <div className="rr-inbox-grid">
          {items.map(item => (
            <article className="rr-inbox-card" key={item.evaluationId}>
              <div className="rr-inbox-card-top">
                <div><span className="rr-inbox-label">Candidate</span><h2>{item.employeeName}</h2><p>{item.employeeId} <span aria-hidden="true">·</span> {item.grade}</p></div>
                <span className="rr-inbox-ticket">{item.ticketNumber}</span>
              </div>
              <div className="rr-inbox-facts">
                <div><span>Current project</span><strong>{item.currentProjectName || '—'}</strong></div>
                <div><span>Location</span><strong>{item.location || '—'}</strong></div>
                <div><span>Release date</span><strong>{fmtDate(item.releaseDate)}</strong></div>
                <div><span>Release location</span><strong>{item.releaseLocation || '—'}</strong></div>
              </div>
              <div className="rr-inbox-description"><span>Skills</span><p>{item.employeeSkills || 'Not provided'}</p></div>
              <div className="rr-inbox-description"><span>Reason for release</span><p>{item.reasonForRelease}</p></div>
              <div className="rr-inbox-footer">
                <span>Respond by {fmtDateTime(item.evaluationDeadlineAt)}</span>
                <Link to={`/release-requests/${item.ticketId}`}>View ticket details</Link>
              </div>
              <div className="rr-inbox-actions">
                <button type="button" className="rr-inbox-no" disabled={busyId !== null} onClick={() => void respond(item, 'NO_NOT_INTERESTED')}>
                  {busyId === item.evaluationId ? 'Saving…' : 'Not interested'}
                </button>
                <button type="button" className="rr-inbox-yes" disabled={busyId !== null} onClick={() => void respond(item, 'YES_INTERESTED')}>
                  {busyId === item.evaluationId ? 'Saving…' : 'Interested'}
                </button>
              </div>
            </article>
          ))}
        </div>
      ) : null}
      {!error && !loading && items && items.length > 0 && <Empty text="Responses are final. Interested candidates enter the assignment queue in response order." />}
    </main>
  );
}
