# LLM Phrase Groups — Provider Contract & Configuration

Date: 2026-08-14

## 1. Provider payload contract

The app sends one locally-segmented OCR sentence segment per request to a provider-neutral
`ILlmPhraseAnalyzer`. The request body (DeepSeek chat-completions JSON) contains only:

- **Segment text** — the concatenated OCR text of the segment's lines.
- **Token metadata** — a flat list of `id` / `surface` / `lemma` / `reading` / `pos1` / `cType` / `cForm`.
- **Local continuous span summaries** — surface, reading, and token ids of already-resolved JMdict spans.

The assistant is instructed to return **only meaningful combination groups** (non-continuous grammar,
collocations, context-dependent expressions) and must not repeat ordinary tokens or continuous spans
the local pipeline already resolved.

### Response schema

```json
[
  {
    "modelGroupId": "g1",
    "type": "grammar",
    "parts": [["l0:t0"], ["l0:t2"]],
    "label": "〜ないことには〜ない",
    "meaningZh": "如果不…就不…",
    "grammarZh": "表示必要条件…"
  }
]
```

- `parts` is an array of arrays of token ids. Each inner array is one contiguous part; parts may be
  separated by intervening tokens. A part may span an accepted OCR line boundary.
- `modelGroupId` is request-local only and not unique across requests.
- Optional fields (`confidence`, `reason`) are accepted and ignored.

## 2. Token-ID rules

Token ids are strongly typed as `SentenceTokenId` (value object, wire format `l{line}:t{token}`).
Every group part must reference existing token ids from the same request segment. Unknown ids,
non-contiguous ids inside a part, a token repeated within one group, an empty part, or a group id
repeated in the response cause that group to be **dropped individually** without invalidating others.
A malformed id string in the payload fails the response parse (`MalformedJson`).

After validation the app caps output at **8 groups per segment** (provider order preserved) and
assigns an application-owned session `SessionGroupId` used for all parts, hover, and detail display.
The app derives each part's surface, reading, and screen geometry **locally** from referenced OCR
character boxes. Model-provided offsets and surface text are never trusted for rendering.

> **更新(2026-08-16):** 同一响应现还携带一个平行的 `words[]` 数组——每条以 `headword`(从请求词块表逐字复制的 surface)引用一个本地合并词,返回语境词性、最佳中文释义与语法;不返回 token id/读音(本地已有)。每词按 headword 精确匹配本地合并词,匹配不到/重复/超长则单独丢弃,不发生级联错位;每句段上限 32 词。悬停弹窗释义源由英文 gloss 改为该词义(`llm-word-meanings`)。

## 3. Privacy boundary

The provider payload contains **no** screenshots, image bytes, window coordinates, window titles, or
API keys. The request body is capped (~16 KB); oversized requests are rejected before sending.
Diagnostics record only segment outcomes, group/token counts, and validation warnings — never
screenshots, keys, or window titles.

## 4. Fallback behavior

Local UniDic/JMdict words and continuous spans always complete before phrase analysis. Any of the
following yields a retryable warning and an otherwise usable local overlay — never a crash and never a
partially-parsed group:

- Missing/spaces API key (`NoKey`)
- Timeout (`Timeout`)
- Caller cancellation (`Cancelled`)
- Provider refusal / HTTP ≥ 500 (`Refused`)
- Transport error (`TransportError`)
- Malformed JSON (`MalformedJson`)
- Invalid response shape (`InvalidResponse`)

Segments are analyzed concurrently (bounded). A failed segment is skipped with a warning while the
others still submit; the run succeeds as long as any segment succeeds.

## 5. DeepSeek configuration

BYOK settings live in `%LocalAppData%/KotobaSenpai/settings.json`:

| Key | Default | Meaning |
|-----|---------|---------|
| `DeepSeekApiKey` | *(none)* | Provider API key. Empty ⇒ analysis skipped (`NoKey`). |
| `DeepSeekEndpoint` | `https://api.deepseek.com` | Base endpoint; request is POST `{endpoint}/chat/completions`. |
| `DeepSeekModel` | `deepseek-chat` | Model name. |
| `PhraseGroupsEnabled` | *(none / false)* | Set to `true` to enable phrase analysis. |

The adapter uses a dedicated `HttpClient` with a 30 s timeout and a `Bearer` authorization header.
No offline local grammar catalog is added; the LLM is the primary detector for MVP.