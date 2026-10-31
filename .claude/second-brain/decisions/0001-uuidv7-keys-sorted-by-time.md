---
tags: [decision, format]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0001: UUIDv7 keys, files sorted by them

**Context.** The owner wants translations versioned with the code and shipped with each update
"without huge changes". Keys need to be stable, unique without coordination, and the files need to
diff well when several branches add strings.

**Decision.** Every string's key is a lowercase UUIDv7. All files are sorted ordinally by key.
Because a UUIDv7 starts with its millisecond timestamp, big-endian, that is creation order: new
strings land at the end, and a release's strings form one block. Ids made in the same millisecond
count up in the rand_a bits (`UuidV7.New`), so a batch keeps its order too.

**Alternatives.**
- *Readable keys* (`settings.save_button`): naming debates, renames break translations, and
  sorting by name scatters new strings through the file.
- *The English text as key* (gettext style): fixing a typo orphans every translation.
- *Random UUIDv4*: unique, but sorted order is random, so every addition lands somewhere in the middle.

**Consequences.** Ids are compared exactly, so uppercase ids are rejected by `rcinel check`. Ids
are never reused; a changed meaning gets a new id. Developers don't type ids: they write `""` and
`rcinel scan --assign` fills them in ([[0003-code-is-the-source-of-source-texts]]).

Related: [[0002-flat-key-value-json]]
