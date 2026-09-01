/*
 * admin-api.js
 * Shared transport + session module for the Lucky5 admin dashboard.
 * Plain JS, no build step, no frameworks - kept fully separate from the
 * player cabinet's wwwroot/js/api-client.js.
 *
 * Exposes:
 *   window.AdminSession - localStorage-backed session helpers
 *   window.AdminApi     - singleton client with convenience methods
 *   window.AdminApiClient / window.AdminApiError - constructors, for pages
 *                          that need to extend or test the client directly.
 */
(function attachLucky5AdminApi(global) {
    'use strict';

    var SESSION_KEY = 'lucky5_admin_session';
    var LOGIN_PAGE = 'login.html';

    function resolveAdminUrl(relativePage) {
        try {
            return new URL(relativePage, global.location.href).href;
        } catch (_) {
            return relativePage;
        }
    }

    var AdminSession = {
        KEY: SESSION_KEY,

        get: function getSession() {
            try {
                var raw = global.localStorage.getItem(SESSION_KEY);
                return raw ? JSON.parse(raw) : null;
            } catch (_) {
                return null;
            }
        },

        set: function setSession(session) {
            global.localStorage.setItem(SESSION_KEY, JSON.stringify(session || {}));
        },

        clear: function clearSession() {
            global.localStorage.removeItem(SESSION_KEY);
        },

        isValid: function isValid() {
            var session = AdminSession.get();
            if (!session || !session.accessToken) return false;
            if (session.expiresAtUtc) {
                var expiresAt = new Date(session.expiresAtUtc).getTime();
                if (!Number.isNaN(expiresAt) && expiresAt <= Date.now()) return false;
            }
            return true;
        },

        redirectToLogin: function redirectToLogin() {
            global.location.href = resolveAdminUrl(LOGIN_PAGE);
        }
    };

    var AdminApiError = /** @class */ (function (_super) {
        function AdminApiError(message, details) {
            details = details || {};
            var instance = _super.call(this, message || 'Request failed') || this;
            instance.name = 'AdminApiError';
            instance.status = Number(details.status || 0);
            instance.code = details.code || '';
            instance.traceId = details.traceId || '';
            instance.errors = details.errors || [];
            if (Object.setPrototypeOf) {
                Object.setPrototypeOf(instance, AdminApiError.prototype);
            }
            return instance;
        }
        AdminApiError.prototype = Object.create(_super.prototype);
        AdminApiError.prototype.constructor = AdminApiError;
        return AdminApiError;
    }(Error));

    function normalizeKeys(value) {
        if (Array.isArray(value)) return value.map(normalizeKeys);
        if (!value || typeof value !== 'object') return value;

        var result = {};
        Object.keys(value).forEach(function (key) {
            if (key === '__proto__' || key === 'constructor' || key === 'prototype') return;
            var normalizedKey = key.length > 0 ? key[0].toLowerCase() + key.slice(1) : key;
            result[normalizedKey] = normalizeKeys(value[key]);
        });
        return result;
    }

    function unwrapResponse(json) {
        if (!json || typeof json !== 'object') return json;
        var data = ('data' in json) ? json.data : (('Data' in json) ? json.Data : json);
        return normalizeKeys(data);
    }

    function AdminApiClient(options) {
        options = options || {};
        this.baseUrl = String(options.baseUrl || '').replace(/\/$/, '');
        this.fetchImpl = options.fetchImpl || (typeof global.fetch === 'function' ? global.fetch.bind(global) : null);
    }

    AdminApiClient.prototype.request = function request(method, path, body, options) {
        options = options || {};
        var self = this;
        var headers = Object.assign({ Accept: 'application/json' }, options.headers || {});

        if (!options.skipAuth) {
            var session = AdminSession.get();
            if (session && session.accessToken) {
                headers.Authorization = 'Bearer ' + session.accessToken;
            }
        }
        if (body !== undefined) headers['Content-Type'] = 'application/json';

        if (!this.fetchImpl) {
            return Promise.reject(new AdminApiError('Fetch transport is unavailable', { status: 0 }));
        }

        return this.fetchImpl(this.baseUrl + path, {
            method: method,
            headers: headers,
            body: body === undefined ? undefined : JSON.stringify(body),
            signal: options.signal
        }).catch(function (error) {
            throw new AdminApiError((error && error.message) || 'Network request failed', { status: 0 });
        }).then(function (response) {
            return response.text().then(function (raw) {
                var json = null;
                try {
                    json = raw ? JSON.parse(raw) : null;
                } catch (_) {
                    throw new AdminApiError('Non-JSON response from ' + path, { status: response.status });
                }

                var normalized = normalizeKeys(json || {});
                var success = normalized.success;
                if (success === undefined) success = response.ok;

                if ((response.status === 401 || response.status === 403) && !options.skipAuthRedirect) {
                    AdminSession.clear();
                    AdminSession.redirectToLogin();
                }

                if (!response.ok || success === false) {
                    throw new AdminApiError(
                        normalized.message || (normalized.errors && normalized.errors[0]) || ('Request failed (' + response.status + ')'),
                        {
                            status: response.status,
                            traceId: normalized.traceId,
                            errors: normalized.errors || []
                        }
                    );
                }

                return unwrapResponse(json);
            });
        });
    };

    AdminApiClient.prototype.get = function get(path, options) {
        return this.request('GET', path, undefined, options);
    };

    AdminApiClient.prototype.post = function post(path, body, options) {
        return this.request('POST', path, body, options);
    };

    AdminApiClient.prototype.put = function put(path, body, options) {
        return this.request('PUT', path, body, options);
    };

    AdminApiClient.prototype.del = function del(path, body, options) {
        return this.request('DELETE', path, body, options);
    };

    /* ---------------------------------------------------------- */
    /* Auth                                                        */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.login = function login(username, password) {
        return this.request('POST', '/api/auth/login', { username: username, password: password }, {
            skipAuth: true,
            skipAuthRedirect: true
        });
    };

    AdminApiClient.prototype.logout = function logout() {
        return this.request('POST', '/api/auth/logout', undefined, { skipAuthRedirect: true }).catch(function () {
            /* best-effort; ignore transport errors on logout */
        });
    };

    /* ---------------------------------------------------------- */
    /* Dashboard / audit                                           */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.getDashboard = function getDashboard() {
        return this.get('/api/admin/dashboard');
    };

    AdminApiClient.prototype.listAudit = function listAudit(take) {
        var query = take ? ('?take=' + encodeURIComponent(take)) : '';
        return this.get('/api/admin/audit' + query);
    };

    /* ---------------------------------------------------------- */
    /* Users                                                        */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.listUsers = function listUsers() {
        return this.get('/api/admin/users');
    };

    AdminApiClient.prototype.searchUsers = function searchUsers(query) {
        return this.get('/api/admin/users/search?q=' + encodeURIComponent(query || ''));
    };

    AdminApiClient.prototype.getUser = function getUser(userId) {
        return this.get('/api/admin/users/' + encodeURIComponent(userId));
    };

    AdminApiClient.prototype.getUserDetail = function getUserDetail(userId) {
        return this.get('/api/admin/users/' + encodeURIComponent(userId) + '/detail');
    };

    AdminApiClient.prototype.createUser = function createUser(payload) {
        return this.post('/api/admin/users/create', payload);
    };

    AdminApiClient.prototype.setUserRole = function setUserRole(userId, role) {
        return this.post('/api/admin/users/' + encodeURIComponent(userId) + '/role', { role: role });
    };

    AdminApiClient.prototype.bulkAssignAgent = function bulkAssignAgent(userIds, agentId) {
        return this.post('/api/admin/users/assign-agent', { userIds: userIds, agentId: agentId });
    };

    AdminApiClient.prototype.creditUser = function creditUser(payload) {
        return this.post('/api/admin/users/credit', payload);
    };

    AdminApiClient.prototype.forceEndSession = function forceEndSession(userId, machineId) {
        return this.post('/api/admin/users/' + encodeURIComponent(userId) + '/force-end-session', { machineId: machineId });
    };

    AdminApiClient.prototype.rechargeBonus = function rechargeBonus(userId, rechargeAmount) {
        return this.post('/api/admin/users/recharge-bonus', { userId: userId, rechargeAmount: rechargeAmount });
    };

    /* ---------------------------------------------------------- */
    /* Agents                                                       */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.getAgentsSummary = function getAgentsSummary() {
        return this.get('/api/admin/agents/summary');
    };

    AdminApiClient.prototype.getAgents = function getAgents() {
        return this.get('/api/agent');
    };

    AdminApiClient.prototype.createAgent = function createAgent(payload) {
        return this.post('/api/agent', payload);
    };

    AdminApiClient.prototype.loadCredit = function loadCredit(agentId, amount) {
        return this.post('/api/agent/' + encodeURIComponent(agentId) + '/load-credit', { amount: amount });
    };

    AdminApiClient.prototype.assignUser = function assignUser(agentId, userId) {
        return this.post('/api/agent/' + encodeURIComponent(agentId) + '/assign-user/' + encodeURIComponent(userId), undefined);
    };

    AdminApiClient.prototype.getAgentUsers = function getAgentUsers(agentId) {
        return this.get('/api/agent/' + encodeURIComponent(agentId) + '/users');
    };

    AdminApiClient.prototype.createUserUnderAgent = function createUserUnderAgent(payload) {
        return this.post('/api/agent/create-user', payload);
    };

    /* ---------------------------------------------------------- */
    /* Machines                                                     */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.listMachines = function listMachines() {
        return this.get('/api/admin/machines');
    };

    AdminApiClient.prototype.getMachine = function getMachine(machineId) {
        return this.get('/api/admin/machines/' + encodeURIComponent(machineId));
    };

    AdminApiClient.prototype.getMachineDetail = function getMachineDetail(machineId) {
        return this.get('/api/admin/machines/' + encodeURIComponent(machineId) + '/detail');
    };

    AdminApiClient.prototype.resetMachine = function resetMachine(machineId) {
        return this.post('/api/admin/machines/' + encodeURIComponent(machineId) + '/reset');
    };

    AdminApiClient.prototype.forceResetMachine = function forceResetMachine(machineId) {
        return this.post('/api/admin/machines/' + encodeURIComponent(machineId) + '/force-reset');
    };

    AdminApiClient.prototype.setDoorState = function setDoorState(machineId, doorState) {
        return this.post('/api/admin/machines/' + encodeURIComponent(machineId) + '/door-state', { doorState: doorState });
    };

    /* ---------------------------------------------------------- */
    /* Cabinet devices                                              */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.listCabinetDevices = function listCabinetDevices() {
        return this.get('/api/admin/cabinet-devices');
    };

    AdminApiClient.prototype.getCabinetDevice = function getCabinetDevice(deviceId) {
        return this.get('/api/admin/cabinet-devices/' + encodeURIComponent(deviceId));
    };

    AdminApiClient.prototype.provisionCabinetDevice = function provisionCabinetDevice(payload) {
        return this.post('/api/admin/cabinet-devices', payload);
    };

    AdminApiClient.prototype.revokeCabinetDevice = function revokeCabinetDevice(deviceId, payload) {
        return this.post('/api/admin/cabinet-devices/' + encodeURIComponent(deviceId) + '/revoke', payload);
    };

    /* ---------------------------------------------------------- */
    /* Content management                                            */
    /* ---------------------------------------------------------- */

    AdminApiClient.prototype.listOffers = function listOffers() {
        return this.get('/api/admin/content/offers');
    };

    AdminApiClient.prototype.createOffer = function createOffer(title, description, bonusAmount) {
        return this.post('/api/admin/content/offers', { title: title, description: description, bonusAmount: bonusAmount });
    };

    AdminApiClient.prototype.updateOffer = function updateOffer(id, title, description, bonusAmount) {
        return this.put('/api/admin/content/offers/' + encodeURIComponent(id), { title: title, description: description, bonusAmount: bonusAmount });
    };

    AdminApiClient.prototype.deleteOffer = function deleteOffer(id) {
        return this.del('/api/admin/content/offers/' + encodeURIComponent(id));
    };

    AdminApiClient.prototype.listTerms = function listTerms() {
        return this.get('/api/admin/content/terms');
    };

    AdminApiClient.prototype.upsertTerms = function upsertTerms(version, bodyMarkdown) {
        return this.put('/api/admin/content/terms', { version: version, bodyMarkdown: bodyMarkdown });
    };

    AdminApiClient.prototype.deleteTerms = function deleteTerms() {
        return this.del('/api/admin/content/terms');
    };

    AdminApiClient.prototype.listAppSettings = function listAppSettings() {
        return this.get('/api/admin/content/app-settings');
    };

    AdminApiClient.prototype.upsertAppSetting = function upsertAppSetting(key, value) {
        return this.post('/api/admin/content/app-settings', { key: key, value: value });
    };

    AdminApiClient.prototype.deleteAppSetting = function deleteAppSetting(key) {
        return this.del('/api/admin/content/app-settings/' + encodeURIComponent(key));
    };

    global.AdminSession = AdminSession;
    global.AdminApiError = AdminApiError;
    global.AdminApiClient = AdminApiClient;
    global.AdminApi = new AdminApiClient();
})(window);
