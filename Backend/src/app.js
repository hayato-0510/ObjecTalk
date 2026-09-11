import express from 'express';
import { extractBearerToken } from './auth/firebaseAuth.js';
import { buildInstructions } from './conversation/buildInstructions.js';
import { selectVoice } from './conversation/selectVoice.js';
import { createDailyBudget } from './rateLimit/dailyBudget.js';

const MAX_SDP_LENGTH = 200_000;

export function createApp({
  verifyIdToken,
  createRealtimeCall,
  voicePools,
  invokeTool,
  // Per-user request-count limiting is disabled by default: the client's own retry loop can
  // legitimately fire many requests in a short window while a single conversation-start attempt
  // is failing, and that shouldn't lock a visitor out. Pass a real limiter in to re-enable it.
  limiter = () => true,
  dailyBudget = createDailyBudget(),
}) {
  if (typeof verifyIdToken !== 'function' || typeof createRealtimeCall !== 'function') {
    throw new TypeError('Authentication and realtime dependencies are required');
  }
  const app = express();
  app.disable('x-powered-by');
  app.use(express.json({ limit: '256kb' }));
  app.get('/health', (_req, res) => res.json({ status: 'ok' }));

  // Resolves the caller's uid or sends the 401/429 itself and returns null. Shared by every
  // authenticated route so the header-strictness and verification stay identical.
  async function authenticate(req, res) {
    if (countRawHeaders(req.rawHeaders, 'authorization') !== 1) {
      res.status(401).json({ error: 'unauthorized' });
      return null;
    }
    const token = extractBearerToken(req.get('authorization'));
    if (!token) {
      res.status(401).json({ error: 'unauthorized' });
      return null;
    }
    let identity;
    try {
      identity = await verifyIdToken(token);
    } catch (error) {
      console.error('token verification failed:', error?.code ?? error?.message);
      res.status(401).json({ error: 'unauthorized' });
      return null;
    }
    if (!identity?.uid) {
      res.status(401).json({ error: 'unauthorized' });
      return null;
    }
    if (!limiter(identity.uid, req.ip ?? 'unknown')) {
      res.status(429).json({ error: 'rate_limited' });
      return null;
    }
    return identity;
  }

  app.post('/api/realtime/calls', async (req, res) => {
    const identity = await authenticate(req, res);
    if (!identity) return undefined;
    if (!dailyBudget.tryConsume()) return res.status(402).json({ error: 'daily_budget_exhausted' });
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
      if (error?.code === 'insufficient_quota') {
        return res.status(402).json({ error: 'quota_exhausted' });
      }
      return res.status(502).json({ error: 'realtime_unavailable' });
    }
  });

  if (typeof invokeTool === 'function') {
    app.post('/api/tools/invoke', async (req, res) => {
      const identity = await authenticate(req, res);
      if (!identity) return undefined;
      try {
        const { name, args } = validateToolBody(req.body);
        const output = await invokeTool(name, args);
        return res.json({ output });
      } catch (error) {
        if (error instanceof TypeError || error instanceof RangeError) {
          return res.status(400).json({ error: 'invalid_request' });
        }
        console.error('tool invoke failed:', error?.message);
        return res.status(502).json({ error: 'tool_unavailable' });
      }
    });
  }

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
  const mode = body.mode ?? undefined;
  if (mode !== undefined && mode !== 'exhibition') throw new RangeError('mode');
  const category = optionalCategory(body.category);
  return { objectId, objectName, personalityPrompt, language, sdp, mode, category };
}

function validateToolBody(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) throw new TypeError('body');
  const name = bounded(body.name, 'name', 1, 40);
  const args = body.arguments;
  if (args !== undefined && typeof args !== 'string' && (typeof args !== 'object' || args === null)) {
    throw new TypeError('arguments');
  }
  // The tool arguments (place names, search queries) are never logged.
  return { name, args };
}

function optionalCategory(value) {
  if (value === undefined) return undefined;
  if (typeof value !== 'string') throw new TypeError('category');
  if (value.trim().length > 50) throw new RangeError('category');
  return value;
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
