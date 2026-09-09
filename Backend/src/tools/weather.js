// get_weather tool: Open-Meteo geocoding + daily forecast, no API key. Returns one short Japanese
// sentence. Every failure is a soft failure — a string the model can read out — never a throw, so a
// flaky network cannot drop the conversation.

const GEOCODING_URL = 'https://geocoding-api.open-meteo.com/v1/search';
const FORECAST_URL = 'https://api.open-meteo.com/v1/forecast';

// WMO weather codes -> a short Japanese description. Coarse on purpose.
const WEATHER_TEXT = [
  { max: 0, text: '快晴' },
  { max: 3, text: '晴れときどきくもり' },
  { max: 48, text: '霧' },
  { max: 57, text: '霧雨' },
  { max: 67, text: '雨' },
  { max: 77, text: '雪' },
  { max: 82, text: 'にわか雨' },
  { max: 86, text: 'にわか雪' },
  { max: 99, text: '雷雨' },
];

function describeCode(code) {
  if (typeof code !== 'number') return '天気は不明';
  return (WEATHER_TEXT.find((entry) => code <= entry.max) ?? { text: '天気は不明' }).text;
}

function isoOffsetFromToday(now, days) {
  const date = new Date(now.getTime());
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

function resolveTargetDate(date, now) {
  if (date === 'tomorrow') return { iso: isoOffsetFromToday(now, 1), label: 'tomorrow' };
  if (typeof date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(date)) return { iso: date, label: 'iso' };
  return { iso: isoOffsetFromToday(now, 0), label: 'today' };
}

function formatDayLabel(iso) {
  const [, month, day] = iso.split('-');
  return `${Number(month)}月${Number(day)}日`;
}

export function createWeatherTool({ fetchImpl = fetch, now = () => new Date() } = {}) {
  return async function getWeather({ location, date } = {}) {
    if (typeof location !== 'string' || location.trim() === '') {
      return 'どの場所の天気を知りたいか教えてね。';
    }

    let place;
    try {
      const geoUrl = `${GEOCODING_URL}?name=${encodeURIComponent(location.trim())}&count=1&language=ja&format=json`;
      const geo = await (await fetchImpl(geoUrl)).json();
      place = geo?.results?.[0];
    } catch (error) {
      console.error('weather geocoding failed:', error?.message);
      return '天気を調べられなかったよ。';
    }
    if (!place || typeof place.latitude !== 'number') {
      return `「${location.trim()}」の天気は見つからなかったよ。`;
    }

    const target = resolveTargetDate(date, now());
    let daily;
    try {
      const url = `${FORECAST_URL}?latitude=${place.latitude}&longitude=${place.longitude}`
        + '&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max'
        + '&timezone=auto&forecast_days=7';
      daily = (await (await fetchImpl(url)).json())?.daily;
    } catch (error) {
      console.error('weather forecast failed:', error?.message);
      return '天気を調べられなかったよ。';
    }

    const times = Array.isArray(daily?.time) ? daily.time : [];
    let index = times.indexOf(target.iso);
    let note = '';
    if (index < 0) {
      index = 0;
      note = target.label === 'iso' ? '（その日は調べられないから今日の天気だよ）' : '';
    }
    if (index < 0 || times.length === 0) {
      return '天気を調べられなかったよ。';
    }

    const name = place.name ?? location.trim();
    const day = formatDayLabel(times[index]);
    const text = describeCode(daily.weather_code?.[index]);
    const max = daily.temperature_2m_max?.[index];
    const min = daily.temperature_2m_min?.[index];
    const pop = daily.precipitation_probability_max?.[index];

    const parts = [`${name}の${day}: ${text}`];
    if (typeof max === 'number' && typeof min === 'number') parts.push(`最高${Math.round(max)}度・最低${Math.round(min)}度`);
    if (typeof pop === 'number') parts.push(`降水確率${pop}%`);
    return parts.join('、') + '。' + note;
  };
}
