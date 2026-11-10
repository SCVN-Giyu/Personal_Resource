# Q&A Dashboard · <subject>

<!--
This file is a DASHBOARD, not where entries live. Each question is its own note in questions/
(copy references/qa-entry-template.md). Replace <subject-slug> below with the subject's slug.
In Obsidian with the Dataview plugin, the blocks below render as live tables.
Without Obsidian (e.g. an agent reading files): list questions/*.md and read their frontmatter;
the Manual Summary at the bottom must then be kept in sync by the agent.
-->

answer_state (A): open ❓ · explained 💭 · sourced 📚 · verified 🔬 · contested ⚠️ · blocked 🧱 · parked ⏸
understanding (B): lost 🌫 · can-follow 👀 · can-explain 🗣 · can-apply 🎯
Only the user (or a passed quiz) sets understanding. States can go down. Answers are never deleted.

## Needs attention (start every session here)
```dataview
TABLE answer_state AS "A", understanding AS "B", concept, next_action, updated
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>"
  AND (contains(list("open", "explained", "contested", "blocked"), answer_state) OR understanding = "lost")
SORT updated ASC
```

## Blocked: needs the user (editor / device / access)
```dataview
TABLE concept, next_action, updated
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>" AND answer_state = "blocked"
SORT updated ASC
```

## Quiz material (understanding not yet solid)
```dataview
TABLE understanding AS "B", concept, hard_because
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>" AND contains(list("lost", "can-follow"), understanding)
SORT concept ASC
```

## Per concept
```dataview
TABLE length(rows) AS "Questions", rows.answer_state AS "A", rows.understanding AS "B"
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>"
GROUP BY concept
SORT concept ASC
```

## Where I get stuck (pattern of difficulty)
```dataview
TABLE length(rows) AS "Count", rows.file.link AS "Questions"
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>"
GROUP BY hard_because
SORT length(rows) DESC
```

## All questions
```dataview
TABLE answer_state AS "A", understanding AS "B", concept, hard_because, created
FROM ""
WHERE type = "qa" AND subject = "<subject-slug>"
SORT id ASC
```

---

## Manual Summary (fallback when Dataview is not available; agent keeps it in sync)

| ID | Question (short) | Concept | A | B | Hard because | Next action |
|---|---|---|---|---|---|---|
| [[Q-001]] | | | open | lost | | |
