export function createInMemoryLimiter({ limit = 10, windowMs = 60_000, now = Date.now } = {}) {
  const entries = new Map();
  return function allow(uid, ip) {
    const key = `${uid}:${ip}`;
    const current = now();
    const entry = entries.get(key);
    if (!entry || current - entry.startedAt >= windowMs) {
      entries.set(key, { startedAt: current, count: 1 });
      return true;
    }
    if (entry.count >= limit) return false;
    entry.count += 1;
    return true;
  };
}
