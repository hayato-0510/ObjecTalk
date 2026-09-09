import assert from 'node:assert/strict';
import test from 'node:test';

import { createWeatherTool } from '../src/tools/weather.js';

function stubFetch(routes) {
  return async (url) => {
    const key = Object.keys(routes).find((fragment) => String(url).includes(fragment));
    if (!key) throw new Error(`unexpected url: ${url}`);
    const value = routes[key];
    if (value instanceof Error) throw value;
    return { ok: true, json: async () => value };
  };
}

const GEOCODE_SUZUKA = {
  results: [{ latitude: 34.88, longitude: 136.58, name: '鈴鹿市' }],
};

function forecast(days) {
  return {
    daily: {
      time: days.map((d) => d.date),
      weather_code: days.map((d) => d.code),
      temperature_2m_max: days.map((d) => d.max),
      temperature_2m_min: days.map((d) => d.min),
      precipitation_probability_max: days.map((d) => d.pop),
    },
  };
}

test('summarises today by default', async () => {
  const getWeather = createWeatherTool({ now: () => new Date('2026-09-10T00:00:00Z'),
    fetchImpl: stubFetch({
      'geocoding-api': GEOCODE_SUZUKA,
      '/v1/forecast': forecast([
        { date: '2026-09-10', code: 3, max: 29, min: 21, pop: 20 },
        { date: '2026-09-11', code: 61, max: 26, min: 20, pop: 70 },
      ]),
    }),
  });

  const summary = await getWeather({ location: '鈴鹿市' });

  assert.match(summary, /鈴鹿市/);
  assert.match(summary, /9月10日/);
  assert.match(summary, /29度/);
  assert.match(summary, /20%/);
});

test('resolves tomorrow to the next day', async () => {
  const getWeather = createWeatherTool({ now: () => new Date('2026-09-10T00:00:00Z'),
    fetchImpl: stubFetch({
      'geocoding-api': GEOCODE_SUZUKA,
      '/v1/forecast': forecast([
        { date: '2026-09-10', code: 0, max: 30, min: 22, pop: 0 },
        { date: '2026-09-11', code: 61, max: 26, min: 20, pop: 80 },
      ]),
    }),
  });

  const summary = await getWeather({ location: '鈴鹿市', date: 'tomorrow' });

  assert.match(summary, /9月11日/);
  assert.match(summary, /雨/);
  assert.match(summary, /80%/);
});

test('resolves an ISO date that is inside the forecast window', async () => {
  const getWeather = createWeatherTool({ now: () => new Date('2026-09-10T00:00:00Z'),
    fetchImpl: stubFetch({
      'geocoding-api': GEOCODE_SUZUKA,
      '/v1/forecast': forecast([
        { date: '2026-09-10', code: 0, max: 30, min: 22, pop: 0 },
        { date: '2026-09-11', code: 2, max: 28, min: 21, pop: 10 },
        { date: '2026-09-12', code: 95, max: 25, min: 20, pop: 90 },
      ]),
    }),
  });

  const summary = await getWeather({ location: '鈴鹿市', date: '2026-09-12' });

  assert.match(summary, /9月12日/);
  assert.match(summary, /雷/);
});

test('reports when the place cannot be found', async () => {
  const getWeather = createWeatherTool({ now: () => new Date('2026-09-10T00:00:00Z'),
    fetchImpl: stubFetch({ 'geocoding-api': { results: [] } }),
  });

  const summary = await getWeather({ location: 'どこでもない場所' });

  assert.match(summary, /見つからなかった/);
});

test('reports a soft failure when the forecast request throws', async () => {
  const getWeather = createWeatherTool({ now: () => new Date('2026-09-10T00:00:00Z'),
    fetchImpl: stubFetch({
      'geocoding-api': GEOCODE_SUZUKA,
      '/v1/forecast': new Error('network down'),
    }),
  });

  const summary = await getWeather({ location: '鈴鹿市' });

  assert.match(summary, /調べられなかった/);
});

test('missing location is a soft failure, not a throw', async () => {
  const getWeather = createWeatherTool({ fetchImpl: stubFetch({}) });

  const summary = await getWeather({});
  assert.match(summary, /場所/);
});
