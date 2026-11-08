<script lang="ts">
  import { onMount } from 'svelte';
  import { asset } from '$app/paths';
  import type { Locale } from './content.js';
  import { MASCOT_FRAMES } from './mascotFrames.js';
  import { advanceMascotFrame, type MascotPlayback } from './mascotPlayback.js';

  let { locale, celebration = 0, motionEnabled = true }: { locale: Locale; celebration?: number; motionEnabled?: boolean } = $props();
  let canvas = $state<HTMLCanvasElement>();
  let element = $state<HTMLDivElement>();
  let position = $state(1);
  let dragging = $state(false);
  let animationReady = $state(false);
  let pointerId: number | undefined;
  let dragOrigin = 0;
  let dragPosition = 1;

  const words = {
    en: {
      name: 'Dragon maid position',
      drag: 'Drag left or right, or use the left and right arrow keys.',
      thanks: 'Thank you for downloading KotobaSenpai!'
    },
    zh: {
      name: '龙女仆的位置',
      drag: '可以左右拖动，也可以用左右方向键移动。',
      thanks: '谢谢你下载 KotobaSenpai！'
    },
    ja: {
      name: 'ドラゴンメイドの位置',
      drag: '左右にドラッグ、または左右の矢印キーで移動できます。',
      thanks: 'KotobaSenpai をダウンロードしてくれてありがとう！'
    }
  };
  const text = $derived(words[locale]);
  const player = { celebrate: () => {}, syncMotion: (_enabled: boolean) => {} };

  $effect(() => {
    if (celebration > 0) player.celebrate();
  });

  $effect(() => { player.syncMotion(motionEnabled); });

  const rememberPosition = () => {
    try {
      localStorage.setItem('kotobasenpai-mascot-position', String(position));
    } catch {
      // Keep dragging available when persistent storage is unavailable.
      console.info('Mascot position will only apply to this page.');
    }
  };

  const startDrag = (event: PointerEvent) => {
    if (!event.isPrimary || event.button !== 0) return;
    pointerId = event.pointerId;
    dragOrigin = event.clientX;
    dragPosition = position;
    dragging = true;
    element?.setPointerCapture(event.pointerId);
    if (event.pointerType === 'mouse') event.preventDefault();
  };

  const moveDrag = (event: PointerEvent) => {
    if (!dragging || event.pointerId !== pointerId || !element) return;
    const range = Math.max(1, (element.parentElement?.clientWidth ?? document.documentElement.clientWidth - 32) - element.offsetWidth);
    position = Math.min(1, Math.max(0, dragPosition + (event.clientX - dragOrigin) / range));
  };

  const stopDrag = (event: PointerEvent) => {
    if (event.pointerId !== pointerId) return;
    dragging = false;
    pointerId = undefined;
    if (element?.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId);
    rememberPosition();
  };

  const moveWithKeyboard = (event: KeyboardEvent) => {
    if (!element) return;
    const step = 24 / Math.max(1, (element.parentElement?.clientWidth ?? document.documentElement.clientWidth - 32) - element.offsetWidth);
    if (event.key === 'ArrowLeft') position = Math.max(0, position - step);
    else if (event.key === 'ArrowRight') position = Math.min(1, position + step);
    else if (event.key === 'Home') position = 0;
    else if (event.key === 'End') position = 1;
    else return;
    event.preventDefault();
    rememberPosition();
  };

  onMount(() => {
    try {
      const saved = localStorage.getItem('kotobasenpai-mascot-position');
      if (saved !== null && Number.isFinite(Number(saved))) position = Math.min(1, Math.max(0, Number(saved)));
    } catch {
      position = 1;
    }

    const drawing = canvas;
    const context = drawing?.getContext('2d');
    if (!drawing || !context) return;

    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const images = { mood: new Image(), jump: new Image() };
    let playback: MascotPlayback = { clip: 'mood', cursor: MASCOT_FRAMES.mood - 1, loopDirection: 1 };
    let direction = 1;
    let anchorY: number | undefined;
    let frameRequest = 0;
    let lastTime = 0;
    let disposed = false;
    let assetsReady = false;
    let pendingCelebration = celebration > 0;
    let lastDrawn = '';

    const paint = () => {
      const { clip, cursor } = playback;
      const index = Math.min(MASCOT_FRAMES[clip] - 1, Math.max(0, Math.floor(cursor)));
      const key = `${clip}:${index}`;
      if (!assetsReady || lastDrawn === key) return;
      const { width, height, columns } = MASCOT_FRAMES;
      context.clearRect(0, 0, width, height);
      context.drawImage(images[clip], (index % columns) * width, Math.floor(index / columns) * height,
        width, height, 0, 0, width, height);
      lastDrawn = key;
    };

    const canAdvance = () => playback.clip === 'jump' || direction > 0 || playback.cursor > 0;

    const tick = (time: number) => {
      frameRequest = 0;
      if (disposed || !assetsReady || document.hidden || motion.matches || !motionEnabled) return;
      // Preserve the original playback speed without skipping clips after a hidden tab resumes.
      const elapsed = lastTime ? Math.min((time - lastTime) / 1000, 0.1) : 0;
      lastTime = time;
      const step = elapsed * MASCOT_FRAMES.fps;
      playback = advanceMascotFrame(playback, direction, step);
      paint();
      if (canAdvance()) frameRequest = requestAnimationFrame(tick);
      else lastTime = 0;
    };

    const wake = () => {
      if (disposed || frameRequest || !assetsReady || document.hidden || motion.matches || !motionEnabled || !canAdvance()) return;
      lastTime = 0;
      frameRequest = requestAnimationFrame(tick);
    };

    const follow = (nextDirection: number) => {
      direction = nextDirection;
      if (motion.matches || !motionEnabled) {
        if (playback.clip === 'mood') playback.cursor = direction < 0 ? 0 : MASCOT_FRAMES.mood - 1;
        paint();
      } else wake();
    };

    const trackMouse = (event: PointerEvent) => {
      if (event.pointerType !== 'mouse' || dragging) return;
      const topZone = Math.min(200, window.innerHeight / 4);
      if (event.clientY > topZone) {
        anchorY = event.clientY;
        follow(1);
      } else if (event.clientY <= 8 || anchorY === undefined) {
        anchorY = event.clientY;
        follow(-1);
      } else if (Math.abs(event.clientY - anchorY) >= 3) {
        follow(event.clientY < anchorY ? -1 : 1);
        anchorY = event.clientY;
      }
    };

    const leavePage = (event: PointerEvent) => {
      if (event.pointerType === 'mouse' && event.clientY <= 0 && !dragging) {
        anchorY = undefined;
        follow(-1);
      }
    };

    player.celebrate = () => {
      pendingCelebration = true;
      if (disposed || !assetsReady || document.hidden) return;
      pendingCelebration = false;
      playback = { clip: 'jump', cursor: 0, loopDirection: 1 };
      paint();
      // Use a happy still pose instead of jumping when reduced motion is requested.
      if (!motion.matches && motionEnabled) wake();
    };

    const visibilityChanged = () => {
      cancelAnimationFrame(frameRequest);
      frameRequest = 0;
      lastTime = 0;
      if (!document.hidden) {
        if (pendingCelebration) player.celebrate();
        else wake();
      }
    };

    const motionChanged = () => {
      cancelAnimationFrame(frameRequest);
      frameRequest = 0;
      lastTime = 0;
      playback.cursor = playback.clip === 'jump' ? 0 : direction < 0 ? 0 : MASCOT_FRAMES.mood - 1;
      playback.loopDirection = 1;
      paint();
      wake();
    };

    player.syncMotion = motionChanged;

    images.mood.src = asset('mascot/mood.webp');
    images.jump.src = asset('mascot/jump.webp');
    Promise.all([images.mood.decode(), images.jump.decode()]).then(() => {
      if (disposed) return;
      assetsReady = true;
      paint();
      animationReady = true;
      if (pendingCelebration) player.celebrate();
      else wake();
    }).catch(error => {
      // Keep the independent still image available if the animation cannot load.
      if (!disposed) console.warn('Mascot animation could not load; keeping the still image.', error);
    });

    window.addEventListener('pointermove', trackMouse, { passive: true });
    document.documentElement.addEventListener('pointerleave', leavePage);
    document.addEventListener('visibilitychange', visibilityChanged);
    motion.addEventListener('change', motionChanged);
    return () => {
      disposed = true;
      cancelAnimationFrame(frameRequest);
      player.celebrate = () => {};
      player.syncMotion = () => {};
      window.removeEventListener('pointermove', trackMouse);
      document.documentElement.removeEventListener('pointerleave', leavePage);
      document.removeEventListener('visibilitychange', visibilityChanged);
      motion.removeEventListener('change', motionChanged);
    };
  });
</script>

<div class="mascot-dock">
<div
  class="mascot"
  class:dragging
  bind:this={element}
  role="slider"
  tabindex="0"
  aria-label={text.name}
  aria-describedby="mascot-instructions"
  aria-orientation="horizontal"
  aria-valuemin="0"
  aria-valuemax="100"
  aria-valuenow={Math.round(position * 100)}
  title={text.drag}
  style:--mascot-position={position}
  style:--mascot-ratio={MASCOT_FRAMES.width / MASCOT_FRAMES.height}
  onpointerdown={startDrag}
  onpointermove={moveDrag}
  onpointerup={stopDrag}
  onpointercancel={stopDrag}
  onlostpointercapture={stopDrag}
  onkeydown={moveWithKeyboard}
>
  <img src={asset('mascot/happy.webp')} width={MASCOT_FRAMES.width} height={MASCOT_FRAMES.height} alt="" draggable="false" hidden={animationReady} />
  <canvas bind:this={canvas} width={MASCOT_FRAMES.width} height={MASCOT_FRAMES.height} aria-hidden="true" hidden={!animationReady}></canvas>
  <span class="drag-handle" aria-hidden="true"></span>
</div>
</div>
<p class="sr-only" id="mascot-instructions">{text.drag}</p>
<p class="sr-only" role="status" aria-live="polite">{celebration > 0 ? text.thanks : ''}</p>

<style>
  .mascot-dock {
    --mascot-height: clamp(300px, 46vh, 420px);
    position: fixed;
    inset-inline: 16px;
    bottom: max(16px, env(safe-area-inset-bottom));
    height: var(--mascot-height);
    z-index: 4;
    pointer-events: none;
  }

  .mascot {
    position: absolute;
    left: calc(var(--mascot-position) * 100%);
    width: calc(var(--mascot-height) * var(--mascot-ratio));
    height: var(--mascot-height);
    transform: translateX(calc(var(--mascot-position) * -100%));
    pointer-events: auto;
    cursor: grab;
    touch-action: pan-y;
    user-select: none;
    -webkit-user-select: none;
    outline-offset: 5px;
    border-radius: 8px;
    contain: layout style;
  }

  .mascot.dragging { cursor: grabbing; }
  .mascot:focus-visible { outline: 2px solid var(--accent); }
  img, canvas { display: block; width: 100%; height: 100%; pointer-events: none; }
  [hidden] { display: none; }
  .drag-handle { position: absolute; bottom: -5px; left: 50%; width: 16px; height: 3px; border-radius: 2px; background: var(--muted); opacity: 0.25; transform: translateX(-50%); pointer-events: none; }
  .mascot:hover .drag-handle, .mascot:focus-visible .drag-handle { opacity: 0.7; }

  @media (max-width: 640px) {
    .mascot-dock { --mascot-height: clamp(230px, 38vh, 290px); }
  }
</style>
