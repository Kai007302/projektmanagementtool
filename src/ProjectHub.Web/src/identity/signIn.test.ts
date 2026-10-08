import { beforeEach, describe, expect, it, vi } from 'vitest'

const msal = vi.hoisted(() => ({
  app: {
    handleRedirectPromise: vi.fn(),
    getActiveAccount: vi.fn(),
    getAllAccounts: vi.fn(),
    loginRedirect: vi.fn(),
    setActiveAccount: vi.fn(),
    acquireTokenSilent: vi.fn(),
    acquireTokenRedirect: vi.fn(),
  },
  create: vi.fn(),
}))

vi.mock('./devUser', () => ({ devIdentityEnabled: false }))
vi.mock('@azure/msal-browser', () => ({
  createStandardPublicClientApplication: msal.create,
  InteractionRequiredAuthError: class extends Error {},
}))

const entra = { mode: 'entra-id', tenantId: 'tenant', clientId: 'client', scope: 'api://client/access_as_user' }
const account = { homeAccountId: 'kai' }

function answer(body: object) {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status: 200 })))
}

describe('signIn', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
    msal.create.mockResolvedValue(msal.app)
    msal.app.handleRedirectPromise.mockResolvedValue(null)
    msal.app.getActiveAccount.mockReturnValue(null)
    msal.app.getAllAccounts.mockReturnValue([])
  })

  it('needs nothing from Microsoft when the API runs in development', async () => {
    answer({ mode: 'development', tenantId: null, clientId: null, scope: null })
    const { signIn, accessToken } = await import('./signIn')

    expect(await signIn()).toBe(true)
    expect(msal.create).not.toHaveBeenCalled()
    expect(await accessToken()).toBeNull()
  })

  it('sends a person without an account to the Microsoft sign-in page of the tenant', async () => {
    answer(entra)
    const { signIn } = await import('./signIn')

    expect(await signIn()).toBe(false)
    expect(msal.create).toHaveBeenCalledWith(
      expect.objectContaining({ auth: expect.objectContaining({ clientId: 'client', authority: 'https://login.microsoftonline.com/tenant' }) }),
    )
    expect(msal.app.loginRedirect).toHaveBeenCalledWith({ scopes: ['api://client/access_as_user'] })
  })

  it('comes back from Microsoft to the page the person was on, not to the start page', async () => {
    answer(entra)
    window.history.replaceState(null, '', '/projekte/0192a000-0000-7000-8000-000000000001/liste')
    const { signIn } = await import('./signIn')
    expect(await signIn()).toBe(false)

    window.history.replaceState(null, '', '/')
    msal.app.handleRedirectPromise.mockResolvedValue({ account })
    vi.resetModules()
    const again = await import('./signIn')
    expect(await again.signIn()).toBe(true)

    expect(window.location.pathname).toBe('/projekte/0192a000-0000-7000-8000-000000000001/liste')
    expect(msal.app.handleRedirectPromise).toHaveBeenCalledWith({ navigateToLoginRequestUrl: false })
    window.history.replaceState(null, '', '/')
  })

  it('returns from Microsoft signed in and asks for API tokens with the configured scope', async () => {
    answer(entra)
    msal.app.handleRedirectPromise.mockResolvedValue({ account })
    msal.app.acquireTokenSilent.mockResolvedValue({ accessToken: 'token' })
    const { signIn, accessToken, signedInWithEntra } = await import('./signIn')

    expect(await signIn()).toBe(true)
    expect(signedInWithEntra()).toBe(true)
    expect(await accessToken()).toBe('token')
    expect(msal.app.acquireTokenSilent).toHaveBeenCalledWith({ scopes: ['api://client/access_as_user'], account })
  })
})
