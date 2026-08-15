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