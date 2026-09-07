const FEMININE_VOICES = Object.freeze(['marin', 'coral', 'shimmer']);
const MASCULINE_VOICES = Object.freeze(['cedar', 'ash', 'echo']);
const NEUTRAL_VOICES = Object.freeze(['alloy', 'sage', 'verse', 'ballad']);

export function selectVoice({ objectId, objectName }, {
  feminineVoices = FEMININE_VOICES,
  masculineVoices = MASCULINE_VOICES,
  neutralVoices = NEUTRAL_VOICES,
} = {}) {
  if (objectName.endsWith('ちゃん')) {
    return feminineVoices[stableIndex(objectId, feminineVoices.length)];
  }
  if (objectName.endsWith('くん')) {
    return masculineVoices[stableIndex(objectId, masculineVoices.length)];
  }
  return neutralVoices[stableIndex(objectId, neutralVoices.length)];
}

function stableIndex(value, length) {
  let total = 0;
  for (const character of value) total += character.codePointAt(0);
  return total % length;
}
