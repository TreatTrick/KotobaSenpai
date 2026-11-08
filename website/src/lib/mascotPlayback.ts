import { MASCOT_FRAMES } from './mascotFrames.ts';

export interface MascotPlayback {
  clip: 'mood' | 'jump';
  cursor: number;
  loopDirection: 1 | -1;
}

/** Preserve fractional frame progress while looping happy tail frames or download celebrations. */
export const advanceMascotFrame = (state: MascotPlayback, direction: number, step: number): MascotPlayback => {
  if (state.clip === 'jump') {
    return { ...state, cursor: (state.cursor + step) % MASCOT_FRAMES.jump };
  }
  if (direction < 0) {
    return { ...state, cursor: Math.max(0, state.cursor - step), loopDirection: 1 };
  }

  // Loop only the final two seconds of happy tail wagging, without replaying the mood transition.
  const start = MASCOT_FRAMES.mood - MASCOT_FRAMES.fps * 2;
  const span = MASCOT_FRAMES.mood - 1 - start;
  const distance = state.cursor - start;
  const travel = (state.loopDirection > 0 ? distance : 2 * span - distance) + step;
  if (travel < 0) return { ...state, cursor: state.cursor + step, loopDirection: 1 };
  const phase = travel % (2 * span);
  return {
    ...state,
    cursor: start + (phase <= span ? phase : 2 * span - phase),
    loopDirection: phase < span ? 1 : -1
  };
};
