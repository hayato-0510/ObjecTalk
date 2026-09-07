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
  assert.equal(value.model, 'gpt-realtime-2.1');
  assert.equal(value.voice, 'marin');
  assert.deepEqual(value.voicePools, {
    feminineVoices: ['marin', 'coral', 'shimmer'],
    masculineVoices: ['cedar', 'ash', 'echo'],
    neutralVoices: ['alloy', 'sage', 'verse', 'ballad'],
  });
});

test('loads trimmed configurable voice pools', () => {
  const value = loadEnvironment({
    OPENAI_API_KEY: 'key',
    FIREBASE_PROJECT_ID: 'project',
    OPENAI_REALTIME_FEMININE_VOICES: ' marin, coral ',
    OPENAI_REALTIME_MASCULINE_VOICES: 'cedar,echo',
    OPENAI_REALTIME_NEUTRAL_VOICES: 'alloy, verse',
  });

  assert.deepEqual(value.voicePools, {
    feminineVoices: ['marin', 'coral'],
    masculineVoices: ['cedar', 'echo'],
    neutralVoices: ['alloy', 'verse'],
  });
});

test('rejects invalid model, voice, and port', () => {
  const base = { OPENAI_API_KEY: 'key', FIREBASE_PROJECT_ID: 'project' };
  assert.throws(() => loadEnvironment({ ...base, PORT: '0' }), /PORT/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_MODEL: 'bad model' }), /MODEL/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_VOICE: '../bad' }), /VOICE/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_FEMININE_VOICES: 'marin,../bad' }), /FEMININE_VOICES/);
  assert.throws(() => loadEnvironment({ ...base, OPENAI_REALTIME_MASCULINE_VOICES: ',' }), /MASCULINE_VOICES/);
});
