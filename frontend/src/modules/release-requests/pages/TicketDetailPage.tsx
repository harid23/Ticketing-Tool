import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { releaseApi } from '../api';
import type { Attachment, AuditItem, Evaluation, HistoryItem, TicketDetail } from '../types';
import { AllocationCountdown, AllocationFlow, Card, Empty, ErrorBox, Field, fmtDate, fmtDateTime, Loading, SlaBadge, StatusBadge, Timeline } from '../components/Common';
import ActionDialog, { type DialogField } from '../components/ActionDialog';

type Dlg = { title: string; intro?: string; fields?: DialogField[]; confirm: string; run: (v: Record<string, string>) => Promise<unknown> } | null;

export default function TicketDetailPage() {
  const id = Number(useParams().ticketId);
  const [t, setT] = useState<TicketDetail | null>(null);
  const [history, setHistory] = useState<HistoryItem[]>([]);
  const [audit, setAudit] = useState<AuditItem[] | null>(null);
  const [files, setFiles] = useState<Attachment[]>([]);
  const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  const [dlg, setDlg] = useState<Dlg>(null);

  const load = useCallback(async () => {
    try {
      const d = await releaseApi.get(id); setT(d);
      const [h, a] = await Promise.all([releaseApi.history(id), releaseApi.attachments(id)]); setHistory(h); setFiles(a);
      setAudit(d.allowedActions.includes('viewAudit') ? await releaseApi.audit(id) : null);
      setError('');
    } catch (e) { setError((e as Error).message); }
  }, [id]);
  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    let cancelled = false;
    const refreshActivity = async () => {
      try {
        const [detail, items] = await Promise.all([releaseApi.get(id), releaseApi.history(id)]);
        if (!cancelled) {
          setT(detail);
          setHistory(items);
          setError('');
        }
      } catch (e) {
        if (!cancelled) setError((e as Error).message);
      }
    };
    const timer = window.setInterval(() => { void refreshActivity(); }, 10_000);
    return () => { cancelled = true; window.clearInterval(timer); };
  }, [id]);

  const act = (d: NonNullable<Dlg>) => setDlg(d);
  const done = async (msg: string) => { setDlg(null); setNotice(msg); await load(); };

  if (error && !t) return <ErrorBox message={error} />;
  if (!t) return <Loading />;
  const can = (a: string) => t.allowedActions.includes(a);
  const today = new Date().toISOString().slice(0, 10);

  const evalButtons = (e: Evaluation) => (<>
    {e.canRespond && <>
      <button onClick={() => act({ title: `Respond for ${e.clusterCode}`, intro: 'Your response is final and is time-stamped by the server.', confirm: 'Yes, interested', run: () => releaseApi.respond(id, e.evaluationId, 'YES_INTERESTED') })}>Yes</button>
      <button onClick={() => act({ title: `Respond for ${e.clusterCode}`, intro: 'Confirm that your cluster is NOT interested.', confirm: 'No, not interested', run: () => releaseApi.respond(id, e.evaluationId, 'NO_NOT_INTERESTED') })}>No</button></>}
    {e.canStart && <button onClick={() => act({ title: 'Start evaluation', confirm: 'Start', run: () => releaseApi.start(id, e.evaluationId) })}>Start evaluation</button>}
    {e.canComplete && <button onClick={() => act({
      title: `Record outcome for ${e.clusterCode}`, confirm: 'Save outcome',
      fields: [{ name: 'result', label: 'Result', type: 'select', required: true, options: [{ value: 'SELECTED', label: 'Selected' }, { value: 'NOT_SELECTED', label: 'Not selected' }] },
        { name: 'interviewDate', label: 'Interview date', type: 'date' }, { name: 'reason', label: 'Rejection reason (required if not selected)', type: 'textarea' }],
      run: v => releaseApi.complete(id, e.evaluationId, { selectionResult: v.result as 'SELECTED' | 'NOT_SELECTED', interviewDate: v.interviewDate || undefined, rejectionReason: v.reason || undefined }),
    })}>Record interview result</button>}
  </>);

  return (
    <div className="rr-detail-page">
      <header className="rr-detail-head">
        <h2>{t.ticketNumber}</h2><StatusBadge status={t.status} /><SlaBadge sla={t.slaStatus} deadline={t.evaluationDeadlineAt} />
        <span>Owner: <strong>{t.assignedToHead ? 'Head' : t.ownerCluster ?? '—'}</strong>{t.ownerName ? ` (${t.ownerName})` : ''}</span>
        <span>Employee: <strong>{t.employeeName}</strong> ({t.employeeId})</span>
        {t.reopenedFromTicketNumber && <span>Reopened from <Link to={`../${t.reopenedFromTicketId}`}>{t.reopenedFromTicketNumber}</Link></span>}
      </header>
      {notice && <p role="status" className="rr-success">{notice}</p>}
      {error && <ErrorBox message={error} />}

      <div className="rr-actions">
        {can('approveSlaException') && <button onClick={() => act({
          title: 'Approve one 14-day SLA extension?',
          intro: 'This keeps the request open for the ASDM manager to finish the allocation results. If it is still unresolved at the new deadline, the request will close automatically.',
          confirm: 'Approve extension',
          run: () => releaseApi.approveSlaException(id),
        })}>Approve 14-day extension</button>}
        {can('cancel') && <button onClick={() => act({ title: 'Cancel this ticket?', intro: 'Allowed only within 2 working days of identifying the internal opportunity. This cannot be undone.',
          fields: [{ name: 'on', label: 'Date the opportunity was identified', type: 'date', required: true, defaultValue: today }, { name: 'reason', label: 'Reason', type: 'textarea', required: true }],
          confirm: 'Cancel ticket', run: v => releaseApi.cancel(id, { reason: v.reason, opportunityIdentifiedOn: v.on }) })}>Cancel ticket</button>}
        {can('close') && <button onClick={() => act({ title: 'Close this ticket?', fields: [{ name: 'reason', label: 'Closure note', type: 'textarea', required: true }], confirm: 'Close ticket', run: v => releaseApi.close(id, v.reason) })}>Close ticket</button>}
        {can('reopen') && <button onClick={() => act({ title: 'Raise a reopened request', intro: 'A new ticket is created and linked to this one. The closed ticket is not changed.', fields: [{ name: 'reason', label: 'Reason / demand details', type: 'textarea', required: true }], confirm: 'Create reopened ticket', run: v => releaseApi.reopen(id, { reason: v.reason }) })}>Reopen</button>}
      </div>
      {t.slaExceptionApproved && <p className="rr-success">A one-time 14-day SLA extension is approved. New deadline: {fmtDateTime(t.evaluationDeadlineAt)}.</p>}

      <Card title="Allocation process">
        <AllocationCountdown deadline={t.evaluationDeadlineAt} active={t.status === 'OPEN' || t.status === 'WIP' || t.status === 'REOPENED'} />
        <AllocationFlow
          evaluations={t.evaluations.filter(evaluation => evaluation.response === 'YES_INTERESTED')}
          renderActions={evalButtons}
          emptyText={t.evaluations.length ? 'No clusters have expressed interest in this request yet.' : 'This ticket follows the direct release flow; no cluster circulation.'}
        />
      </Card>

      <Card title="Employee details"><dl className="rr-grid">
        <Field label="Name">{t.employeeName}</Field><Field label="Employee ID">{t.employeeId}</Field><Field label="Data source">{t.employeeSource === 'MASTER' ? 'Master' : 'Manual'}</Field>
        <Field label="Grade">{t.grade}</Field><Field label="Band">{t.band}</Field><Field label="PSL">{t.psl}</Field><Field label="Location">{t.location}</Field><Field label="Supervisor">{t.currentSupervisor}</Field></dl></Card>
      <Card title="Current project"><dl className="rr-grid"><Field label="Project ID">{t.currentProjectId}</Field><Field label="Project name">{t.currentProjectName}</Field></dl></Card>
      <Card title="Release details"><dl className="rr-grid">
        <Field label="Initiating cluster">{t.initiatingCluster}</Field><Field label="Category">{t.category}</Field><Field label="Release date">{fmtDate(t.releaseDate)}</Field><Field label="Release location">{t.releaseLocation}</Field>
        <Field label="Reason">{t.reasonForRelease}</Field><Field label="Recommended for new assignment">{t.recommendedForNewAssignment ? 'Yes' : 'No'}</Field>
        <Field label="Employee aware">{t.employeeAware ? 'Yes' : 'No'}</Field><Field label="Factored in forecast">{t.factoredInForecast ? 'Yes' : 'No'}</Field><Field label="Backfill required">{t.backfillRequired ? 'Yes' : 'No'}</Field>
        <Field label="Created by">{t.createdByName} · {fmtDateTime(t.createdAt)}</Field><Field label="SLA deadline">{fmtDateTime(t.evaluationDeadlineAt)}</Field>
        {t.closedAt && <Field label="Closed">{fmtDateTime(t.closedAt)} by {t.closedByName} ({t.closureReason})</Field>}
        {t.cancellationReason && <Field label="Cancellation reason">{t.cancellationReason}</Field>}</dl></Card>
      <Card title="Feedback"><dl><Field label="Skills">{t.employeeSkills}</Field><Field label="Feedback provided">{t.feedbackProvided}</Field>
        <Field label="Detailed feedback">{t.detailedFeedback}</Field><Field label="Commitment made">{t.commitmentMade}</Field><Field label="Remarks">{t.remarks}</Field></dl></Card>

      <Card title="Attachments" actions={can('attach') && (
        <label className="rr-button">Upload<input type="file" hidden onChange={async ev => {
          const file = ev.target.files?.[0]; if (!file) return;
          const kind = window.confirm('Is this a Softlock document? (OK = Softlock, Cancel = other / reopen email)') ? 'SOFTLOCK' : 'OTHER';
          try { await releaseApi.upload(id, kind, file); setNotice('File uploaded.'); await load(); } catch (e) { setError((e as Error).message); }
          ev.target.value = ''; }} /></label>)}>
        {!files.length ? <Empty text="No attachments." /> : <ul>{files.map(a => <li key={a.attachmentId}>
          <button className="rr-link" onClick={() => releaseApi.downloadAttachment(id, a.attachmentId, a.fileName).catch(e => setError(e.message))}>{a.fileName}</button>
          <span className="rr-muted"> · {a.kind} · {(a.fileSize / 1024).toFixed(0)} KB · {fmtDateTime(a.uploadedAt)}</span></li>)}</ul>}
      </Card>

      <Card title="Status timeline"><Timeline items={history} /></Card>

      {audit && <Card title="Audit history"><div className="rr-table-wrap"><table className="rr-table"><thead><tr><th>When</th><th>Who</th><th>Action</th><th>Old</th><th>New</th><th>IP</th></tr></thead>
        <tbody>{audit.map(a => <tr key={a.auditId}><td>{fmtDateTime(a.performedAt)}</td><td>{a.performedByName}</td><td>{a.action}</td><td><code>{a.oldValue}</code></td><td><code>{a.newValue}</code></td><td>{a.ipAddress}</td></tr>)}</tbody></table></div></Card>}

      <ActionDialog open={!!dlg} title={dlg?.title ?? ''} intro={dlg?.intro} fields={dlg?.fields} confirmLabel={dlg?.confirm ?? 'Confirm'}
        onCancel={() => setDlg(null)} onConfirm={async v => { await dlg!.run(v); await done('Done.'); }} />
    </div>
  );
}
