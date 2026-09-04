const MODEL_PATTERN = /^[a-zA-Z0-9._-]{1,100}$/;
const VOICE_PATTERN = /^[a-zA-Z0-9_-]{1,50}$/;

export function loadEnvironment(env = process.env) {
  const openAiApiKey = required(env.OPENAI_API_KEY, 'OPENAI_API_KEY');
  const firebaseProjectId = required(env.FIREBASE_PROJECT_ID, 'FIREBASE_PROJECT_ID');
  const model = optionalValidated(env.OPENAI_REALTIME_MODEL, 'gpt-realtime', MODEL_PATTERN, 'OPENAI_REALTIME_MODEL');
  const voice = optionalValidated(env.OPENAI_REALTIME_VOICE, 'marin', VOICE_PATTERN, 'OPENAI_REALTIME_VOICE');
  const port = parsePort(env.PORT ?? '8080');
  return Object.freeze({ openAiApiKey, firebaseProjectId, model, voice, port });
}

function required(value, name) {
  if (typeof value !== 'string' || value.trim() === '') throw new Error(`${name} is required`);
  return value.trim();
}

function optionalValidated(value, fallback, pattern, name) {
  const normalized = typeof value === 'string' && value.trim() !== '' ? value.trim() : fallback;
  if (!pattern.test(normalized)) throw new Error(`${name} is invalid`);
  return normalized;
}

function parsePort(value) {
  if (!/^\d+$/.test(String(value))) throw new Error('PORT is invalid');
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT is invalid');
  return port;
}
