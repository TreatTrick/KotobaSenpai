<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { asset, resolve } from '$app/paths';
  import { gsap } from 'gsap';
  import { ScrollTrigger } from 'gsap/ScrollTrigger';
  import DemoPlayer from './DemoPlayer.svelte';
  import Mascot from './Mascot.svelte';
  import { CONTENT, DOWNLOAD_URL, INTRODUCTION_VIDEO_URL, LANGUAGES, RELEASE_URL, REPOSITORY_URL, SITE_URL, type DemoName, type Locale } from './content';

  let { locale }: { locale: Locale } = $props();
  const text = $derived(CONTENT[locale]);
  const suffix: Locale = $derived(locale === 'zh' ? 'zh' : 'en');
  let page = $state<HTMLDivElement>();
  let celebration = $state(0);
  let activeChapter = $state(0);
  let activeLanguage = $state(untrack(() => locale === 'zh' ? 1 : 0));
  let activeStep = $state(0);
  let motionEnabled = $state(false);
  let systemReducedMotion = $state(false);
  const language = $derived(LANGUAGES[activeLanguage]);
  const chapter = $derived(text.chapters[activeChapter]);
  const chapterMedia = $derived<DemoName>(chapter.media === 'furigana' ? 'furigana' : `${chapter.media}-${suffix}`);
  const currentUrl = $derived(`${SITE_URL}/${locale === 'zh' ? 'zh/' : ''}`);
  const celebrate = () => { celebration += 1; };
  const changeLanguage = (offset: number) => { activeLanguage = (activeLanguage + offset + LANGUAGES.length) % LANGUAGES.length; };

  const navigateTab = (event: KeyboardEvent, index: number, count: number, select: (index: number) => void) => {
    let next = index;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % count;
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index - 1 + count) % count;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = count - 1;
    else return;
    event.preventDefault();
    select(next);
    if (event.currentTarget instanceof HTMLElement) {
      event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
    }
  };

  onMount(() => {
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updateMotion = () => {
      systemReducedMotion = motion.matches;
      motionEnabled = !motion.matches;
    };
    updateMotion();
    motion.addEventListener('change', updateMotion);
    return () => { motion.removeEventListener('change', updateMotion); };
  });

  $effect(() => {
    if (typeof document !== 'undefined') {
      document.documentElement.dataset.motion = motionEnabled ? 'on' : 'off';
      document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en';
    }
  });

  $effect(() => {
    if (!page || !motionEnabled || systemReducedMotion) return;
    gsap.registerPlugin(ScrollTrigger);
    const context = gsap.context(() => {
      gsap.from('.hero-line', { y: 35, opacity: 0, duration: 1.1, stagger: 0.12, ease: 'power4.out' });
      gsap.from('.hero-intro, .hero-actions, .hero-note', { y: 18, opacity: 0, duration: 0.9, stagger: 0.1, delay: 0.3, ease: 'power3.out' });
      gsap.fromTo('.hero-screen', { scale: 0.94, rotateX: 5, y: 25 }, {
        scale: 1, rotateX: 0, y: 0, ease: 'none',
        scrollTrigger: { trigger: '.hero-stage', start: 'top 85%', end: 'center 45%', scrub: 1 }
      });
      gsap.fromTo('.experience-heading span', { color: '#777580' }, {
        color: '#f5f5f7', stagger: 0.3, ease: 'none',
        scrollTrigger: { trigger: '.experience-heading', start: 'top 85%', end: 'bottom 40%', scrub: 0.8 }
      });
      gsap.from('.language-screen', { y: 65, scale: 0.97, ease: 'none',
        scrollTrigger: { trigger: '.language-screen', start: 'top 95%', end: 'top 30%', scrub: 1 } });
      gsap.from('.setup-copy', { y: 40, opacity: 0.3, duration: 1, ease: 'power3.out',
        scrollTrigger: { trigger: '.provider-section', start: 'top 75%', once: true } });
      gsap.from('.download-icon', { y: 45, rotate: -8, ease: 'none',
        scrollTrigger: { trigger: '.download-section', start: 'top 95%', end: 'top 30%', scrub: 1 } });
    }, page);
    return () => { context.revert(); };
  });
</script>

<svelte:head>
  <title>{text.title}</title>
  <meta name="description" content={text.description} />
  <meta property="og:type" content="website" />
  <meta property="og:site_name" content="KotobaSenpai" />
  <meta property="og:title" content={text.title} />
  <meta property="og:description" content={text.description} />
  <meta property="og:url" content={currentUrl} />
  <meta property="og:image" content={`${SITE_URL}/media/hero-${suffix}.webp`} />
  <meta property="og:locale" content={locale === 'zh' ? 'zh_CN' : 'en_US'} />
  <meta name="twitter:card" content="summary_large_image" />
  <meta name="twitter:title" content={text.title} />
  <meta name="twitter:description" content={text.description} />
  <meta name="twitter:image" content={`${SITE_URL}/media/hero-${suffix}.webp`} />
  <link rel="canonical" href={currentUrl} />
  <link rel="alternate" hreflang="en" href={`${SITE_URL}/`} />
  <link rel="alternate" hreflang="zh-CN" href={`${SITE_URL}/zh/`} />
  <link rel="alternate" hreflang="x-default" href={`${SITE_URL}/`} />
  <link rel="icon" type="image/webp" href={asset('media/icon.webp')} />
</svelte:head>

{#snippet arrowIcon()}
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6" /></svg>
{/snippet}
{#snippet downloadIcon()}
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" aria-hidden="true"><path d="M12 3v12m-5-5 5 5 5-5M4 16v4h16v-4" /></svg>
{/snippet}
{#snippet githubIcon()}
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 .9a11.1 11.1 0 0 0-3.51 21.63c.55.1.76-.24.76-.54v-2.08c-3.1.67-3.75-1.32-3.75-1.32-.51-1.29-1.24-1.63-1.24-1.63-1.01-.69.08-.68.08-.68 1.12.08 1.7 1.15 1.7 1.15.99 1.69 2.59 1.2 3.22.92.1-.72.39-1.2.7-1.48-2.47-.28-5.06-1.23-5.06-5.49 0-1.21.43-2.2 1.14-2.98-.11-.28-.5-1.41.11-2.94 0 0 .93-.3 3.05 1.14a10.6 10.6 0 0 1 5.55 0c2.12-1.44 3.05-1.14 3.05-1.14.61 1.53.22 2.66.11 2.94.71.78 1.14 1.77 1.14 2.98 0 4.27-2.6 5.21-5.08 5.49.4.35.76 1.02.76 2.06v3.06c0 .3.2.65.77.54A11.1 11.1 0 0 0 12 .9Z" /></svg>
{/snippet}

<div class="page" class:reduced-motion={!motionEnabled} lang={locale === 'zh' ? 'zh-CN' : 'en'} bind:this={page}>
  <a class="skip-link" href="#main">{text.skip}</a>
  <header class="site-header">
    <nav class="navigation" aria-label={locale === 'zh' ? '主导航' : 'Main navigation'}>
      <a class="wordmark" href={locale === 'zh' ? resolve('/zh') : resolve('/')} aria-label="KotobaSenpai" data-sveltekit-reload>
        <img src={asset('media/icon.webp')} width="32" height="32" alt="" />
        <span>KotobaSenpai</span>
      </a>
      <div class="nav-links">
        <a href="#experience">{text.nav[0]}</a>
        <a href="#languages">{text.nav[1]}</a>
        <a href="#start">{text.nav[2]}</a>
      </div>
      <div class="nav-actions">
        <a class="locale-link" href={locale === 'zh' ? resolve('/') : resolve('/zh')} lang={locale === 'zh' ? 'en' : 'zh-CN'} data-sveltekit-reload>{locale === 'zh' ? 'EN' : '中文'}</a>
        <a class="button button-small" href={DOWNLOAD_URL} onclick={celebrate}>{text.navDownload}{@render downloadIcon()}</a>
      </div>
    </nav>
  </header>

  <main id="main">
    <section class="hero section-width" aria-labelledby="hero-title">
      <a class="release-link" href={RELEASE_URL}><span class="release-dot"></span>{text.release}{@render arrowIcon()}</a>
      <h1 id="hero-title"><span class="hero-line">{text.hero[0]}</span><span class="hero-line accent">{text.hero[1]}</span></h1>
      <p class="hero-intro">{text.intro}</p>
      <div class="hero-actions">
        <a class="button" href={DOWNLOAD_URL} onclick={celebrate}>{text.download}{@render downloadIcon()}</a>
        <a class="text-link" href={locale === 'zh' ? INTRODUCTION_VIDEO_URL : '#experience'} target={locale === 'zh' ? '_blank' : undefined} rel={locale === 'zh' ? 'noopener noreferrer' : undefined}>{text.watch}<span class="circle-play" aria-hidden="true">▶</span></a>
      </div>
      <p class="hero-note">{text.heroNote}</p>
      <div class="hero-stage">
        <div class="hero-screen screen-frame">
          <div class="screen-toolbar"><span class="window-controls" aria-hidden="true"><i></i><i></i><i></i></span><span>KotobaSenpai <span class="toolbar-separator">/</span> {locale === 'zh' ? '阅读进行时' : 'Reading, uninterrupted'}</span><span class="status-indicator" aria-hidden="true"></span></div>
          <DemoPlayer name={`hero-${suffix}`} label={text.demoTitle} pauseLabel={text.pause} playLabel={text.play} {motionEnabled} eager />
        </div>
      </div>
      <p class="media-caption">{text.demoCaption}</p>
    </section>

    <section class="experience-section" id="experience" aria-labelledby="experience-title">
      <div class="section-width">
        <div class="experience-heading"><h2 id="experience-title"><span>{text.experienceTitle[0]}</span><span>{text.experienceTitle[1]}</span></h2><p>{text.experienceCopy}</p></div>
        <div class="chapter-tabs" role="tablist" aria-label={text.nav[0]}>
          {#each text.tabs as tab, index}
            <button role="tab" id={`chapter-tab-${index}`} tabindex={activeChapter === index ? 0 : -1} aria-selected={activeChapter === index} aria-controls="chapter-panel" class:active={activeChapter === index} onclick={() => { activeChapter = index; }} onkeydown={(event) => navigateTab(event, index, text.tabs.length, (value) => { activeChapter = value; })}>{tab}</button>
          {/each}
        </div>
        <div class="chapter-panel" role="tabpanel" id="chapter-panel" aria-labelledby={`chapter-tab-${activeChapter}`} tabindex="0">
          <div class="chapter-copy"><span class="feature-detail">{chapter.detail}</span><h3>{chapter.title}</h3><p>{chapter.body}</p><span class="chapter-counter" aria-hidden="true">0{activeChapter + 1} <span>/ 03</span></span></div>
          <div class="chapter-screen screen-frame">{#key chapterMedia}<DemoPlayer name={chapterMedia} label={chapter.title} pauseLabel={text.pause} playLabel={text.play} {motionEnabled} />{/key}</div>
        </div>
      </div>
    </section>

    <section class="language-section section-width" id="languages" aria-labelledby="languages-title">
      <div class="section-heading"><h2 id="languages-title">{text.languagesTitle[0]}<br /><span class="accent">{text.languagesTitle[1]}</span></h2><p>{text.languagesCopy}</p></div>
      <div class="language-picker" role="tablist" aria-label={text.galleryLabel}>
        {#each LANGUAGES as item, index}
          <button role="tab" id={`language-tab-${item.id}`} tabindex={activeLanguage === index ? 0 : -1} aria-selected={activeLanguage === index} aria-controls="language-panel" class:active={activeLanguage === index} onclick={() => { activeLanguage = index; }} onkeydown={(event) => navigateTab(event, index, LANGUAGES.length, (value) => { activeLanguage = value; })}>{item.name}</button>
        {/each}
      </div>
      <div class="language-screen" id="language-panel" role="tabpanel" aria-labelledby={`language-tab-${language.id}`} tabindex="0">
        <img src={asset(`media/language-${language.id}.webp`)} alt={`${text.galleryAlt} ${language.name}`} width="1600" height="900" loading="lazy" />
        <div class="language-navigation"><button onclick={() => changeLanguage(-1)} aria-label={text.previous} class="previous">{@render arrowIcon()}</button><span>{language.name}</span><button onclick={() => changeLanguage(1)} aria-label={text.next}>{@render arrowIcon()}</button></div>
      </div>
      <p class="language-sample" lang={language.id === 'cn' ? 'zh-CN' : language.id}>{language.sample}</p>
      <p class="section-note">{text.languagesNote}</p>
    </section>

    <section class="provider-section" aria-labelledby="provider-title">
      <div class="section-width provider-layout">
        <div class="setup-copy"><h2 id="provider-title">{text.setupTitle[0]}<br /><span class="accent">{text.setupTitle[1]}</span></h2><p>{text.setupCopy}</p><div class="protocol-list">{#each text.setupTags as tag}<span>{tag}</span>{/each}</div><p class="small-copy">{text.setupNote}</p></div>
        <div class="provider-demo screen-frame"><DemoPlayer name={`setup-${suffix}`} label={text.setupTitle.join(' ')} pauseLabel={text.pause} playLabel={text.play} {motionEnabled} /></div>
      </div>
      <div class="cost-story section-width"><div class="cost-mark" aria-hidden="true">{locale === 'zh' ? '¥' : '$'}</div><div><h3>{text.costTitle}</h3><p>{text.costCopy}</p><p class="small-copy">{text.costNote}</p></div></div>
    </section>

    <section class="start-section section-width" id="start" aria-labelledby="start-title">
      <div class="section-heading"><h2 id="start-title">{text.startTitle[0]}<br /><span class="muted-heading">{text.startTitle[1]}</span></h2></div>
      <div class="start-layout">
        <div class="step-list" role="tablist" aria-label={text.nav[2]} aria-orientation="vertical">
          {#each text.steps as step, index}
            <button class:active={activeStep === index} role="tab" id={`step-tab-${index}`} tabindex={activeStep === index ? 0 : -1} aria-selected={activeStep === index} aria-controls="step-panel" onclick={() => { activeStep = index; }} onkeydown={(event) => navigateTab(event, index, text.steps.length, (value) => { activeStep = value; })}><span class="step-number">{index + 1}</span><span><strong>{step.title}</strong><span class="step-description">{step.body}</span></span></button>
          {/each}
        </div>
        <div class="onboarding-screen screen-frame" role="tabpanel" id="step-panel" aria-labelledby={`step-tab-${activeStep}`} tabindex="0">
          {#key activeStep}<DemoPlayer name={`${activeStep === 1 ? 'setup' : 'onboarding'}-${suffix}`} label={text.steps[activeStep].title} pauseLabel={text.pause} playLabel={text.play} {motionEnabled} />{/key}
        </div>
      </div>
    </section>

    <section class="faq-section section-width" aria-labelledby="faq-title"><h2 id="faq-title">{text.faqTitle}</h2><div class="faq-list">{#each text.faqs as faq}<details><summary>{faq.q}<span aria-hidden="true">+</span></summary><p>{faq.a}</p></details>{/each}</div></section>

    <section class="download-section" id="download" aria-labelledby="download-title">
      <div class="section-width"><img class="download-icon" src={asset('media/social-icon.webp')} alt="" width="104" height="104" loading="lazy" /><h2 id="download-title">{text.finalTitle[0]}<br /><span class="accent">{text.finalTitle[1]}</span></h2><p class="download-copy">{text.finalCopy}</p><div class="download-actions"><a class="button" href={DOWNLOAD_URL} onclick={celebrate}>{text.download}{@render downloadIcon()}</a><a class="text-link" href={REPOSITORY_URL}>{@render githubIcon()}{text.source}</a></div><p class="section-note">{text.finalNote}</p><a class="release-notes-link" href={RELEASE_URL}>{text.releaseNotes}{@render arrowIcon()}</a><p class="mascot-hint">{text.mascotHint}</p></div>
    </section>
  </main>

  <footer class="site-footer section-width"><div class="footer-top"><a class="wordmark" href={locale === 'zh' ? resolve('/zh') : resolve('/')}><img src={asset('media/icon.webp')} width="28" height="28" alt="" /><span>KotobaSenpai</span></a><span>{text.footerTag}</span><div class="footer-links"><a href={REPOSITORY_URL}>GitHub</a><a href={`${REPOSITORY_URL}/issues`}>{text.issues}</a><a href={`${REPOSITORY_URL}/blob/main/${locale === 'zh' ? 'README.zh-CN.md' : 'README.md'}`}>{text.docs}</a></div></div><div class="footer-bottom"><p>{text.footerNote}</p><button class="motion-toggle" aria-pressed={motionEnabled} onclick={() => { motionEnabled = !motionEnabled; }}>{text.motion}<span class:enabled={motionEnabled}></span>{motionEnabled ? text.motionOn : text.motionOff}</button></div></footer>
  <Mascot {locale} {celebration} {motionEnabled} />
</div>
