---
tags: [decision, ai]
created: 2026-10-31
updated: 2026-10-31
status: active
supersedes:
---
# 0007: AI drafts, people confirm

**Context.** The owner wants an AI agent to create all translations to start with, and logged-in
users to confirm or correct them.

**Decision.** A fixed workflow, not an open-ended agent: `TranslationDrafter` batches the missing and
outdated strings (those on a screenshot travel with that image), and sends each batch to Claude with
structured output (a JSON schema), the project's and the language's instructions, notes, box sizes,
and up to 40 confirmed translations for consistent terms. Results are stored as `draft` by
`ai:<model>`. They are applied only if nobody changed the string during the run, and drafts that
lost a placeholder are dropped and reported. The default model is `claude-opus-5-5` at effort
`medium`, set in rcinel.json. Requests opt in to server-side refusal fallbacks.

**Alternatives.** *An agent with tools over the project*: more cost and less predictability for a
task that is a batch transformation. *Auto-confirming high-confidence drafts*: contradicts the
review the owner asked for.

**Consequences.** Needs `ANTHROPIC_API_KEY` for the editor or `rcinel draft`. The whole project
context goes to the API; projects with secret texts must not enable it.

Related: [[0006-editor-works-on-the-files]]
