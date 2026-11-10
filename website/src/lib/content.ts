export type Locale = 'en' | 'zh';
export type DemoName = 'furigana' | `${'hero' | 'analysis' | 'setup' | 'onboarding'}-${'en' | 'zh'}`;

export const REPOSITORY_URL = 'https://github.com/TreatTrick/KotobaSenpai';
export const RELEASE_URL = `${REPOSITORY_URL}/releases/latest`;
export const DOWNLOAD_URL = `${RELEASE_URL}/download/KotobaSenpai-win-x64-Setup.exe`;
export const SITE_URL = 'https://treattrick.github.io/KotobaSenpai';

export const LANGUAGES = [
  { id: 'en', name: 'English', sample: 'Stay in the story.' },
  { id: 'cn', name: '简体中文', sample: '把下一幕，读进心里。' },
  { id: 'ko', name: '한국어', sample: '이야기 속에 머물러 보세요.' },
  { id: 'es', name: 'Español', sample: 'Quédate en la historia.' },
  { id: 'pt', name: 'Português', sample: 'Continue na história.' },
  { id: 'ru', name: 'Русский', sample: 'Оставайтесь в истории.' }
] as const;

export const CONTENT = {
  en: {
    title: 'KotobaSenpai — Japanese Visual Novel AI Reading Assistant',
    description: 'Read Japanese visual novels on Windows with on-screen OCR, furigana, pitch accents, contextual AI explanations, and sentence translation. Download KotobaSenpai.',
    skip: 'Skip to content',
    nav: ['The experience', 'Your language', 'Getting started'],
    download: 'Download for Windows',
    navDownload: 'Download',
    source: 'Explore on GitHub',
    release: 'Get the latest release',
    hero: ['Stay in the story.', 'Read it in Japanese.'],
    intro: 'The scene. The words. That one unforgettable line. Understand more of it, without looking away.',
    heroNote: 'Windows 10 / 11 · x64 · Your own AI provider',
    watch: 'See it in action',
    demoTitle: 'Your visual novel. A little more understood.',
    demoCaption: 'Real footage from KotobaSenpai · Witch on the Holy Night',
    pause: 'Pause demonstration',
    play: 'Play demonstration',
    experienceTitle: ['Less looking things up.', 'More being there.'],
    experienceCopy: 'A reading companion that meets you where the story is. Readings above the kanji. Meaning beside the line. Your attention stays with the scene.',
    tabs: ['Read naturally', 'Understand the context', 'See the whole sentence'],
    chapters: [
      { title: 'The reading, right above the word.', body: 'Furigana and available pitch-accent patterns sit directly on the game text. Keep reading without a trip to another window.', detail: 'Furigana + pitch accents', media: 'furigana' },
      { title: 'The meaning that belongs in this moment.', body: 'AI uses the surrounding sentence to explain words and grammar together. Even expressions split across the line get their full meaning.', detail: 'Contextual word + grammar explanations', media: 'analysis' },
      { title: 'A little help with the bigger picture.', body: 'Bring up a full-sentence translation when you want one. Read the original first, then check the part that has you wondering.', detail: 'On-demand sentence translation', media: 'hero' }
    ],
    languagesTitle: ['Japanese on screen.', 'Your language beside it.'],
    languagesCopy: 'Choose the language that makes a new idea click. Your explanations and translations can use any left-to-right language supported by your AI model.',
    languagesNote: 'Six examples. Not a six-language limit. Availability and quality depend on your provider and model.',
    galleryLabel: 'Example explanation languages',
    previous: 'Previous language',
    next: 'Next language',
    galleryAlt: 'KotobaSenpai explaining Japanese visual-novel text in',
    setupTitle: ['Your model.', 'Your choice.'],
    setupCopy: 'Bring the AI provider you prefer. Configure it yourself, or let your local Agent discover models, check the connection, and apply your settings.',
    setupTags: ['OpenAI Responses', 'Anthropic Messages', 'Chat Completions'],
    setupNote: 'Works with local coding Agents such as Codex, Claude Code, and Cursor. You choose your provider and supply your own credentials.',
    costTitle: 'A whole chapter. A small AI bill.',
    costCopy: 'One real reading session: about US$0.075 for 30 minutes of Witch on the Holy Night with DeepSeek V4.1 Flash.',
    costNote: 'An actual-use example, not a fixed rate. Your cost depends on the provider, model, and token usage.',
    startTitle: ['A few steps.', 'Then, the next chapter.'],
    steps: [
      { title: 'Prepare your dictionary.', body: 'The first-run guide helps you download and prepare the Japanese dictionary.' },
      { title: 'Connect your AI.', body: 'Add your preferred provider, or copy the setup prompt into your local Agent.' },
      { title: 'Choose a window. Start reading.', body: 'Select the game window, choose your reading area, and let recognition follow the text.' }
    ],
    faqTitle: 'A few things before you begin.',
    faqs: [
      { q: 'What do I need to run it?', a: 'A Windows 10 version 2004 or later (build 19041+) or Windows 11 PC with x64 support. The installer includes the .NET 10 runtime and OCR models. Dictionaries are prepared during onboarding.' },
      { q: 'Do I need an AI subscription?', a: 'AI explanations and translations use the provider and API key you configure. A ChatGPT or Claude chat subscription does not automatically include API usage. Costs are determined by your chosen provider.' },
      { q: 'Can I use my own explanation language?', a: 'Yes. Choose a left-to-right language supported by your model. This is independent of the app interface, which is available in English and Simplified Chinese.' },
      { q: 'Where can I see the source or report an issue?', a: 'The project, release notes, and issue tracker are on GitHub. The links below take you straight there.' }
    ],
    finalTitle: ['The next line', 'is waiting for you.'],
    finalCopy: 'Keep the original words. Find a little more meaning.',
    finalNote: 'Latest release · Windows x64 · .NET runtime included',
    releaseNotes: 'Release notes & checksums',
    footerTag: 'Your Japanese reading companion.',
    footerNote: 'Game footage illustrates the reading experience. Game content belongs to its respective owners.',
    issues: 'Report an issue',
    docs: 'Read the guide',
    mascotHint: 'A little company for your next chapter.',
    motion: 'Motion',
    motionOn: 'On',
    motionOff: 'Off'
  },
  zh: {
    title: 'KotobaSenpai — 日语视觉小说 AI 阅读助手｜振假名与翻译',
    description: '用 KotobaSenpai 辅读日语视觉小说与 Galgame。Windows 屏幕 OCR 识别、振假名、日语音调、AI 语境释义与整句翻译，让理解跟上剧情。下载 Windows 版，自由选择 AI 服务商。',
    skip: '跳至主要内容',
    nav: ['阅读体验', '你的语言', '轻松上手'],
    download: '下载 Windows 版',
    navDownload: '立即下载',
    source: '在 GitHub 探索',
    release: '获取最新版本',
    hero: ['少按暂停。', '多看懂剧情。'],
    intro: '这一幕，这句话，这份只属于原文的心动。\n让理解跟上故事，让目光留在这里。',
    heroNote: 'Windows 10 / 11 · x64 · 自由选择 AI 服务商',
    watch: '看看实际效果',
    demoTitle: '你的视觉小说，多了一位阅读搭档。',
    demoCaption: 'KotobaSenpai 实际演示 ·《魔法使之夜》',
    pause: '暂停演示',
    play: '播放演示',
    experienceTitle: ['少一点来回查词。', '多一点身临其境。'],
    experienceCopy: '读音就在汉字上方，释义就在台词旁边。\n不用在词典和游戏之间来回切换，继续读，就是了。',
    tabs: ['顺畅地读', '结合语境懂', '整句话看明白'],
    chapters: [
      { title: '读音，抬眼就能看见。', body: '振假名直接显示在游戏原文上方；有音调数据时，高低音也一起标出。读到不熟悉的汉字，故事不必跟着暂停。', detail: '振假名 + 日语音调', media: 'furigana' },
      { title: '给你的，是此刻的意思。', body: '结合当前句子理解词语和语法，把适合这段剧情的释义交到你眼前。即使是中间隔着其他词的短语，也能一起解释。', detail: '语境释义 + 语法组合', media: 'analysis' },
      { title: '需要时，再看懂整句话。', body: '先读原文，再按需展开整句翻译。遇到拿不准的表达，确认一下，然后回到故事里。', detail: '按需显示整句翻译', media: 'hero' }
    ],
    languagesTitle: ['原文是日语。', '理解，用你的语言。'],
    languagesCopy: '用最容易让你心领神会的语言，理解新的词句。释义和整句翻译支持所选 AI 模型能够输出的任何从左到右书写的语言。',
    languagesNote: '下面只是六个示例，不是六种语言的限制。实际可用语种与质量取决于服务商和模型。',
    galleryLabel: '释义语言示例',
    previous: '上一种语言',
    next: '下一种语言',
    galleryAlt: 'KotobaSenpai 使用以下语言解释日语视觉小说台词：',
    setupTitle: ['模型你来选。', '故事放心读。'],
    setupCopy: '接入你喜欢的 AI 服务商。可以手动配置，也可以交给本机 Agent 查询模型、验证连接，再帮你应用设置。',
    setupTags: ['OpenAI Responses', 'Anthropic Messages', 'Chat Completions'],
    setupNote: '支持与 Codex、Claude Code、Cursor 等本机编程 Agent 配合。服务商由你选择，凭据由你提供。',
    costTitle: '剧情读了不少，费用只用一点。',
    costCopy: '一次真实体验：使用 DeepSeek V4.1 Flash 游玩《魔法使之夜》，30 分钟约 ¥0.50。',
    costNote: '这是一次实际使用参考，不是固定费率。费用随服务商定价、模型及 Token 用量变化。',
    startTitle: ['几步准备。', '然后，读下一幕。'],
    steps: [
      { title: '准备好日语词典。', body: '跟着首次引导，下载并准备阅读需要的日语词典。' },
      { title: '接入你选择的 AI。', body: '填写服务商信息，或将配置提示词交给本机 Agent。' },
      { title: '选个窗口，开始读。', body: '选择游戏窗口和阅读区域，让识别跟上台词的变化。' }
    ],
    faqTitle: '开始之前，你可能想知道。',
    faqs: [
      { q: '需要什么运行环境？', a: '支持 Windows 10 2004（内部版本 19041）及以上版本，或 Windows 11，需支持 x64。安装包自带 .NET 10 运行时和 OCR 模型；日语词典会在首次引导时准备。' },
      { q: '需要订阅 AI 会员吗？', a: 'AI 释义和翻译使用你配置的服务商与 API Key。ChatGPT 或 Claude 的聊天会员通常不包含 API 用量；具体费用按你选择的服务商计费。' },
      { q: '可以选择自己的释义语言吗？', a: '可以。选择模型支持的从左到右书写的语言即可，不受固定列表限制。释义语言与软件界面语言独立设置，软件界面目前支持英文和简体中文。' },
      { q: '在哪里查看源码或反馈问题？', a: '项目源码、版本说明和问题反馈都在 GitHub。页面底部的链接可以直接前往。' }
    ],
    finalTitle: ['下一句，', '等你读懂。'],
    finalCopy: '留下原文的心动，也读懂更多故事。',
    finalNote: '最新正式版 · Windows x64 · 自带 .NET 运行时',
    releaseNotes: '版本说明与校验文件',
    footerTag: '陪你读日语的故事搭档。',
    footerNote: '游戏画面仅用于展示阅读体验，游戏内容归各自权利人所有。',
    issues: '反馈问题',
    docs: '阅读指南',
    mascotHint: '下一章，也有她陪你。',
    motion: '动效',
    motionOn: '开',
    motionOff: '关'
  }
} as const;
