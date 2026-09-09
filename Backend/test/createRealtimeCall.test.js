import assert from 'node:assert/strict';
import test from 'node:test';
import { createOpenAIRealtimeCall } from '../src/realtime/createRealtimeCall.js';

test('sends server session and API key to the OpenAI endpoint', async () => {
  let request;
  const call = createOpenAIRealtimeCall({
    apiKey: 'server-key', model: 'gpt-realtime', voice: 'marin',
    fetchImpl: async (url, options) => { request = { url, options }; return { ok: true, text: async () => 'answer' }; },
  });
  assert.equal(await call({ sdp: 'offer', instructions: 'safe rules' }), 'answer');
  assert.equal(request.url, 'https://api.openai.com/v1/realtime/calls');
  assert.equal(request.options.headers.Authorization, 'Bearer server-key');
  assert.equal(request.options.body.get('sdp'), 'offer');
  const session = JSON.parse(request.options.body.get('session'));
  assert.equal(session.type, 'realtime');
  assert.deepEqual(session.output_modalities, ['audio']);
  assert.equal(session.audio.input.turn_detection.type, 'server_vad');
  // Echo guard: the client has no AEC, so the assistant's own voice leaks into the mic.
  assert.equal(session.audio.input.turn_detection.threshold, 0.8);
  assert.equal(session.audio.input.turn_detection.silence_duration_ms, 1000);
  assert.equal(session.audio.output.voice, 'marin');
});

test('uses the per-object voice selected for this realtime session', async () => {
  let request;
  const call = createOpenAIRealtimeCall({
    apiKey: 'server-key', model: 'gpt-realtime-2.1', voice: 'marin',
    fetchImpl: async (url, options) => { request = { url, options }; return { ok: true, text: async () => 'answer' }; },
  });

  await call({ sdp: 'offer', instructions: 'safe rules', voice: 'cedar' });

  const session = JSON.parse(request.options.body.get('session'));
  assert.equal(session.audio.output.voice, 'cedar');
});

test('does not expose upstream response body in failure', async () => {
  const call = createOpenAIRealtimeCall({ apiKey: 'key', model: 'model', voice: 'voice', fetchImpl: async () => ({ ok: false, status: 500, text: async () => 'SECRET' }) });
  await assert.rejects(call({ sdp: 'offer', instructions: 'rules' }), (error) => !error.message.includes('SECRET') && error.code === undefined);
});

test('tags an exhausted-credit 429 so the caller can stop retrying', async () => {
  const body = JSON.stringify({ error: { type: 'insufficient_quota', code: 'credit_balance_exhausted', message: 'no credits' } });
  const call = createOpenAIRealtimeCall({ apiKey: 'key', model: 'model', voice: 'voice', fetchImpl: async () => ({ ok: false, status: 429, text: async () => body }) });
  await assert.rejects(call({ sdp: 'offer', instructions: 'rules' }), (error) => error.code === 'insufficient_quota');
});

test('leaves a transient 429 untagged so it stays retryable', async () => {
  const body = JSON.stringify({ error: { type: 'rate_limit_exceeded', message: 'slow down' } });
  const call = createOpenAIRealtimeCall({ apiKey: 'key', model: 'model', voice: 'voice', fetchImpl: async () => ({ ok: false, status: 429, text: async () => body }) });
  await assert.rejects(call({ sdp: 'offer', instructions: 'rules' }), (error) => error.code === undefined);
});

test('registers tools and tool_choice auto when tools are provided', async () => {
  let request;
  const tools = [{ type: 'function', name: 'get_weather', description: 'd', parameters: { type: 'object', properties: {} } }];
  const call = createOpenAIRealtimeCall({
    apiKey: 'k', model: 'm', voice: 'marin', tools,
    fetchImpl: async (url, options) => { request = { url, options }; return { ok: true, text: async () => 'answer' }; },
  });
  await call({ sdp: 'offer', instructions: 'rules' });
  const session = JSON.parse(request.options.body.get('session'));
  assert.deepEqual(session.tools, tools);
  assert.equal(session.tool_choice, 'auto');
});

test('omits tools and tool_choice when none are provided', async () => {
  let request;
  const call = createOpenAIRealtimeCall({
    apiKey: 'k', model: 'm', voice: 'marin',
    fetchImpl: async (url, options) => { request = { url, options }; return { ok: true, text: async () => 'answer' }; },
  });
  await call({ sdp: 'offer', instructions: 'rules' });
  const session = JSON.parse(request.options.body.get('session'));
  assert.equal('tools' in session, false);
  assert.equal('tool_choice' in session, false);
});
