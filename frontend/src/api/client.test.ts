import { api, ApiError, setAccessToken } from './client'

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('cliente API', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    setAccessToken(null)
  })

  it('renueva la sesión una vez ante 401 y reintenta con el nuevo token', async () => {
    setAccessToken('viejo')
    const fetchMock = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json(401, { code: 'NO_AUTENTICADO', title: 'x' }))
      .mockResolvedValueOnce(json(200, { accessToken: 'nuevo', expiresAt: '', user: {} }))
      .mockResolvedValueOnce(json(200, { ok: true }))

    await expect(api('/projects')).resolves.toEqual({ ok: true })
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(String(fetchMock.mock.calls[1]![0])).toContain('/auth/refresh')
    const retry = fetchMock.mock.calls[2]![1] as RequestInit
    expect((retry.headers as Record<string, string>).Authorization).toBe('Bearer nuevo')
  })

  it('convierte ProblemDetails en ApiError con código y errores por campo', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      json(400, { code: 'VALIDACION', title: 'Uno o más campos no son válidos.', errors: { name: ['Obligatorio'] } }),
    )
    const err = await api('/projects', { method: 'POST', body: {} }).catch((e) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect(err.code).toBe('VALIDACION')
    expect(err.field('name')).toBe('Obligatorio')
  })

  it('no intenta renovar en endpoints de autenticación', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(json(401, { code: 'CREDENCIALES_INVALIDAS', title: 'x' }))
    await expect(api('/auth/login', { method: 'POST', body: {} })).rejects.toMatchObject({ code: 'CREDENCIALES_INVALIDAS' })
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })
})
