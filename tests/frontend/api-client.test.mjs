import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

function loadApiClient() {
    const window = {};
    vm.runInNewContext(
        readFileSync('server/src/Lucky5.Api/wwwroot/js/api-client.js', 'utf8'),
        { window, console }
    );
    return window.Lucky5ApiClient;
}

test('ApiClient unwraps API data and normalizes response keys', async () => {
    const api = loadApiClient();
    const requests = [];
    const client = new api.ApiClient({
        baseUrl: 'https://example.test',
        tokenProvider: () => 'test-token',
        fetchImpl: async (url, options) => {
            requests.push({ url, options });
            return new Response(JSON.stringify({
                success: true,
                data: { MachineId: 7, NestedValue: { IsOpen: true } }
            }), { status: 200, headers: { 'content-type': 'application/json' } });
        }
    });

    const result = await client.get('/api/machines');

    assert.equal(JSON.stringify(result), JSON.stringify({ machineId: 7, nestedValue: { isOpen: true } }));
    assert.equal(requests[0].url, 'https://example.test/api/machines');
    assert.equal(requests[0].options.headers.Authorization, 'Bearer test-token');
});

test('ApiClient maps structured API failures to ApiError', async () => {
    const api = loadApiClient();
    const client = new api.ApiClient({
        fetchImpl: async () => new Response(JSON.stringify({
            success: false,
            message: 'Try again later',
            errors: ['temporary failure']
        }), { status: 503 })
    });

    await assert.rejects(
        () => client.post('/api/game/deal', { amount: 1 }),
        error => error instanceof api.ApiError
            && error.status === 503
            && error.message === 'Try again later'
            && error.retryable === true
    );
});

test('ApiClient centralizes SignalR invocation', async () => {
    const api = loadApiClient();
    const calls = [];
    const client = new api.ApiClient();
    const connection = {
        invoke: async (...args) => {
            calls.push(args);
            return { ok: true };
        }
    };

    const result = await client.invokeHub(connection, 'Heartbeat', 1);

    assert.equal(JSON.stringify(result), JSON.stringify({ ok: true }));
    assert.deepEqual(calls, [['Heartbeat', 1]]);
});
