import test from 'node:test';
import assert from 'node:assert/strict';
import { MASCOT_FRAMES } from '../src/lib/mascotFrames.ts';
import { advanceMascotFrame } from '../src/lib/mascotPlayback.ts';

const happyStart = MASCOT_FRAMES.mood - MASCOT_FRAMES.fps * 2;
const happyEnd = MASCOT_FRAMES.mood - 1;
const mood = (cursor, loopDirection = 1) => ({ clip: 'mood', cursor, loopDirection });

test('happy playback reverses at the last frame without losing fractional progress', () => {
  assert.deepEqual(advanceMascotFrame(mood(happyEnd - 0.5), 1, 2), mood(happyEnd - 1.5, -1));
});

test('happy playback reverses again at the start of the tail frames', () => {
  assert.deepEqual(advanceMascotFrame(mood(happyStart + 0.5, -1), 1, 2), mood(happyStart + 1.5));
});

test('a sad pose advances into the happy loop without skipping the transition', () => {
  assert.deepEqual(advanceMascotFrame(mood(0), 1, 2.4), mood(2.4));
  assert.deepEqual(advanceMascotFrame(mood(happyStart - 0.5), 1, 2), mood(happyStart + 1.5));
});

test('happy tail playback keeps moving and remains in the happy frames over many cycles', () => {
  let state = mood(happyEnd);
  const seen = new Set();
  for (let frame = 0; frame < 1000; frame++) {
    state = advanceMascotFrame(state, 1, 1);
    assert.ok(state.cursor >= happyStart && state.cursor <= happyEnd);
    assert.equal(state.clip, 'mood');
    seen.add(state.cursor);
  }
  assert.equal(seen.size, MASCOT_FRAMES.fps * 2);
});

test('exit intent leaves the happy loop and stops at the sad pose', () => {
  let state = advanceMascotFrame(mood(happyStart + 2, -1), -1, 3);
  assert.deepEqual(state, mood(happyStart - 1));
  state = advanceMascotFrame(state, -1, MASCOT_FRAMES.mood);
  assert.deepEqual(state, mood(0));
  assert.deepEqual(advanceMascotFrame(state, -1, 2), mood(0));
});

test('download celebration wraps fractional progress and keeps jumping for many cycles', () => {
  let state = { clip: 'jump', cursor: MASCOT_FRAMES.jump - 0.5, loopDirection: 1 };
  state = advanceMascotFrame(state, -1, 2);
  assert.deepEqual(state, { clip: 'jump', cursor: 1.5, loopDirection: 1 });
  for (let cycle = 0; cycle < 100; cycle++) {
    state = advanceMascotFrame(state, cycle % 2 ? -1 : 1, MASCOT_FRAMES.jump);
    assert.deepEqual(state, { clip: 'jump', cursor: 1.5, loopDirection: 1 });
  }
});
