import { createStandardPublicClientApplication, InteractionRequiredAuthError, type AccountInfo, type IPublicClientApplication } from '@azure/msal-browser'
import { devIdentityEnabled } from './devUser'

/** What GET /api/v1/sign-in says: development identity, or Microsoft Entra ID with these values. */
type SignInConfiguration = { mode: 'development' | 'entra-id'; tenantId: string | null; clientId: string | null; scope: string | null }

type EntraSignIn = { app: IPublicClientApplication; account: AccountInfo; scope: string }

let entra: EntraSignIn | null = null

/**
 * Signs the person in before the app renders (ADR 0014). Outside development this is Microsoft Entra ID with a
 * full-page redirect: popups would be cut off by Cross-Origin-Opener-Policy. Resolves false while the browser is
 * on its way to the Microsoft sign-in page.
 */
export async function signIn(): Promise<boolean> {
  if (devIdentityEnabled) return true

  const response = await fetch('/api/v1/sign-in')
  if (!response.ok) throw new Error(`Anmeldedaten nicht verfügbar (${response.status})`)
  const config = (await response.json()) as SignInConfiguration
  if (config.mode !== 'entra-id' || !config.tenantId || !config.clientId || !config.scope) return true

  const app = await createStandardPublicClientApplication({
    auth: {
      clientId: config.clientId,
      authority: `https://login.microsoftonline.com/${config.tenantId}`,
      redirectUri: `${window.location.origin}/`,
      postLogoutRedirectUri: `${window.location.origin}/`,
    },
    // Tokens live only as long as the tab; a new tab signs in silently with the Microsoft session.
    cache: { cacheLocation: 'sessionStorage' },
  })

  const redirect = await app.handleRedirectPromise()
  const account = redirect?.account ?? app.getActiveAccount() ?? app.getAllAccounts()[0]
  if (!account) {
    await app.loginRedirect({ scopes: [config.scope] })
    return false
  }

  app.setActiveAccount(account)
  entra = { app, account, scope: config.scope }
  return true
}

/** Access token for the API, or null with the development identity. Renews silently, else signs in again. */
export async function accessToken(): Promise<string | null> {
  if (!entra) return null
  const request = { scopes: [entra.scope], account: entra.account }
  try {
    return (await entra.app.acquireTokenSilent(request)).accessToken
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) await entra.app.acquireTokenRedirect(request)
    throw error
  }
}

/** For the realtime hubs: browsers send the token as query parameter on WebSockets (EntraIdRegistration). */
export const hubConnectionOptions = { accessTokenFactory: async () => (await accessToken()) ?? '' }

/** True when signed in with Microsoft, so the app offers signing out. */
export const signedInWithEntra = () => entra !== null

export async function signOut(): Promise<void> {
  await entra?.app.logoutRedirect({ account: entra.account })
}
