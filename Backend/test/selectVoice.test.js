import assert from 'node:assert/strict';
import test from 'node:test';
import { selectVoice } from '../src/conversation/selectVoice.js';

const pools = Object.freeze({
  feminineVoices: ['feminine-1', 'feminine-2'],
  masculineVoices: ['masculine-1', 'masculine-2'],
  neutralVoices: ['neutral-1', 'neutral-2', 'neutral-3', 'neutral-4'],
});

test('selects from the feminine pool only when the name ends in chan', () => {
  assert.equal(selectVoice({ objectId: 'object-a', objectName: 'りんごちゃん' }, pools), 'feminine-2');
  assert.equal(selectVoice({ objectId: 'object-a', objectName: 'ちゃんちゃん人形' }, pools), 'neutral-2');
});

test('selects from the masculine pool when the name ends in kun', () => {
  assert.equal(selectVoice({ objectId: 'object-a', objectName: 'とけいくん' }, pools), 'masculine-2');
});

test('keeps the neutral voice stable per object id while varying between objects', () => {
  assert.equal(selectVoice({ objectId: 'object-a', objectName: 'りんご' }, pools), 'neutral-2');
  assert.equal(selectVoice({ objectId: 'object-a', objectName: '時計' }, pools), 'neutral-2');
  assert.equal(selectVoice({ objectId: 'object-b', objectName: 'りんご' }, pools), 'neutral-3');
});
