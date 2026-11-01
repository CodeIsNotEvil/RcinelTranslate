---
tags: [moc]
created: 2026-10-31
updated: 2026-10-31
status: active
---
# Index

## Projects
- [[rcineltranslate]]: the library, the `rtr` tool and the editor; open questions and next steps

## Resources
- [[gotchas]]: snags found while building it

## Decisions
- [[0001-uuidv7-keys-sorted-by-time]]: string ids are lowercase UUIDv7, and every file is sorted by them
- [[0002-flat-key-value-json]]: shipped files are flat key-value JSON; review and context live beside them
- [[0003-code-is-the-source-of-source-texts]]: `rtr scan` (formerly `rcinel scan`) generates the source file from the calls in the code
- [[0004-dependency-free-clients-with-shared-fixtures]]: three hand-written readers, one set of test cases
- [[0005-review-state-by-source-hash]]: a review records a hash of the source text, which makes "outdated" free
- [[0006-editor-works-on-the-files]]: Blazor Server on the git checkout, users in a JSON file, no database
- [[0007-ai-drafts-never-confirm]]: Claude drafts with structured output; only people confirm
- [[0008-cine-translate-namespace]]: namespaces are `CINE.Translate.*`, not `CINE.Rcinel.*`
