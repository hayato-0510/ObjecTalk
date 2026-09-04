import { getAuth } from 'firebase-admin/auth';

export function extractBearerToken(header) {
  if (typeof header !== 'string' || header.includes(',')) return null;
  const match = /^Bearer ([^\s]+)$/.exec(header);
  return match?.[1] ?? null;
}

export function createFirebaseTokenVerifier(auth = getAuth()) {
  return async function verifyIdToken(token) {
    return auth.verifyIdToken(token, true);
  };
}
