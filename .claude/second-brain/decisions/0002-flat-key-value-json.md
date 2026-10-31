---
tags: [decision, format]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0002: Flat key-value JSON, with review and context beside it

**Context.** The owner asked for "a format that is commonly chosen by other translation libraries",
loaded from files shipped with the app, readable from C#, Java and JavaScript.

**Decision.** Shipped files are flat JSON objects, id to text, one per language: the key-value JSON
that i18next, Crowdin, Lokalise, Weblate and Phrase all import and export. Review states live in
`review/<language>.json` and translator context in `context/`, so the shipped files stay exactly
what the app needs. Writers produce one canonical form (sorted, one entry per line), so diffs only
show real changes. The spec is `docs/format.md`.

**Alternatives.**
- *XLIFF 2.0*: carries source, target and state in one file and is the industry interchange format,
  but needs an XML parser in every client (Node has none built in), and is verbose to diff and ship.
- *gettext PO*: keyed by source text (see [[0001-uuidv7-keys-sorted-by-time]]), and no JSON-native client.
- *ARB*: JSON with `@key` metadata, but Flutter-specific conventions and metadata mixed into what ships.
- *State inside the shipped file* (`{"text": ..., "state": ...}`): the apps would ship review data
  and need a nested reader.

**Consequences.** Plural forms aren't modelled; a text is one string with placeholders. Adding
plurals later means a format version 2 or ICU MessageFormat inside the strings.

Related: [[0005-review-state-by-source-hash]]
