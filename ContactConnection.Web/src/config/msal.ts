import { PublicClientApplication, type Configuration } from '@azure/msal-browser'

const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID as string
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID as string

const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: '/portal/auth/callback',
    postLogoutRedirectUri: '/portal/login',
  },
  cache: {
    cacheLocation: 'sessionStorage',
  },
}

export const msalInstance = new PublicClientApplication(msalConfig)

// Initialize eagerly so redirect state is stored before loginRedirect() runs
export const msalInitialized = msalInstance.initialize()

export const portalLoginRequest = {
  scopes: ['openid', 'profile', 'email'],
}
