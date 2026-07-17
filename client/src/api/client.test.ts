import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, apiBaseUrl } from './client';

function mockFetchOnce(init: { ok: boolean; status: number; json?: unknown }) {
  const fetchMock = vi.fn().mockResolvedValue({
    ok: init.ok,
    status: init.status,
    json: async () => init.json,
  } as Response);
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('api client', () => {
  it('returns parsed JSON on a 200 response', async () => {
    const fetchMock = mockFetchOnce({ ok: true, status: 200, json: { value: 42 } });

    const result = await api.get<{ value: number }>('/thing');

    expect(result).toEqual({ value: 42 });
    expect(fetchMock).toHaveBeenCalledWith(
      `${apiBaseUrl()}/thing`,
      expect.objectContaining({ method: 'GET' }),
    );
  });

  it('throws ApiError carrying ProblemDetails on a non-2xx response', async () => {
    mockFetchOnce({
      ok: false,
      status: 404,
      json: { title: 'Not Found', status: 404 },
    });

    const error = await api.get('/missing').catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
    expect((error as ApiError).problem?.title).toBe('Not Found');
  });

  it('serializes the body and sets the content-type on POST', async () => {
    const fetchMock = mockFetchOnce({ ok: true, status: 200, json: { id: '1' } });

    await api.post('/things', { name: 'x' });

    const [, options] = fetchMock.mock.calls[0];
    expect(options.method).toBe('POST');
    expect(options.body).toBe(JSON.stringify({ name: 'x' }));
    expect(options.headers['Content-Type']).toBe('application/json');
  });
});
