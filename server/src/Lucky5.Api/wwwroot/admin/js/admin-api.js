/*
 * admin-api.js
 * Shared transport module for the Lucky5 admin dashboard.
 * Mirrors the ApiError / normalizeKeys / unwrapResponse conventions used by
 * the cabinet client's js/api-client.js, but is kept fully independent so the
 * player-facing client is never touched by admin dashboard work.
 *
 * Exposes:
 *   - window.AdminApi: convenience object with one method per AdminController
 *     (and AuthController) endpoint consumed by the admin dashboard.
 *   - window.Lucky5AdminApiClient: low-level pieces (ApiError, normalizeKeys,
 *     AdminApiClient, SESSION_KEY) for advanced/edge-case usage.
 */
(function attachLucky5AdminApi(global) {
    'use strict';

    const SESSION_KEY = 'lucky5_admin_session';
    const LOGIN_FILE = 'login.html';

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

    function getSession() {
        try {
            const raw = global.localStorage.getItem(SESSION_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch (_) {
            return null;
        }
    }

    function setSession(session) {
        global.localStorage.setItem(SESSION_KEY, JSON.stringify(session || {}));
    }

    function clearSession() {
        global.localStorage.removeItem(SESSION_KEY);
    }

    function isAuthenticated() {
        const session = getSession();
        return Boolean(session && session.accessToken);
    }

    function getCurrentProfile() {
        const session = getSession();
        return (session && session.profile) || null;
    }

    function isOnLoginPage() {
        return /(^|\/)login\.html$/i.test(global.location.pathname);
    }

    function redirectToLogin() {
        clearSession();
        if (!isOnLoginPage()) {
            global.location.href = LOGIN_FILE;
        }
    }

    function toQueryString(params) {
        const usp = new URLSearchParams();
        Object.entries(params || {}).forEach(([key, value]) => {
            if (value !== undefined && value !== null && value !== '') {
                usp.set(key, value);
            }
        });
        const serialized = usp.toString();
        return serialized ? `?${serialized}` : '';
    }

    class AdminApiClient {
        constructor({ baseUrl = '', fetchImpl } = {}) {
            this.baseUrl = String(baseUrl || '').replace(/\/$/, '');
            this.fetchImpl = fetchImpl || (typeof global.fetch === 'function' ? global.fetch.bind(global) : null);
        }

        async request(method, path, body, options = {}) {
            const headers = { Accept: 'application/json', ...(options.headers || {}) };
            const session = getSession();
            if (session?.accessToken) headers.Authorization = `Bearer ${session.accessToken}`;
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

            if (response.status === 401 && !options.skipAuthRedirect) {
                redirectToLogin();
                throw new ApiError('Session expired. Please log in again.', { status: 401 });
            }

            const raw = await response.text();
            let json = null;
            try {
                json = raw ? JSON.parse(raw) : null;
            } catch (_) {
                throw new ApiError(`Non-JSON response from ${path}`, { status: response.status });
            }

            const normalized = normalizeKeys(json || {});
            const success = normalized.success ?? true;
            if (!response.ok || success === false) {
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

        put(path, body, options) {
            return this.request('PUT', path, body, options);
        }

        delete(path, body, options) {
            return this.request('DELETE', path, body, options);
        }
    }

    const client = new AdminApiClient();

    const AdminApi = {
        // --- Session helpers -------------------------------------------------
        getSession,
        setSession,
        clearSession,
        isAuthenticated,
        getCurrentProfile,

        // --- Auth --------------------------------------------------------------
        login(username, password) {
            return client.post('/api/auth/login', { username, password }, { skipAuthRedirect: true });
        },
        logout() {
            return client.post('/api/auth/logout', undefined, { skipAuthRedirect: true }).catch(() => null);
        },

        // --- Dashboard / audit ---------------------------------------------
        getDashboard() {
            return client.get('/api/admin/dashboard');
        },
        listAudit(take = 100) {
            return client.get(`/api/admin/audit${toQueryString({ take })}`);
        },

        // --- Users -----------------------------------------------------------
        listUsers() {
            return client.get('/api/admin/users');
        },
        searchUsers(query) {
            return client.get(`/api/admin/users/search${toQueryString({ q: query })}`);
        },
        getUser(userId) {
            return client.get(`/api/admin/users/${userId}`);
        },
        getUserDetail(userId) {
            return client.get(`/api/admin/users/${userId}/detail`);
        },
        createUser(payload) {
            return client.post('/api/admin/users/create', payload);
        },
        setUserRole(userId, role) {
            return client.post(`/api/admin/users/${userId}/role`, { role });
        },
        bulkAssignAgent(userIds, agentId) {
            return client.post('/api/admin/users/assign-agent', { userIds, agentId });
        },
        creditUser(payload) {
            return client.post('/api/admin/users/credit', payload);
        },
        forceEndSession(userId, machineId) {
            return client.post(`/api/admin/users/${userId}/force-end-session`, { machineId });
        },
        rechargeBonus(userId, rechargeAmount) {
            return client.post('/api/admin/users/recharge-bonus', { userId, rechargeAmount });
        },

        // --- Agents (admin summary view) -----------------------------------
        getAgentsSummary() {
            return client.get('/api/admin/agents/summary');
        },

        // --- Machines ----------------------------------------------------------
        listMachines() {
            return client.get('/api/admin/machines');
        },
        getMachine(machineId) {
            return client.get(`/api/admin/machines/${machineId}`);
        },
        getMachineDetail(machineId) {
            return client.get(`/api/admin/machines/${machineId}/detail`);
        },
        resetMachine(machineId) {
            return client.post(`/api/admin/machines/${machineId}/reset`);
        },
        forceResetMachine(machineId) {
            return client.post(`/api/admin/machines/${machineId}/force-reset`);
        },
        setDoorState(machineId, doorState) {
            return client.post(`/api/admin/machines/${machineId}/door-state`, { doorState });
        },

        // --- Cabinet devices -------------------------------------------------
        listCabinetDevices() {
            return client.get('/api/admin/cabinet-devices');
        },
        getCabinetDevice(deviceId) {
            return client.get(`/api/admin/cabinet-devices/${deviceId}`);
        },
        provisionCabinetDevice(payload) {
            return client.post('/api/admin/cabinet-devices', payload);
        },
        revokeCabinetDevice(deviceId, payload) {
            return client.post(`/api/admin/cabinet-devices/${deviceId}/revoke`, payload);
        }
    };

    global.AdminApi = AdminApi;
    global.Lucky5AdminApiClient = Object.freeze({
        AdminApiClient,
        ApiError,
        normalizeKeys,
        unwrapResponse,
        SESSION_KEY
    });
})(window);
