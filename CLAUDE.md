# CLAUDE.md

## Writing rule

**Invoke the `ste-writing` skill before you write or edit any prose in this repository.** Invoke it first, then write. Do not write the text first and clean it up after.

This applies to:

- Markdown files. This includes `project_brief.md`, everything under `docs/`, and any README.
- Commit messages and pull request text.
- Code comments and XML documentation comments.
- User-facing strings. This includes error messages, log messages, and UI labels.

This does not apply to:

- Code, identifiers, and command syntax.
- Chat replies to the user.

Use **strict** mode for error messages, log messages, and step-by-step procedures. Use **STE-flavored** mode for the roadmap files, the brief, and README files.

Run the skill self-lint before you return the text. The mechanical rules are what remove the slop:

1. Split any sentence over 20 words.
2. Replace every semicolon with a period.
3. Expand every contraction.
4. Make passive voice active when the actor is known.
5. Replace nominalizations and phrasal verbs with a plain verb.
6. Give one thing one name.

Two project conventions on top of the skill:

- Keep the em dash. The skill permits it, and the existing documentation uses it.
- Keep bold on decisions and warnings. The roadmap depends on it.

## Documentation map

| File                                        | Holds                                             |
| ------------------------------------------- | ------------------------------------------------- |
| `project_brief.md`                          | Format research and design decisions.             |
| `docs/roadmap/README.md`                    | The step sequence and the completed work.         |
| `docs/roadmap/01` to `15`                   | One implementation step each, with pitfalls.      |
| `docs/roadmap/98-known-upstream-defects.md` | Defects in the MIT libraries that we work around. |
| `docs/roadmap/99-api-notes.md`              | Verified library signatures and call order.       |

`99-api-notes.md` wins over `project_brief.md` where the two disagree. The brief describes three APIs incorrectly. Read the API notes before you write library code.

## Verify before you document

Do not write API detail from memory or from the brief. Read the source in `third_party/` and confirm it. Several claims in the brief did not survive that check.

## Repository rules

**Never merge `third_party/CoreExtensions` forward to master.** Its branch sits on commit `1e1e687` on purpose. Master deletes `ReadNullTermUTF8` and `WriteNullTermUTF8`, which Nikki calls. A merge breaks the build with 46 errors.

**Do not copy code from `SpeedReflect/Binary`.** That repository is GPL-3.0. Nikki, Endscript, and CoreExtensions are MIT. Treat the Binary repository as read-only documentation.

**Never point code at a live game install.** Apply to a staging copy, verify, then swap.
