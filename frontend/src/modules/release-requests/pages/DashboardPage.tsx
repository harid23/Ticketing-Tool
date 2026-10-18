import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { releaseApi } from '../api';
import type { Dashboard, LocalDemoScenario } from '../types';
import { ErrorBox, fmtDateTime, Loading } from '../components/Common';
import { isLocalDevelopment } from '../../../auth';

export default function DashboardPage() {
  const [d, setD] = useState<Dashboard | null>(null); const [err, setErr] = useState('');
  const [scenarios, setScenarios] = useState<LocalDemoScenario[] | null>(null);
  const [scenarioError, setScenarioError] = useState('');
  const [seeding, setSeeding] = useState(false);
  useEffect(() => { releaseApi.dashboard().then(setD).catch(e => setErr(e.message)); }, []);
  if (err && !d) return <ErrorBox message={err} />;
  if (!d) return <Loading />;
  const seedScenarios = async () => {
    setSeeding(true);
    setScenarioError('');
    try {
      setScenarios(await releaseApi.seedLocalDemoScenarios());
      setD(await releaseApi.dashboard());
    } catch (error) {
      setScenarioError(error instanceof Error ? error.message : 'Could not create local SLA scenarios.');
    } finally {
      setSeeding(false);
    }
  };
  const cards: [string, number][] = [
    ['Open', d.totalOpen], ['In progress (WIP)', d.totalWip], ['Closed', d.totalClosed], ['Cancelled', d.totalCancelled], ['Reopened', d.totalReopened],
    ['Allocated (Selected)', d.allocatedCount], ['Released (Rejected)', d.releasedCount],
    ['SLA at risk', d.slaAtRisk], ['SLA breached', d.slaBreached],
    ['Awaiting cluster response', d.awaitingClusterResponse], ['Awaiting interview', d.awaitingInterview], ['Awaiting final decision', d.awaitingFinalDecision]];
  return (
    <div><h2>Release request dashboard</h2>
      <div className="rr-cards">{cards.map(([label, n]) => <div className="rr-stat" key={label}><div className="rr-stat-n">{n}</div><div>{label}</div></div>)}</div>
      {isLocalDevelopment && <section className="rr-card rr-demo-scenarios">
        <header><h3>Local SLA test scenarios</h3><button type="button" disabled={seeding} onClick={() => void seedScenarios()}>
          {seeding ? 'Preparing…' : 'Load demo requests'}
        </button></header>
        <p>Creates clearly labeled, synthetic requests for fresh, near-deadline, manual-decision, and closed-after-approved-extension SLA states. Safe to run again; existing demo requests are reused.</p>
        {scenarioError && <ErrorBox message={scenarioError} />}
        {scenarios && <ul>{scenarios.map(scenario => <li key={scenario.ticketNumber}>
          <Link to={`/release-requests/${scenario.ticketId}`}>{scenario.ticketNumber}</Link>
          {' · '}{scenario.scenario}{' · '}{scenario.status}{' / '}{scenario.slaStatus}
          {' · Deadline: '}{fmtDateTime(scenario.deadline)}
        </li>)}</ul>}
      </section>}
    </div>
  );
}
