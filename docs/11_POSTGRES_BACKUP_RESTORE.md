# PostgreSQL backup and restore runbook (Web v1)

This is an operational template, not a claim that JAX Power backups are configured or tested. Operations must assign an owner, schedule, retention period, secure storage, and a non-production restore target before pilot use. A daily logical backup is a starting recommendation; choose frequency from the acceptable data-loss window.

## Backup

Use PostgreSQL client tools compatible with the server version. Keep credentials in an approved secret store or short-lived environment, never command-line arguments, shell history, or Git. Example placeholders:

```sh
pg_dump --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$PGDATABASE" --format=custom --file="ipqc-YYYYMMDD.dump"
pg_restore --list "ipqc-YYYYMMDD.dump"
```

Check the command exit code, archive size, readable table-of-contents, and backup job logs. Encrypt and restrict the archive and copy it to the approved backup location. A successful `pg_dump` is not sufficient proof of restoreability; rehearse restore regularly.

## Restore rehearsal on an isolated database

1. Create a **non-production** database with separate credentials and no route to production clients. Disable the scheduler (`Scheduler__Enabled=false`) and outbound integrations in the test environment.
2. Restore the selected archive with `pg_restore --host="$PGHOST" --port="$PGPORT" --username="$PGUSER" --dbname="$PGDATABASE" --clean --if-exists "ipqc-YYYYMMDD.dump"`. `--clean` destroys existing target data; verify the isolated target name before running it.
3. Check EF migration history and application startup against the restored schema. Do not apply unreviewed migrations automatically during restore.
4. Compare counts and representative rows for `ApplicationUsers`, `PatrolStandards`, `PatrolPlans`, `PatrolTasks`, immutable submissions/items/samples, reviews, and Completed tasks. Check that one Completed report resolves its final approved revision.
5. Test login with an authorized test account in the isolated environment. Protect or replace restored credentials according to the organization's test-data policy.
6. Record backup timestamp, tool/server versions, restore start/end, counts, workflow checks, failures, and corrective action.

Do not start the scheduler against a restored copy until its generation boundary and catch-up window have been reviewed; otherwise due historical plans may generate new tasks in the test database. Keep restored test data isolated and access-controlled.

Inspection image/file storage is **not implemented** in current Web v1. When added later, backup and restore must coordinate database metadata with external file bytes and retention.
