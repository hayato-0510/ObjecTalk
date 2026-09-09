// Object category -> one extra persona line for the system prompt. The registration flow lets the
// operator type a free-text category; only an exact (trimmed) match against these seven natural
// Japanese labels adds a line. Anything else — including 「その他」 — adds nothing, so the feature
// silently no-ops rather than guessing.
const PERSONA_BY_CATEGORY = Object.freeze({
  電子機器: 'あなたは電子機器です。仕組み・使い方・作れるものを、中学生にもわかる言葉で短く話します。',
  飲みもの: 'あなたは飲みものです。中身や体との関わりを、やさしく短く話します。',
  傘: 'あなたは傘です。今日必要かどうかや、天気の話題に自然につなげます。',
  本: 'あなたは本です。中身・ジャンル・似たものの話をします。',
  文房具: 'あなたは文房具です。使い方の工夫や、勉強・ものづくりの話をします。',
  アクセサリ: 'あなたは小物・アクセサリです。作り方や使いどころの話をします。',
});

export function categoryPersona(category) {
  if (typeof category !== 'string') return '';
  return PERSONA_BY_CATEGORY[category.trim()] ?? '';
}
