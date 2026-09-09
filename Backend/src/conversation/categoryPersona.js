// Object category -> one extra persona line for the system prompt. The registration flow lets the
// operator type a free-text category; only an exact (trimmed) match — after a few obvious hiragana
// spellings are folded to the canonical label — adds a line. Anything else, 「その他」 included,
// adds nothing, so the feature silently no-ops rather than guessing.

const PERSONA_BY_CATEGORY = Object.freeze({
  電子機器: 'あなたは電子機器です。仕組み・使い方・作れるものを、中学生にもわかる言葉で短く話します。',
  飲みもの: 'あなたは飲みものです。中身や体との関わりを、やさしく短く話します。',
  傘: 'あなたは傘です。今日わたし（傘）を持っていったほうがいいか、天気の話題に自然につなげます。',
  本: 'あなたは本です。中身・ジャンル・似たものの話をします。',
  文房具: 'あなたは文房具です。使い方の工夫や、勉強・ものづくりの話をします。',
  アクセサリ: 'あなたは小物・アクセサリです。作り方や使いどころの話をします。',
});

// A short list of the spellings the operator is likely to type instead of the canonical label.
const ALIASES = Object.freeze({
  かさ: '傘',
  ほん: '本',
  のみもの: '飲みもの',
  '飲み物': '飲みもの',
  ぶんぼうぐ: '文房具',
  アクセサリー: 'アクセサリ',
  でんしきき: '電子機器',
});

export function canonicalCategory(category) {
  if (typeof category !== 'string') return '';
  const trimmed = category.trim();
  return ALIASES[trimmed] ?? trimmed;
}

export function categoryPersona(category) {
  return PERSONA_BY_CATEGORY[canonicalCategory(category)] ?? '';
}
