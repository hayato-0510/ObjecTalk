// A whole-deployment daily cap on OpenAI Realtime usage, for the open-campus exhibition where a
// busy booth could otherwise run up the bill. Each accepted call consumes an estimated session
// length; the tally resets at local midnight. With no maxMinutes configured it never blocks.

export function createDailyBudget({ maxMinutes = null, minutesPerSession = 3, now = () => new Date() } = {}) {
  if (maxMinutes != null && (!Number.isFinite(maxMinutes) || maxMinutes <= 0)) {
    throw new RangeError('maxMinutes must be a positive number');
  }
  if (!Number.isFinite(minutesPerSession) || minutesPerSession <= 0) {
    throw new RangeError('minutesPerSession must be a positive number');
  }

  let day = null;
  let usedMinutes = 0;

  function rollOver() {
    const today = now().toLocaleDateString('en-CA'); // YYYY-MM-DD in local time
    if (today !== day) {
      day = today;
      usedMinutes = 0;
    }
  }

  return {
    // Call once per accepted realtime session. Returns false when today's budget is spent.
    tryConsume() {
      if (maxMinutes == null) return true;
      rollOver();
      if (usedMinutes + minutesPerSession > maxMinutes) return false;
      usedMinutes += minutesPerSession;
      return true;
    },
    remainingMinutes() {
      if (maxMinutes == null) return Infinity;
      rollOver();
      return Math.max(0, maxMinutes - usedMinutes);
    },
  };
}
