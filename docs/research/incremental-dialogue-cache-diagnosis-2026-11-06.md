# Incremental dialogue cache diagnosis — 2026-11-06

## Evidence

Source: `%LOCALAPPDATA%/KotobaSenpai/logs/kotobasenpai-2026-11-06.log`.
All times below use UTC+08:00. The screenshot was taken at 21:47:34.
`DiagEnabled` was absent from the settings file and the `diag` directory did not
exist, so no historical OCR images, token dumps, or provider payloads were
available. The log retained the OCR text, request segment IDs, cache counts,
provider timings, and overlay annotation counts.

| Time | Observation |
| --- | --- |
| 21:46:49.904 | Two lines: `segments=2, cacheHits=1, providerRequests=1`. |
| 21:46:56.231 | Third line appended: still `segments=2, cacheHits=1, providerRequests=1`; request `s1-2` joined the second and third lines. |
| 21:47:02.097 | Analysis completed; overlay contained 21 underlines. |
| 21:47:14.294 | Stable OCR changed the second line's suffix from `よ」` to `よ」、`. |
| 21:47:14.952 | Preview replaced the overlay with only 7 underlines. |
| 21:47:14.974 | Fourth sentence: `segments=3, cacheHits=1, providerRequests=2`; requests `s1-2` and `s3-4`. |
| 21:47:20.616 | Both requests completed; overlay contained 32 underlines. |

The target-occlusion hide events began at 21:47:22, after the repeated requests
completed. They do not explain the earlier loss of annotations.

## Cause

The second OCR line was `時間も惜しいし、手早くいくわよ」`. It had a closing
quote without terminal punctuation. The segmenter joined it to the next line,
`「それは助かる。やっぱり時間は大切だ」`, changing the cached analysis unit when
the third sentence appeared. When the fourth sentence appeared, OCR added a
comma after the second line's closing quote. This changed the joined segment's
cache key again, resending both old lines and removing their successful-segment
flags during the pending preview.

An additional regression case exposed an unsafe shortcut in reference rebinding:
removing an opening quote while adding trailing punctuation can leave the token
IDs unchanged even though those IDs now refer to different content tokens.

## Correction and verification

- Separate adjacent dialogues at closing-quote/opening-quote boundaries while
  preserving wrapped narration such as `彼は「行く」` / `と言った。`.
- Ignore commas and periods outside a closing Japanese dialogue quote for cache
  identity; retain content punctuation and require matching lexical metadata.
- Verify token identity before reusing provider references unchanged.
- Log each analysis segment's source, normalized text, token count, and cache hit.
- Replay the recorded two-to-three-line and trailing-comma transitions through
  `WordOverlayApplicationService`, asserting retained group IDs and underlines
  while the new provider result is deliberately withheld.
- Replay all four snapshots with real UniDic, including the original opening
  quote and the game's separate `いやみ` ruby line. Exactly four provider calls
  are required across all four snapshots; previously completed annotations stay
  available during the fourth request.

The provider is replaced at its interface during replay; verification consumes
no provider tokens. Live gameplay still requires verification after rebuilding
and restarting the application.

The two isolated workflow regressions failed before the correction. After the
correction, the application built with zero warnings and errors, and 1,086 tests
passed (Core 253, Windows 379, Architecture 38, App 416). The existing unrelated
`Open_popup_follows_application_theme_changes_without_replacing_content` failure
was excluded, as in the preceding baseline verification.
