import assert from 'node:assert/strict';
import test from 'node:test';
import { loadEnvironment } from '../src/config/environment.js';

test('validates required secrets without exposing values', () => {
  assert.throws(() => loadEnvironment({}), /OPENAI_API_KEY/);
  assert.throws(() => loadEnvironment({ OPENAI_API_KEY: 'key' }), /FIREBASE_PROJECT_ID/);
});

test('loads validated defaults and port', () => {
  const value = loadEnvironment({ OPENAI_API_KEY: 'key', FIREBASE_PROJECT_ID: 'project', PORT: '9000' });
  assert.equal(value.port, 9000);
  assert.equal(value.model, 'gpt-realtime');
  assert.equal(value.voice, 'marin');
});

test('rejects invalid model, voice, and port', () => {
  const base = { OPENAI_API_KEY: 'key', FIREBASE_PROJECT_ID: 'project' };
  assert.throws(() => loadEnvironment({ ...base, PORT: '0' }), /PORT/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_MODEL: 'bad model' }), /MODEL/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_VOICE: '../bad' }), /VOICE/);
});
