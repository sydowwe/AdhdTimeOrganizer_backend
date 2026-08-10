# Review: AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/EntityWithActivityBuilderExtensions.cs
Role: config
Summary: Small, focused builder-extension pair for wiring `BaseEntityWithActivity` FKs; correct and safe as used today, but the `isRequired` parameter is a footgun against the non-nullable `ActivityId` column and the API shape has drifted from the sibling `EntityWithUserBuilderExtensions`.

## Issues
- [Low][Quality] AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/EntityWithActivityBuilderExtensions.cs:11,16,21,26 — `isRequired` is exposed as a caller-settable parameter (default `true`), but `BaseEntityWithActivity.ActivityId` is declared `long` (non-nullable), so `IsRequired(false)` would fight the CLR type and produce a confusing/incorrect model (EF typically still infers required from the non-nullable FK property regardless of the explicit `IsRequired(false)` call, or throws depending on convention timing).
  Why: A future caller could pass `isRequired: false` expecting an optional Activity link and get either a silently-ignored setting or a runtime model-building surprise; there's no `long?` counterpart to make that combination sensible.
  Fix: Drop the `isRequired` parameter (mirror `EntityWithUserBuilderExtensions`, which has none) unless/until `ActivityId` is made nullable for a genuine optional-FK use case.
  Confidence: Med

- [Nit][Convention] AdhdTimeOrganizer/infrastructure/persistence/configuration/extensions/EntityWithActivityBuilderExtensions.cs:10-28 — API shape diverges from the sibling `EntityWithUserBuilderExtensions`: this one returns `void` (no fluent chaining) and is a plain static method rather than the C# 13 `extension<TEntity>(...)` member syntax the User variant now uses.
  Why: Inconsistent style within the same folder/purpose makes the codebase harder to predict; the User version was clearly the more recently modernized twin.
  Fix: Optionally align to the `extension<TEntity>(...)` block and return the `ReferenceCollectionBuilder`/`ReferenceReferenceBuilder` for chaining, matching `IsManyWithOneUser`/`IsOneWithOneUser`.
  Confidence: Low

No other issues found — all current call sites (`RoutineToDoListConfiguration`, `ToDoListItemConfiguration`, `TemplateTaskConfiguration`, `RepeatingPlannerTaskConfiguration`, `PlannerTaskConfiguration`, `ActivityHistoryConfiguration`, `MemoryAnchorConfiguration`) use the defaults (`isRequired: true`, `Cascade`), and `BaseEntityWithActivity` itself derives from `BaseEntityWithUser`, so entities configured through this helper are covered by the portal's global `IEntityWithUser` query filter (unlike the `Activity*Profile` entities called out in CLAUDE.md, which don't go through this file at all).
