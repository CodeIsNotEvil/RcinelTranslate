# Second Brain

A plain-Markdown knowledge base for RcinelTranslate, shared by the owner and Claude Code, in the same
layout as the one in MyMonoRepo. It holds what the code and git history *can't* tell you: why
decisions were made, what's in flight, open questions, and gotchas found the hard way.

It's Obsidian-compatible: open `.claude/second-brain/` as a vault and the `[[wikilinks]]` resolve.

## Layout (PARA plus decisions)

| Folder | Holds |
|---|---|
| `inbox/` | Quick, unsorted captures. Triage them later |
| `projects/` | Work with a goal and an end |
| `areas/` | Ongoing responsibilities with no end date |
| `resources/` | Reusable knowledge, gotchas |
| `decisions/` | ADR-style records: context, decision, alternatives, consequences. Never rewritten, only superseded |
| `journal/` | Dated session logs (`YYYY-MM-DD.md`) |
| `archive/` | Finished or abandoned things, kept for context |
| `templates/` | Starting points for new notes |

Start at [[index]].

## Conventions

- **One idea per note.** File names are lowercase kebab-case; decisions are numbered (`NNNN-slug.md`).
- **Frontmatter** on every note: `tags`, `created`, `updated`, `status` (`active` | `done` | `superseded` | `idea`).
- **Link liberally** with `[[note-name]]`.
- **Point, don't copy.** Link to repo files instead of pasting code; don't duplicate `CLAUDE.md`, the
  README or `docs/format.md`. Notes cover the *why* and the *what next*.
- **Absolute dates only** (`2026-10-31`).
- **Decisions are append-only.** To change one, write a new record and mark the old one `superseded`.
