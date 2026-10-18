import type * as T from './types';

/**
 * Plug this into the host app's existing authentication, e.g. with MSAL:
 *   configureReleaseApi({ baseUrl: import.meta.env.VITE_RELEASE_API_URL,
 *                         getAccessToken: async () => (await msal.acquireTokenSilent({ scopes: ['api://<api-client-id>/access_as_user'], account })).accessToken });
 * No secrets live in the browser: only a short-lived user token is sent, and the backend validates it.
 */
let config: { baseUrl: string; getAccessToken: () => Promise<string | null>; getHeaders?: () => Record<string, string> } | null = null;
export function configureReleaseApi(c: { baseUrl: string; getAccessToken: () => Promise<string | null>; getHeaders?: () => Record<string, string> }) { config = c; }

export class ApiError extends Error {
  constructor(public status: number, message: string, public fieldErrors: Record<string, string[]> = {}, public traceId?: string) { super(message); }
}

async function request<R>(method: string, path: string, body?: unknown, form?: FormData): Promise<R> {
  if (!config) throw new Error('Release API not configured. Call configureReleaseApi() at app start.');
  const token = await config.getAccessToken();
  const res = await fetch(`${config.baseUrl}/api/v1${path}`, {
    method,
    headers: { ...config.getHeaders?.(), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: form ?? (body ? JSON.stringify(body) : undefined),
  });
  if (!res.ok) {
    let p: any = {}; try { p = await res.json(); } catch { /* not JSON */ }
    // Backend errors are RFC 7807 ProblemDetails; field errors (400) are in p.errors
    const fieldErrors: Record<string, string[]> = {};
    Object.entries(p.errors ?? {}).forEach(([k, v]) => { fieldErrors[k.charAt(0).toLowerCase() + k.slice(1)] = v as string[]; });
    throw new ApiError(res.status, p.detail ?? p.title ?? `Request failed (${res.status})`, fieldErrors, p.traceId);
  }
  if (res.status === 204) return undefined as R;
  return res.json() as Promise<R>;
}

const qs = (o: Record<string, unknown>) => {
  const p = new URLSearchParams();
  Object.entries(o).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== '') p.set(k, String(v)); });
  const s = p.toString(); return s ? `?${s}` : '';
};

export const releaseApi = {
  me: () => request<T.Me>('GET', '/me'),
  notifications: () => request<T.ActivityNotification[]>('GET', '/notifications'),
  seedLocalDemoScenarios: () => request<T.LocalDemoScenario[]>('POST', '/local-demo/scenarios'),
  clusters: () => request<T.Cluster[]>('GET', '/clusters'),
  categories: () => request<T.ReleaseCategory[]>('GET', '/release-categories'),
  pendingClusterEvaluations: () => request<T.ClusterEvaluationInboxItem[]>('GET', '/cluster-evaluations/pending'),
  ownedAllocationTickets: () => request<T.AllocationTicket[]>('GET', '/cluster-evaluations/owned'),
  availableResources: (q: Record<string, unknown>) => request<T.Paged<T.AvailableResource>>('GET', `/resources/available${qs(q)}`),
  employee: (id: string) => request<T.Employee>('GET', `/employees/${encodeURIComponent(id)}`),

  list: (q: Record<string, unknown>) => request<T.Paged<T.TicketSummary>>('GET', `/release-requests${qs(q)}`),
  get: (id: number) => request<T.TicketDetail>('GET', `/release-requests/${id}`),
  create: (b: T.CreateTicketInput) => request<T.TicketDetail>('POST', '/release-requests', b),
  update: (id: number, b: unknown) => request<T.TicketDetail>('PUT', `/release-requests/${id}`, b),
  cancel: (id: number, b: { reason: string; opportunityIdentifiedOn: string }) => request<T.TicketDetail>('POST', `/release-requests/${id}/cancel`, b),
  close: (id: number, reason: string) => request<T.TicketDetail>('POST', `/release-requests/${id}/close`, { reason }),
  approveSlaException: (id: number) => request<T.TicketDetail>('POST', `/release-requests/${id}/sla-exception/approve`),
  reopen: (id: number, b: { reason: string; requestingClusterId?: number }) => request<T.TicketDetail>('POST', `/release-requests/${id}/reopen`, b),
  history: (id: number) => request<T.HistoryItem[]>('GET', `/release-requests/${id}/history`),
  audit: (id: number) => request<T.AuditItem[]>('GET', `/release-requests/${id}/audit`),

  respond: (t: number, e: number, response: T.ClusterResponse) => request<T.Evaluation>('POST', `/release-requests/${t}/evaluations/${e}/respond`, { response }),
  updateAllocationStatus: (t: number, e: number, status: T.AllocationStatus, rejectionReason?: string) =>
    request<T.Evaluation>('PUT', `/release-requests/${t}/evaluations/${e}/status`, { status, rejectionReason }),
  start: (t: number, e: number) => request<T.Evaluation>('POST', `/release-requests/${t}/evaluations/${e}/start`),
  complete: (t: number, e: number, b: { selectionResult: 'SELECTED' | 'NOT_SELECTED'; interviewDate?: string; rejectionReason?: string; comments?: string }) =>
    request<T.Evaluation>('POST', `/release-requests/${t}/evaluations/${e}/complete`, b),

  attachments: (id: number) => request<T.Attachment[]>('GET', `/release-requests/${id}/attachments`),
  upload: (id: number, kind: string, file: File) => { const f = new FormData(); f.append('kind', kind); f.append('file', file); return request<T.Attachment>('POST', `/release-requests/${id}/attachments`, undefined, f); },
  /** Downloads through the API (with the bearer token) and hands the browser a temporary blob URL. */
  downloadAttachment: async (id: number, attId: number, fileName: string) => {
    if (!config) throw new Error('Release API not configured.');
    const token = await config.getAccessToken();
    const res = await fetch(`${config.baseUrl}/api/v1/release-requests/${id}/attachments/${attId}/download`, { headers: { ...config.getHeaders?.(), ...(token ? { Authorization: `Bearer ${token}` } : {}) } });
    if (!res.ok) throw new ApiError(res.status, 'Download failed');
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement('a'); a.href = url; a.download = fileName; a.click(); URL.revokeObjectURL(url);
  },
  dashboard: () => request<T.Dashboard>('GET', '/dashboard/summary'),
};
