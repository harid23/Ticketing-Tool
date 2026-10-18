import { InteractionRequiredAuthError, PublicClientApplication, type Configuration, type SilentRequest } from '@azure/msal-browser';

const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID?.trim() ?? '';
const clientId = import.meta.env.VITE_ENTRA_SPA_CLIENT_ID?.trim() ?? '';
export const apiScope = import.meta.env.VITE_ENTRA_API_SCOPE?.trim() ?? '';

export const isLocalDevelopment = import.meta.env.DEV && import.meta.env.VITE_AUTH_MODE !== 'entra';
export const isEntraConfigured = Boolean(tenantId && clientId && apiScope);

const msalConfig: Configuration = {
  auth: {
    clientId: clientId || '00000000-0000-0000-0000-000000000000',
    authority: `https://login.microsoftonline.com/${tenantId || 'common'}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: { cacheLocation: 'sessionStorage' },
};

export const msalInstance = new PublicClientApplication(msalConfig);

export async function getApiAccessToken(): Promise<string | null> {
  if (isLocalDevelopment) return null;
  if (!isEntraConfigured) throw new Error('Entra sign-in is not configured. Set VITE_ENTRA_TENANT_ID, VITE_ENTRA_SPA_CLIENT_ID, and VITE_ENTRA_API_SCOPE.');

  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
  if (!account) throw new Error('Sign in to continue.');

  const request: SilentRequest = { scopes: [apiScope], account };
  try {
    const result = await msalInstance.acquireTokenSilent(request);
    return result.accessToken;
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await msalInstance.acquireTokenRedirect({ scopes: [apiScope], account });
      throw new Error('Redirecting to Microsoft to refresh your access. Retry your last action after sign-in.');
    }
    throw error;
  }
}