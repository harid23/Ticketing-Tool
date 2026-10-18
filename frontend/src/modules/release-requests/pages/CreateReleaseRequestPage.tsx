import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiError, releaseApi } from '../api';
import { MIN_FEEDBACK_LENGTH, type Cluster, type CreateTicketInput, type ReleaseCategory } from '../types';
import { ErrorBox } from '../components/Common';

const empty: CreateTicketInput = {
  employeeId: '', employeeName: '', clusterId: null, releaseCategoryId: null, releaseDate: '', reasonForRelease: '',
  recommendedForNewAssignment: false, employeeAware: false, employeeSkills: '', feedbackProvided: '', detailedFeedback: '', commitmentMade: '',
  factoredInForecast: false, grade: '', location: '', currentSupervisor: '', currentProjectId: '', currentProjectName: '', remarks: '',
  releaseLocation: '', psl: '', band: '', backfillRequired: false,
};

export default function CreateReleaseRequestPage() {
  const nav = useNavigate();
  const [f, setF] = useState<CreateTicketInput>(empty);
  const [clusters, setClusters] = useState<Cluster[]>([]);
  const [cats, setCats] = useState<ReleaseCategory[]>([]);
  const [source, setSource] = useState<'MASTER' | 'MANUAL' | null>(null);
  const [lookupMsg, setLookupMsg] = useState('');
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  useEffect(() => { releaseApi.clusters().then(setClusters).catch(e => setError(e.message)); releaseApi.categories().then(setCats).catch(e => setError(e.message)); }, []);
  const set = <K extends keyof CreateTicketInput>(k: K, v: CreateTicketInput[K]) => setF(p => ({ ...p, [k]: v }));

  async function lookup() {
    setLookupMsg(''); if (!f.employeeId.trim()) return;
    try {
      const e = await releaseApi.employee(f.employeeId.trim());
      setF(p => ({ ...p, employeeName: e.name, grade: e.grade ?? '', location: e.location ?? '', currentSupervisor: e.supervisor ?? '', currentProjectId: e.projectId ?? '',
        currentProjectName: e.projectName ?? '', psl: e.psl ?? '', band: e.band ?? '', employeeSkills: p.employeeSkills || (e.skills ?? '') }));
      setSource('MASTER'); setLookupMsg('Employee found in master data. You can still correct a value; changes are recorded.');
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) { setSource('MANUAL'); setLookupMsg('Not found in master data. Please enter the details manually.'); }
      else setLookupMsg((e as Error).message);
    }
  }

  const feedbackLen = f.detailedFeedback.trim().length;
  const circulation = cats.find(c => c.id === f.releaseCategoryId)?.requiresClusterCirculation;

  async function submit(ev: React.FormEvent) {
    ev.preventDefault(); setError(''); setErrors({});
    if (feedbackLen < MIN_FEEDBACK_LENGTH) { setErrors({ detailedFeedback: [`At least ${MIN_FEEDBACK_LENGTH} characters required.`] }); return; }
    setBusy(true);
    try { const t = await releaseApi.create(f); nav(`../${t.id}`, { relative: 'path' }); }
    catch (e) { if (e instanceof ApiError) { setErrors(e.fieldErrors); setError(e.message); } else setError((e as Error).message); }
    finally { setBusy(false); }
  }

  const fe = (k: string) => errors[k]?.map(m => <div key={m} className="rr-error">{m}</div>);
  const text = (k: keyof CreateTicketInput, label: string, required = true) => (
    <label className="rr-input">{label}{required && ' *'}
      <input value={String(f[k] ?? '')} onChange={e => set(k, e.target.value as never)} required={required} />{fe(k)}</label>);
  const check = (k: keyof CreateTicketInput, label: string) => (
    <label className="rr-check"><input type="checkbox" checked={Boolean(f[k])} onChange={e => set(k, e.target.checked as never)} /> {label}</label>);

  return (
    <form onSubmit={submit} className="rr-form" noValidate>
      <h2>New release request</h2>
      {error && <ErrorBox message={error} />}

      <fieldset><legend>Employee</legend>
        <label className="rr-input">Employee ID *
          <div className="rr-row"><input value={f.employeeId} onChange={e => { set('employeeId', e.target.value); setSource(null); }} onBlur={lookup} required />
            <button type="button" onClick={lookup}>Search</button></div>{fe('employeeId')}</label>
        {source && <p className="rr-muted">Employee data source: <strong>{source === 'MASTER' ? 'Master' : 'Manual'}</strong></p>}
        {lookupMsg && <p role="status" className="rr-muted">{lookupMsg}</p>}
        {text('employeeName', 'Employee name')}{text('grade', 'Grade')}{text('location', 'Location')}{text('currentSupervisor', 'Current supervisor')}
        {text('currentProjectId', 'Current project ID')}{text('currentProjectName', 'Current project name')}{text('psl', 'PSL')}{text('band', 'Band')}
      </fieldset>

      <fieldset><legend>Release</legend>
        <label className="rr-input">Cluster *<select value={f.clusterId ?? ''} onChange={e => set('clusterId', e.target.value ? Number(e.target.value) : null)} required>
          <option value="">Select…</option>{clusters.map(c => <option key={c.id} value={c.id}>{c.code}</option>)}</select>{fe('clusterId')}</label>
        <label className="rr-input">Release category *<select value={f.releaseCategoryId ?? ''} onChange={e => set('releaseCategoryId', e.target.value ? Number(e.target.value) : null)} required>
          <option value="">Select…</option>{cats.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select>{fe('releaseCategoryId')}</label>
        {circulation === false && <p className="rr-muted">This category follows the direct release flow: it goes straight to the Head and is not circulated to clusters.</p>}
        <label className="rr-input">Release date / last working day *<input type="date" value={f.releaseDate} onChange={e => set('releaseDate', e.target.value)} required />{fe('releaseDate')}</label>
        {text('releaseLocation', 'Release location')}{text('reasonForRelease', 'Reason for release')}
        {check('recommendedForNewAssignment', 'Recommended for new assignment in HTPS')}{check('employeeAware', 'Employee is aware of the project release')}
        {check('factoredInForecast', 'Factored in forecast')}{check('backfillRequired', 'Backfill required')}
      </fieldset>

      <fieldset><legend>Feedback</legend>
        <label className="rr-input">Employee skills *<textarea value={f.employeeSkills} onChange={e => set('employeeSkills', e.target.value)} required />{fe('employeeSkills')}</label>
        <label className="rr-input">Feedback provided to employee *<textarea value={f.feedbackProvided} onChange={e => set('feedbackProvided', e.target.value)} required />{fe('feedbackProvided')}</label>
        <label className="rr-input">Detailed feedback for future project allocation * ({feedbackLen}/{MIN_FEEDBACK_LENGTH} min)
          <textarea rows={5} value={f.detailedFeedback} onChange={e => set('detailedFeedback', e.target.value)} aria-invalid={feedbackLen < MIN_FEEDBACK_LENGTH} />{fe('detailedFeedback')}</label>
        {text('commitmentMade', 'Any commitment made to employee?', false)}{text('remarks', 'Remarks', false)}
      </fieldset>

      <p className="rr-muted">Softlock documents can be attached from the ticket page after the request is created.</p>
      <button type="submit" disabled={busy}>{busy ? 'Submitting…' : 'Create release request'}</button>
    </form>
  );
}
