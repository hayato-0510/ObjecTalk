import express from 'express';
import { extractBearerToken } from './auth/firebaseAuth.js';
import { buildInstructions } from './conversation/buildInstructions.js';
import { selectVoice } from './conversation/selectVoice.js';
import { createInMemoryLimiter } from './rateLimit/inMemoryLimiter.js';

const MAX_SDP_LENGTH = 200_000;

export function createApp({ verifyIdToken, createRealtimeCall, voicePools, limiter = createInMemoryLimiter() }) {
  if (typeof verifyIdToken !== 'function' || typeof createRealtimeCall !== 'function') {
    throw new TypeError('Authentication and realtime dependencies are required');
  }
  const app = express();
  app.disable('x-powered-by');
  app.use(express.json({ limit: '256kb' }));
  app.get('/health', (_req, res) => res.json({ status: 'ok' }));
  app.post('/api/realtime/calls', async (req, res) => {
    if (countRawHeaders(req.rawHeaders, 'authorization') !== 1) {
      return res.status(401).json({ error: 'unauthorized' });
    }
    const token = extractBearerToken(req.get('authorization'));
    if (!token) return res.status(401).json({ error: 'unauthorized' });
    let identity;
    try {
      identity = await verifyIdToken(token);
    } catch {
      return res.status(401).json({ error: 'unauthorized' });
    }
    if (!identity?.uid) return res.status(401).json({ error: 'unauthorized' });
    if (!limiter(identity.uid, req.ip ?? 'unknown')) return res.status(429).json({ error: 'rate_limited' });
    try {
      const input = validateBody(req.body);
      const instructions = buildInstructions(input);
      const voice = selectVoice(input, voicePools);
      const answer = await createRealtimeCall({ sdp: input.sdp, instructions, voice });
      if (typeof answer !== 'string' || answer.trim() === '') throw new Error('Empty SDP answer');
      return res.type('application/sdp').send(answer);
    } catch (error) {
      if (error instanceof TypeError || error instanceof RangeError) {
        return res.status(400).json({ error: 'invalid_request' });
      }
      return res.status(502).json({ error: 'realtime_unavailable' });
    }
  });
  app.use((error, _req, res, _next) => {
    if (error?.type === 'entity.too.large' || error instanceof SyntaxError) {
      return res.status(400).json({ error: 'invalid_request' });
    }
    return res.status(500).json({ error: 'internal_error' });
  });
  return app;
}

export function countRawHeaders(rawHeaders, requestedName) {
  let count = 0;
  for (let index = 0; index < rawHeaders.length; index += 2) {
    if (String(rawHeaders[index]).toLowerCase() === requestedName) count += 1;
  }
  return count;
}

function validateBody(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new TypeError('body');
  const objectId = bounded(body.objectId, 'objectId', 1, 128);
  const objectName = bounded(body.objectName, 'objectName', 1, 100);
  const personalityPrompt = body.personalityPrompt ?? '';
  if (typeof personalityPrompt !== 'string') throw new TypeError('personalityPrompt');
  const language = bounded(body.language, 'language', 2, 10);
  if (language !== 'ja') throw new RangeError('language');
  const sdp = rawBounded(body.sdp, 'sdp', 1, MAX_SDP_LENGTH);
  return { objectId, objectName, personalityPrompt, language, sdp };
}

function bounded(value, field, min, max) {
  if (typeof value !== 'string') throw new TypeError(field);
  const normalized = value.trim();
  if (normalized.length < min || normalized.length > max) throw new RangeError(field);
  return normalized;
}

function rawBounded(value, field, min, max) {
  if (typeof value !== 'string' || value.length < min || value.length > max || value.trim() === '') {
    throw new RangeError(field);
  }
  return value;
}
