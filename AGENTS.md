# KotobaSenpai — Contribution Rules

## Language: English only for code, comments, and tests

This project is open source and welcomes global contributors. To keep everything
readable and reusable by everyone:

- **Comments** — all `///` doc comments and `//` inline comments MUST be written
  in English.
- **Tests** — test method names, assertion messages, and test data MUST be in
  English.
- **Runtime output** — exception messages, log messages, and diagnostic strings
  MUST be in English.
- **Identifiers** — class/method/property names are already English (`PascalCase`);
  keep them English.

### Exceptions (Chinese/Japanese is legitimate content, do NOT translate)

- **Japanese test data** — real language samples in tokenizer/dictionary tests
  (e.g. `日本語`, `受ける`, `動詞`) are data, not output. Keep them.
- **`.resx` localization** — user-facing UI text is intentionally localized per
  culture. Keep it; edit the `.resx` files only to change UI language.
- **UniDic field names** — official Japanese terms (e.g. `語彙素` = dictionary
  form) are kept alongside English glosses.

> Rule of thumb: if it's a *message* (comment, test name, assertion, log,
> exception) → English. If it's *data* (language sample, dictionary content,
> localized UI text) → keep as-is.

## UI: FluentLight integration is required

All newly added UI MUST integrate with FluentLight by default. Include this work
when implementing any new page, card, panel, control, window, or popup.

- **Automatic adaptation through shared styles** — reuse the existing FluentLight
  styles and controls. When introducing reusable controls or styles, include the
  appropriate FluentLight defaults so every new instance inherits them.
- **One-off surfaces** — explicitly set `controls:FluentLight.Kind="Surface"`
  on standalone cards and panels, including raw `ui:Card` instances. Follow the
  existing styles for the appropriate light kind on interactive controls.
- **Matching geometry** — set `controls:FluentLight.CornerRadius` to match the
  control's actual rendered corners and the project's design tokens.
- **Host coverage** — reuse the nearest existing `FluentLightHost`. Separate
  windows and popup visual trees MUST provide their own host when they cannot
  inherit one. Keep ordinary page content under the shared shell host.
- **Content elements** — text, icons, and illustrations participate through their
  containing FluentLight surface; configure the enclosing surface rather than
  decorating every child separately.
- **Verification** — before completing UI work, check every added surface and
  interactive control for shared-style or explicit FluentLight configuration,
  matching corners, and host coverage. Build the application and run the relevant
  existing FluentLight tests.
