# Scheduler

> The always-on time substrate: it owns *when* background work runs and *how to observe it* — never the work itself.

## What it does

Scheduler centralises background-job scheduling for the whole platform. It owns the Quartz.NET infrastructure, a registry of recurring jobs, an append-only run log, and the dispatcher that invokes a
handler **by key**. Owning modules (Reminders, Exports, the existing per-module jobs, …) keep their job *logic* and register a recurring schedule against the `Kernel.scheduling` contract; Scheduler
decides when to fire and records what happened. This is the *generic* substrate — it imports no domain module and no Notifications.

This phase (01) ships only the persistence + the cross-module contract. Quartz wiring, the dispatcher, endpoints, the dashboard and job migration land in later phases (02a → 05).

## Setup / running

No module-specific setup. The two tables (`scheduled_job`, `scheduled_job_run`) are created by the portal's EF migration (`SchedulerModule`). See root README for running migrations.

## Docs

- `summary.md` — start here if you're working in this module
- `domain-map.md` — model, invariants, the Kernel contract, file index
