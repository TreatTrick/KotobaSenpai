<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { asset } from '$app/paths';
  import type { DemoName } from './content';

  let { name, label, pauseLabel, playLabel, motionEnabled = true, eager = false }: {
    name: DemoName;
    label: string;
    pauseLabel: string;
    playLabel: string;
    motionEnabled?: boolean;
    eager?: boolean;
  } = $props();

  let element = $state<HTMLDivElement>();
  let video = $state<HTMLVideoElement>();
  let visible = $state(false);
  let loaded = $state(false);
  let userPaused = $state(false);
  let playing = $state(false);
  let documentVisible = $state(true);

  onMount(() => {
    if (!element) return;
    loaded = eager;
    const observer = new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      if (visible) loaded = true;
    }, { threshold: 0.12 });
    observer.observe(element);
    const visibilityChanged = () => { documentVisible = !document.hidden; };
    visibilityChanged();
    document.addEventListener('visibilitychange', visibilityChanged);
    return () => {
      observer.disconnect();
      document.removeEventListener('visibilitychange', visibilityChanged);
    };
  });

  $effect(() => {
    if (!video || !loaded) return;
    if (visible && documentVisible && motionEnabled && !userPaused) {
      void video.play().catch((error: unknown) => {
        if (error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'AbortError')) return;
        console.warn('The demonstration could not start.', error);
      });
    } else video.pause();
  });

  const togglePlayback = async () => {
    if (!video) return;
    loaded = true;
    if (playing) {
      userPaused = true;
      video.pause();
    } else {
      userPaused = false;
      await tick();
      void video.play().catch((error: unknown) => console.warn('The demonstration could not start.', error));
    }
  };
</script>

<div class="demo-player" bind:this={element}>
  <video
    bind:this={video}
    src={loaded ? asset(`media/${name}.mp4`) : undefined}
    poster={asset(`media/${name}.webp`)}
    width="1600" height="900"
    aria-label={label}
    muted loop playsinline preload="none"
    onplay={() => { playing = true; }}
    onpause={() => { playing = false; }}
  ></video>
  <button class="playback-control" onclick={togglePlayback} aria-label={playing ? pauseLabel : playLabel} title={playing ? pauseLabel : playLabel}>
    {#if playing}
      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M7 5h3v14H7zm7 0h3v14h-3z" /></svg>
    {:else}
      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="m8 5 11 7-11 7z" /></svg>
    {/if}
  </button>
</div>
