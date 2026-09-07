const MODEL_PATTERN = /^[a-zA-Z0-9._-]{1,100}$/;
const VOICE_PATTERN = /^[a-zA-Z0-9_-]{1,50}$/;

export function loadEnvironment(env = process.env) {
  const openAiApiKey = required(env.OPENAI_API_KEY, 'OPENAI_API_KEY');
  const firebaseProjectId = required(env.FIREBASE_PROJECT_ID, 'FIREBASE_PROJECT_ID');
  const model = optionalValidated(env.OPENAI_REALTIME_MODEL, 'gpt-realtime-2.1', MODEL_PATTERN, 'OPENAI_REALTIME_MODEL');
  const voice = optionalValidated(env.OPENAI_REALTIME_VOICE, 'marin', VOICE_PATTERN, 'OPENAI_REALTIME_VOICE');
  const port = parsePort(env.PORT ?? '8080');
  const voicePools = Object.freeze({
    feminineVoices: optionalVoiceList(env.OPENAI_REALTIME_FEMININE_VOICES, ['marin', 'coral', 'shimmer'], 'OPENAI_REALTIME_FEMININE_VOICES'),
    masculineVoices: optionalVoiceList(env.OPENAI_REALTIME_MASCULINE_VOICES, ['cedar', 'ash', 'echo'], 'OPENAI_REALTIME_MASCULINE_VOICES'),
    neutralVoices: optionalVoiceList(env.OPENAI_REALTIME_NEUTRAL_VOICES, ['alloy', 'sage', 'verse', 'ballad'], 'OPENAI_REALTIME_NEUTRAL_VOICES'),
  });
  return Object.freeze({ openAiApiKey, firebaseProjectId, model, voice, voicePools, port });
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

function optionalVoiceList(value, fallback, name) {
  if (typeof value !== 'string' || value.trim() === '') return Object.freeze([...fallback]);
  const voices = value.split(',').map((voice) => voice.trim()).filter(Boolean);
  if (voices.length === 0 || voices.some((voice) => !VOICE_PATTERN.test(voice))) {
    throw new Error(`${name} is invalid`);
  }
  return Object.freeze(voices);
}

function parsePort(value) {
  if (!/^\d+$/.test(String(value))) throw new Error('PORT is invalid');
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('PORT is invalid');
  return port;
}
