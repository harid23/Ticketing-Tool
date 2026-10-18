import { useEffect, useRef, useState, type ReactNode } from 'react';

/** Small confirmation dialog with optional inputs. Used for every destructive or decision action. */
export interface DialogField { name: string; label: string; type?: 'text' | 'textarea' | 'date' | 'select'; required?: boolean; options?: { value: string; label: string }[]; defaultValue?: string }

export default function ActionDialog({ open, title, intro, fields = [], confirmLabel, onCancel, onConfirm }: {
  open: boolean; title: string; intro?: ReactNode; fields?: DialogField[]; confirmLabel: string;
  onCancel: () => void; onConfirm: (values: Record<string, string>) => Promise<void>;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const [vals, setVals] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false); const [err, setErr] = useState('');

  useEffect(() => {
    if (open) { setVals(Object.fromEntries(fields.map(f => [f.name, f.defaultValue ?? '']))); setErr(''); ref.current?.showModal(); } else ref.current?.close();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  async function go(e: React.FormEvent) {
    e.preventDefault(); setBusy(true); setErr('');
    try { await onConfirm(vals); } catch (x) { setErr((x as Error).message); } finally { setBusy(false); }
  }
  return (
    <dialog ref={ref} onCancel={onCancel} className="rr-dialog">
      <form onSubmit={go}>
        <h3>{title}</h3>{intro && <p>{intro}</p>}
        {fields.map(f => (
          <label key={f.name} className="rr-input">{f.label}{f.required && ' *'}
            {f.type === 'textarea' ? <textarea required={f.required} value={vals[f.name] ?? ''} onChange={e => setVals(v => ({ ...v, [f.name]: e.target.value }))} />
              : f.type === 'select' ? <select required={f.required} value={vals[f.name] ?? ''} onChange={e => setVals(v => ({ ...v, [f.name]: e.target.value }))}>
                  <option value="">Select…</option>{f.options?.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}</select>
              : <input type={f.type ?? 'text'} required={f.required} value={vals[f.name] ?? ''} onChange={e => setVals(v => ({ ...v, [f.name]: e.target.value }))} />}
          </label>))}
        {err && <p role="alert" className="rr-error">{err}</p>}
        <div className="rr-row"><button type="button" onClick={onCancel}>Back</button><button type="submit" disabled={busy}>{busy ? 'Working…' : confirmLabel}</button></div>
      </form>
    </dialog>
  );
}
