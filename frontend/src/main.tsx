import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import { MsalProvider } from '@azure/msal-react';
import { msalInstance } from './auth';
import './styles.css';

async function start() {
  const root = ReactDOM.createRoot(document.getElementById('root')!);
  try {
    await msalInstance.initialize();
    const redirectResult = await msalInstance.handleRedirectPromise();
    if (redirectResult?.account) msalInstance.setActiveAccount(redirectResult.account);
    else if (!msalInstance.getActiveAccount()) {
      const [account] = msalInstance.getAllAccounts();
      if (account) msalInstance.setActiveAccount(account);
    }

    root.render(
      <React.StrictMode>
        <MsalProvider instance={msalInstance}>
          <App />
        </MsalProvider>
      </React.StrictMode>,
    );
  } catch (error) {
    root.render(<main className="rr-auth-page"><section className="rr-auth-card"><h1>Sign-in could not start</h1><p>{error instanceof Error ? error.message : 'Please reload and try again.'}</p></section></main>);
  }
}

void start();
