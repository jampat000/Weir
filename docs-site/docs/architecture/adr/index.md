---
sidebar_position: 2
title: Architecture Decision Records
---

# Architecture Decision Records

ADRs capture locked structural and platform choices for Weir. They override ad hoc experimentation when the two conflict.

Some ADR numbers are intentionally absent — those were reserved for drafts or withdrawn decisions.

## Index

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-0001](adr-0001) | Repository and application layout | Accepted; backend path updated by ADR-0017 |
| [ADR-0002](adr-0002) | Database and Alembic (SQLite-first) | Accepted; Alembic replaced by numbered SQL migrations (ADR-0017) |
| [ADR-0003](adr-0003) | Auth and session model | Accepted |
| [ADR-0007](adr-0007) | Module-owned worker lanes (SQLite) | Accepted; superseded in part by ADR-0016 |
| [ADR-0008](adr-0008) | WeirSettings aggregate for runtime configuration | Superseded by ADR-0017 |
| [ADR-0009](adr-0009) | Suite-wide timing isolation (durable work) | Accepted |
| [ADR-0012](adr-0012) | Processing preflight parity boundary | Accepted |
| [ADR-0013](adr-0013) | A media manager is a kind, not a product name | Accepted |
| [ADR-0014](adr-0014) | A Processing library is a row, not one of two fixed scopes | Accepted |
| [ADR-0015](adr-0015) | Processing asks a port, and "no answer" is not "nothing" | Accepted; retention half moved out by ADR-0016 |
| [ADR-0016](adr-0016) | Weir is one thing, and it never strands a file | Accepted |
| [ADR-0017](adr-0017) | Weir's backend moves to C# on .NET 10 | Accepted |

ADR-0001, ADR-0002 and ADR-0008 describe the original Python backend. ADR-0017 replaced it with the .NET server in `apps/server`; each of those pages notes what changed.

## When to add an ADR

Add an ADR when a decision changes runtime storage, data safety, security boundaries, release mechanics, or packaging behavior.
