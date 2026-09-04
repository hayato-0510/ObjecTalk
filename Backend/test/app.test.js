import assert from 'node:assert/strict';
import http from 'node:http';
import test from 'node:test';
import { countRawHeaders, createApp } from '../src/app.js';

async function withServer(dependencies, run) {
  const server = createApp(dependencies).listen(0);
  await new Promise((resolve) => server.once('listening', resolve));
  try {
    await run(`http://127.0.0.1:${server.address().port}`);
  } finally {
    await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  }
}

const validBody = {
  objectId: 'object-1', objectName: 'りんご', personalityPrompt: '', language: 'ja', sdp: 'v=0\r\n',
};

function dependencies(overrides = {}) {
  return {
    verifyIdToken: async () => ({ uid: 'user-1' }),
    createRealtimeCall: async () => 'v=0\r\nanswer',
    limiter: () => true,
    ...overrides,
  };
}

test('health has no dependency secrets', async () => {
  await withServer(dependencies(), async (base) => {
    const response = await fetch(`${base}/health`);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { status: 'ok' });
  });
});

test('requires one strict bearer token', async () => {
  let verificationCalls = 0;
  await withServer(dependencies({ verifyIdToken: async () => { verificationCalls++; return { uid: 'u' }; } }), async (base) => {
    for (const authorization of [undefined, 'Basic token', 'Bearer', 'Bearer one,two', 'Bearer one two']) {
      const headers = { 'content-type': 'application/json' };
      if (authorization) headers.authorization = authorization;
      const response = await fetch(`${base}/api/realtime/calls`, { method: 'POST', headers, body: JSON.stringify(validBody) });
      assert.equal(response.status, 401);
    }
    assert.equal(verificationCalls, 0);
  });
});

test('rejects duplicate raw Authorization headers before verification', async () => {
  let verificationCalls = 0;
  await withServer(dependencies({ verifyIdToken: async () => { verificationCalls++; return { uid: 'u' }; } }), async (base) => {
    const response = await rawRequest(base, [
      'Authorization', 'Bearer first',
      'Authorization', 'Bearer second',
      'Content-Type', 'application/json',
      'Content-Length', String(Buffer.byteLength(JSON.stringify(validBody))),
    ], JSON.stringify(validBody));
    assert.ok(response.status === 400 || response.status === 401);
    assert.equal(verificationCalls, 0);
  });
});

test('counts duplicate Authorization fields from the untouched raw header list', () => {
  assert.equal(countRawHeaders([
    'Host', 'localhost',
    'Authorization', 'Bearer first',
    'authorization', 'Bearer second',
  ], 'authorization'), 2);
});

test('returns SDP only after authentication and validation', async () => {
  let upstreamInput;
  await withServer(dependencies({ createRealtimeCall: async (input) => { upstreamInput = input; return 'v=0\r\nanswer'; } }), async (base) => {
    const response = await fetch(`${base}/api/realtime/calls`, {
      method: 'POST', headers: { authorization: 'Bearer valid-token', 'content-type': 'application/json' }, body: JSON.stringify(validBody),
    });
    assert.equal(response.status, 200);
    assert.match(response.headers.get('content-type'), /^application\/sdp/);
    assert.equal(await response.text(), 'v=0\r\nanswer');
    assert.equal(upstreamInput.sdp, validBody.sdp);
    assert.match(upstreamInput.instructions, /りんご/);
  });
});

test('rejects invalid input without calling upstream', async () => {
  let calls = 0;
  await withServer(dependencies({ createRealtimeCall: async () => { calls++; return 'answer'; } }), async (base) => {
    for (const change of [{ objectId: '' }, { language: 'en' }, { sdp: '' }, { personalityPrompt: 2 }]) {
      const response = await fetch(`${base}/api/realtime/calls`, {
        method: 'POST', headers: { authorization: 'Bearer token', 'content-type': 'application/json' }, body: JSON.stringify({ ...validBody, ...change }),
      });
      assert.equal(response.status, 400);
    }
    assert.equal(calls, 0);
  });
});

test('rate limits by verified identity before upstream', async () => {
  let count = 0;
  await withServer(dependencies({ limiter: () => ++count <= 1 }), async (base) => {
    const options = { method: 'POST', headers: { authorization: 'Bearer token', 'content-type': 'application/json' }, body: JSON.stringify(validBody) };
    assert.equal((await fetch(`${base}/api/realtime/calls`, options)).status, 200);
    assert.equal((await fetch(`${base}/api/realtime/calls`, options)).status, 429);
  });
});

test('sanitizes authentication and upstream failures', async () => {
  await withServer(dependencies({ verifyIdToken: async () => { throw new Error('SECRET_TOKEN'); } }), async (base) => {
    const response = await fetch(`${base}/api/realtime/calls`, { method: 'POST', headers: { authorization: 'Bearer SECRET_TOKEN', 'content-type': 'application/json' }, body: JSON.stringify(validBody) });
    assert.equal(response.status, 401);
    assert.doesNotMatch(await response.text(), /SECRET_TOKEN/);
  });
  await withServer(dependencies({ createRealtimeCall: async () => { throw new Error('UPSTREAM_SECRET_BODY'); } }), async (base) => {
    const response = await fetch(`${base}/api/realtime/calls`, { method: 'POST', headers: { authorization: 'Bearer token', 'content-type': 'application/json' }, body: JSON.stringify(validBody) });
    assert.equal(response.status, 502);
    assert.doesNotMatch(await response.text(), /UPSTREAM_SECRET_BODY/);
  });
});

function rawRequest(base, headers, body) {
  return new Promise((resolve, reject) => {
    const request = http.request(`${base}/api/realtime/calls`, { method: 'POST', headers }, (response) => {
      response.resume();
      response.once('end', () => resolve({ status: response.statusCode }));
    });
    request.once('error', reject);
    request.end(body);
  });
}
