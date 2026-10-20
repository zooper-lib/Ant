---
description: learned preferences, project conventions, and Do-Not-Repeat rules
budget_tokens: 2000
---
# Cerebrum

> OpenWolf's learning memory. Updated automatically as the AI learns from interactions.
> Do not edit manually unless correcting an error.
> Last updated: 2026-10-20

## User Preferences

<!-- How the user likes things done. Code style, tools, patterns, communication. -->

## Key Learnings

- **Project:** Ant

## Do-Not-Repeat

<!-- Mistakes made and corrected. Each entry prevents the same mistake recurring. -->
<!-- Format: [YYYY-MM-DD] Description of what went wrong and what to do instead. -->
- [2026-10-20] Shipped a separate `Zooper.Ant.Testing` contract-suite package. Daniel never wanted it: adapter authors test their own adapters. Do not ship test packages; test helpers stay inside `Zooper.Ant.Tests`.
- [2026-10-20] Built `ReadmeSnippets.cs` to make README code compile (with fake Dapr types). README code only has to showcase usage, it is never compiled or tested. Show features in `Zooper.Ant.Example` instead.
- [2026-10-20] Shipped without an example project because tasks.md omitted it. Every Zooper library gets a runnable `Zooper.{Name}.Example` showing off all features; flag plan gaps like this instead of silently following the task list.

## Decision Log

<!-- Significant technical decisions with rationale. Why X was chosen over Y. -->

## User preferences (2026-10-20)
- Daniel is not deep in paging/database-internals; on deep technical design questions he delegates ("I trust your decision making"). Still show the artifact and the tension, but recommend firmly and keep the question short.
- Cross-cutting requirement for Ant: the library must be easy to use. Judge every API decision by the common-stack call site first.
- CORRECTION 2026-10-20: do not ask Daniel implementation-level questions (interface shapes, method signatures, where a type lives). He decides direction only (which stores ship, which hosts, what the product promises). Settle implementation yourself, record it with rationale, move on.
