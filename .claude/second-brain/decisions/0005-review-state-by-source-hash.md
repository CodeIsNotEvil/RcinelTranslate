---
tags: [decision, format]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0005: Review state records a hash of the source text

**Context.** The owner wants to see how far each language is, and a confirmed translation is only
good for the English it was made for.

**Decision.** `review/<language>.json` stores, per id, the state (`draft`/`confirmed`), who, when, and
the first 16 hex digits of the SHA-256 of the source text. If the source text changes, the hash no
longer matches and the translation counts as outdated, with no extra bookkeeping. A translation without
a review line (typed into the file) counts as a draft.

**Alternatives.** *Store the source text*: doubles the English in every language and turns review
diffs into English diffs. *Timestamps*: break with rebases and clock skew.

**Consequences.** Progress (confirmed / draft / outdated / missing) is computed, never stored, so it
can't go out of date. Whitespace-only changes in English make translations outdated too, which is
what a reviewer would want.

Related: [[0002-flat-key-value-json]]
