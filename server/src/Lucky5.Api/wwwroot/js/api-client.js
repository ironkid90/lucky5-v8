/*
 * api-client.js
 * Small transport boundary for REST and SignalR calls.
 * It intentionally has no game-state knowledge; game.js remains the authority
 * for presentation and gameplay orchestration.
 */
(function attachLucky5ApiClient(global) {
    'use strict';

    class ApiError extends Error {
        constructor(message, details = {}) {
            super(message || 'Request failed');
            this.name = 'ApiError';
            this.status = Number(details.status || 0);
            this.code = details.code || '';
            this.traceId = details.traceId || '';
            this.retryable = details.retryable ?? (this.status === 0 || this.status === 408 || this.status === 429 || this.status >= 500);
        }
    }

    function normalizeKeys(value) {
        if (Array.isArray(value)) return value.map(normalizeKeys);
        if (!value || typeof value !== 'object') return value;

        return Object.fromEntries(Object.entries(value).map(([key, nested]) => [
            key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key,
            normalizeKeys(nested)
        ]));
    }

    function unwrapResponse(json) {
        if (!json || typeof json !== 'object') return json;
        return normalizeKeys(json.data ?? json.Data ?? json);
    }

    class ApiClient {
        constructor({ baseUrl = '', tokenProvider = () => null, fetchImpl } = {}) {
            this.baseUrl = String(baseUrl || '').replace(/\/$/, '');
            this.tokenProvider = tokenProvider;
            this.fetchImpl = fetchImpl || (typeof global.fetch === 'function' ? global.fetch.bind(global) : null);
        }

        async request(method, path, body, options = {}) {
            const headers = { Accept: 'application/json', ...(options.headers || {}) };
            const token = this.tokenProvider?.();
            if (token) headers.Authorization = `Bearer ${token}`;
            if (body !== undefined) headers['Content-Type'] = 'application/json';

            if (!this.fetchImpl) {
                throw new ApiError('Fetch transport is unavailable', { retryable: true });
            }

            let response;
            try {
                response = await this.fetchImpl(`${this.baseUrl}${path}`, {
                    method,
                    headers,
                    body: body === undefined ? undefined : JSON.stringify(body),
                    signal: options.signal
                });
            } catch (error) {
                throw new ApiError(error?.message || 'Network request failed', { retryable: true });
            }

            const raw = await response.text();
            let json = null;
            try {
                json = raw ? JSON.parse(raw) : null;
            } catch (_) {
                throw new ApiError(`Non-JSON response from ${path}`, { status: response.status });
            }

            const normalized = normalizeKeys(json || {});
            const status = normalized.status;
            const success = normalized.success ?? true;
            if (!response.ok || success === false || String(status || '').toLowerCase() === 'error') {
                throw new ApiError(
                    normalized.message || normalized.errors?.[0] || `Request failed (${response.status})`,
                    {
                        status: response.status,
                        code: normalized.code,
                        traceId: normalized.traceId,
                        retryable: normalized.retryable
                    }
                );
            }

            return unwrapResponse(json);
        }

        get(path, options) {
            return this.request('GET', path, undefined, options);
        }

        post(path, body, options) {
            return this.request('POST', path, body, options);
        }

        async invokeHub(connection, method, ...args) {
            if (!connection || typeof connection.invoke !== 'function') {
                throw new ApiError('Realtime connection is unavailable', { retryable: true });
            }
            try {
                return await connection.invoke(method, ...args);
            } catch (error) {
                throw new ApiError(error?.message || `SignalR invocation failed: ${method}`, { retryable: true });
            }
        }
    }

    global.Lucky5ApiClient = Object.freeze({ ApiClient, ApiError, normalizeKeys });
})(window);
