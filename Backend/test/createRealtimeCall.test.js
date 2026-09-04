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
  assert.equal(session.audio.output.voice, 'marin');
});

test('does not expose upstream response body in failure', async () => {
  const call = createOpenAIRealtimeCall({ apiKey: 'key', model: 'model', voice: 'voice', fetchImpl: async () => ({ ok: false, text: async () => 'SECRET' }) });
  await assert.rejects(call({ sdp: 'offer', instructions: 'rules' }), (error) => !error.message.includes('SECRET'));
});
