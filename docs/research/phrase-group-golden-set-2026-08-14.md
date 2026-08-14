# Phrase Group Golden Set (MVP)

Date: 2026-08-14

A small representative set of visual-novel sentences to measure whether LLM-first phrase detection is
useful before adding local rules. Each row is one sentence segment with the expected detectable
combination groups. Not yet machine-scored; used to evaluate precision/recall qualitatively.

## Non-continuous grammar

| Sentence | Expected group | Notes |
|----------|----------------|-------|
| 君がいなければ、何も始まらない。 | 〜なければ〜ない | parts separated by `何も` |
| 彼に会うことには、会うんだが。 | 〜ことには〜 | non-continuous |

## Inflection

| Sentence | Expected group | Notes |
|----------|----------------|-------|
| 食べられなくなった。 | 〜られない | inflection + negative |
| 行きたくなくて、困ってる。 | 〜たくない | volitional negative stem |

## Cross-line word (accepted line break)

| Sentence (two OCR lines) | Expected group | Notes |
|--------------------------|----------------|-------|
| お世話 / になりました。 | お世話になる | word split across lines |

## Overlap

| Sentence | Expected group | Notes |
|----------|----------------|-------|
| てめえ、ふざけんなよ。 | 〜ふざける + 〜んじゃない | overlapping groups |

## No-group

| Sentence | Expected group | Notes |
|----------|----------------|-------|
| ありがとう。 | (none) | greeting, no meaningful combination |

## Known MVP evaluation gaps

- No labeled corpus yet → precision/recall not measured; golden set is manual.
- Cross-line joins depend on OCR layout heuristics; false joins can mis-group.
- Ties in overlap hover are resolved but not yet user-tunable.
- The overlay awaits the full phrase run before showing underlines; groups are not yet loaded
  asynchronously after local words.