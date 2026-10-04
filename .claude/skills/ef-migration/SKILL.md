---
name: ef-migration
description: Create and verify an EF Core migration for the PostgreSQL schema. Use when an entity, index or constraint changes, or when asked to add a table, column or migration.
---

# EF Core migration

Applies to `src/CurveRisk.Infrastructure` (PLAN.md phase 2). Migrations are applied to a database that holds market history which cannot be recomputed, so each one is reviewed as SQL, not as C#.

## Steps

1. Change the entity and its `IEntityTypeConfiguration`. Column types are explicit: `numeric(28,10)` for rates and amounts (never `double precision` for stored money), `date` for business dates, `timestamptz` for instants, `jsonb` for curve pillars.
2. Generate:
   ```bash
   dotnet ef migrations add <PascalCaseName> --project src/CurveRisk.Infrastructure --startup-project src/CurveRisk.Api
   ```
3. Produce the SQL and read all of it:
   ```bash
   dotnet ef migrations script <PreviousMigration> <PascalCaseName> --idempotent --project src/CurveRisk.Infrastructure --startup-project src/CurveRisk.Api
   ```
4. Check the SQL against this list and fix the migration by hand where needed:
   - No `DROP COLUMN` or `DROP TABLE` unless the user asked for data to be removed. A rename must be `RENAME`, not drop-and-add; EF guesses wrong when a property and its type change together.
   - New `NOT NULL` column on an existing table has a default or a backfill step.
   - Index creation on large tables uses `CREATE INDEX CONCURRENTLY` (raw SQL, migration marked non-transactional).
   - Foreign keys have an index on the referencing side.
   - `Down` really reverses `Up`, or throws with an explanation when it cannot.
5. Test: the integration suite applies all migrations to a fresh Testcontainers database. Add a test that inserts a row in the old shape, applies the migration, and reads it back, whenever existing data is transformed.

## Do not

- Run `dotnet ef database update` against anything but a local or test database. It is in the `ask` list for that reason; `database drop` is denied outright.
- Edit a migration that has been merged. Add a new one.
- Put seed market data in a migration. That is the importer's job.

Give the user the generated SQL in your summary so they can review what will run.
