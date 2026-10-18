import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { releaseApi } from '../api';
import type { ActivityNotification, Me } from '../types';
import { fmtDateTime } from './Common';

export default function NotificationsBell({ profile }: { profile: Me }) {
  const identity = `${profile.userId ?? profile.roles.join('-')}-${profile.clusterId ?? 'all'}`;
  const storageKey = `release-notifications-read-${identity}`;
  const [items, setItems] = useState<ActivityNotification[]>([]);
  const [readThrough, setReadThrough] = useState(() => Number(sessionStorage.getItem(storageKey) ?? 0));
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    setReadThrough(Number(sessionStorage.getItem(storageKey) ?? 0));
    let cancelled = false;
    const refresh = () => {
      releaseApi.notifications()
        .then(notifications => { if (!cancelled) { setItems(notifications); setError(''); } })
        .catch((loadError: unknown) => {
          if (!cancelled) setError(loadError instanceof Error ? loadError.message : 'Could not load notifications.');
        });
    };
    refresh();
    const timer = window.setInterval(refresh, 10_000);
    return () => { cancelled = true; window.clearInterval(timer); };
  }, [storageKey]);

  const unreadCount = useMemo(() => items.filter(item => item.id > readThrough).length, [items, readThrough]);
  const markRead = () => {
    const latestId = items.reduce((latest, item) => Math.max(latest, item.id), readThrough);
    sessionStorage.setItem(storageKey, String(latestId));
    setReadThrough(latestId);
  };
  const toggleOpen = () => {
    if (!open) markRead();
    setOpen(!open);
  };

  return <div className="rr-notifications">
    <button type="button" className="rr-notifications-button" aria-label={`Notifications${unreadCount ? `, ${unreadCount} unread` : ''}`}
      aria-expanded={open} onClick={toggleOpen}>
      <svg aria-hidden="true" viewBox="0 0 24 24" width="21" height="21" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9" /><path d="M10 21h4" />
      </svg>
      {unreadCount > 0 && <span className="rr-notification-count">{unreadCount > 99 ? '99+' : unreadCount}</span>}
    </button>
    {open && <section className="rr-notification-panel" aria-label="Recent notifications">
      <header><strong>Recent updates</strong><span>{unreadCount ? `${unreadCount} unread` : 'All caught up'}</span></header>
      {error && <p className="rr-notification-error" role="alert">{error}</p>}
      {!error && !items.length && <p className="rr-notification-empty">No recent ticket updates.</p>}
      {items.length > 0 && <ul>{items.map(item => <li key={item.id}>
        <Link to={`/release-requests/${item.ticketId}`} onClick={() => setOpen(false)}>
          <span className="rr-notification-ticket">{item.ticketNumber} · {item.employeeName}</span>
          <span>{item.message}</span>
          <time dateTime={item.occurredAt}>{fmtDateTime(item.occurredAt)}</time>
        </Link>
      </li>)}</ul>}
      <p className="rr-notification-footnote">Updates refresh automatically every 10 seconds.</p>
    </section>}
  </div>;
}
