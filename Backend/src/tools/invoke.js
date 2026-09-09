import { toolNames } from './registry.js';

// Dispatches a model tool call to the concrete tool with a timeout. Unknown tool -> TypeError (the
// route turns that into a 400). Every other failure — a tool throwing, a timeout — becomes a short
// Japanese string so the conversation keeps going and the model can say it could not find out.

function parseArgs(args) {
  if (args === null || args === undefined) return {};
  if (typeof args === 'object') return args;
  if (typeof args === 'string') {
    try {
      const parsed = JSON.parse(args);
      return parsed && typeof parsed === 'object' ? parsed : {};
    } catch {
      throw new TypeError('tool arguments must be valid JSON');
    }
  }
  throw new TypeError('tool arguments must be a string or object');
}

export function createToolInvoker({ getWeather, webSearch, timeoutMs = 8000 } = {}) {
  const tools = { get_weather: getWeather, web_search: webSearch };

  return async function invokeTool(name, args) {
    if (typeof name !== 'string' || !toolNames.has(name)) {
      throw new TypeError(`unknown tool: ${name}`);
    }
    const tool = tools[name];
    if (typeof tool !== 'function') {
      throw new TypeError(`tool not configured: ${name}`);
    }

    const parsed = parseArgs(args);

    let timer;
    const timeout = new Promise((resolve) => {
      timer = setTimeout(() => resolve('時間内に調べられなかったよ。'), timeoutMs);
    });

    try {
      const result = await Promise.race([
        Promise.resolve()
          .then(() => tool(parsed))
          .catch((error) => {
            console.error(`tool ${name} failed:`, error?.message);
            return '調べられなかったよ。';
          }),
        timeout,
      ]);
      return typeof result === 'string' && result.trim() !== '' ? result : '調べられなかったよ。';
    } finally {
      clearTimeout(timer);
    }
  };
}
