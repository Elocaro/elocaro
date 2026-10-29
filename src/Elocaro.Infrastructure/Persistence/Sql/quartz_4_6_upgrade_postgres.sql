-- Quartz.NET: schema upgrade from 3.22 to 4.6 (PostgreSQL), as the scripts of the Quartz.NET repository
-- (database/migrations/4.x) in the order the schema validation of 4.6 names. Every statement checks first,
-- so the file can run more than once. Source: https://github.com/quartznet/quartznet

-- ===== database/migrations/4.0/schema_30_to_40_upgrade_postgres.sql =====
--
-- Quartz.NET schema migration -- 3.x to 4.0
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   MANDATORY. This is the one migration you cannot skip.
--
--   Quartz.NET 3.x probes for MISFIRE_ORIG_FIRE_TIME, EXECUTION_GROUP, PREFERRED_NODE
--   and PREFERRED_NODE_AUTO at startup and degrades gracefully when they are absent.
--   4.x removed those probes and assumes all four exist, so a 3.x database that never
--   ran the optional migrations will fail against 4.x until this script has run.
--
--   4.x also adds columns and a table 3.x never had -- RETRY_POLICY and RETRY_ATTEMPT
--   on QRTZ_TRIGGERS, and the whole QRTZ_PAUSED_JOB_GRPS table -- and validates its
--   schema at startup, so this script is required even for a 3.x database that took
--   every optional migration going.
--
-- This script supersedes the optional per-feature migrations in ../3.17, ../3.18 and
-- ../3.19 -- it applies everything they do. If you already ran some of them, run this
-- anyway: every statement checks first, so it is safe on a partially-migrated database.
-- (../3.20's index alignment is superseded by schema_30_to_40_indexes_postgres.sql,
-- beside this file.)
--
-- Sections, in order:
--   1. MISFIRE_ORIG_FIRE_TIME column                REQUIRED
--   2. EXECUTION_GROUP columns                      REQUIRED
--   3. PREFERRED_NODE / PREFERRED_NODE_AUTO         REQUIRED
--   4. RETRY_POLICY / RETRY_ATTEMPT                 REQUIRED
--   5. QRTZ_PAUSED_JOB_GRPS table                   REQUIRED
--
-- Every section in this file is safe to run while 3.x nodes are still up, which is why the
-- index set is not in it. That is a second file in this folder --
-- schema_30_to_40_indexes_postgres.sql -- and it is NOT safe to run during a mixed window:
-- it drops IDX_QRTZ_T_NFT_ST_MISFIRE, which 3.x drives its misfire sweep from and 4.x does
-- not read at all (#3656). Run this file now; run that one once the last 3.x node has shut
-- down, or straight afterwards on an upgrade with nothing running.
--
-- Sections 4 and 5 have no 3.x counterpart at all, so nothing you ran on 3.x can have
-- applied them.
--
-- RETRY_POLICY holds a trigger's retry policy and RETRY_ATTEMPT how many retries of the
-- occurrence being executed have already been made. Both are nullable with no default, so
-- every existing row reads as "no retry policy" and no data migration is needed (#3520).
--
-- 3.x pauses a job group without recording it anywhere, so a paused job group could not be
-- listed or asked about; 4.x keeps the group names in QRTZ_PAUSED_JOB_GRPS, which is what
-- makes JobGroup.Paused answer truthfully and what carries the pause across a restart
-- (#3336).
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. MISFIRE_ORIG_FIRE_TIME on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.17, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'misfire_orig_fire_time') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN misfire_orig_fire_time bigint null;
  END IF;
END $$;

-- === 2. EXECUTION_GROUP on QRTZ_TRIGGERS and QRTZ_FIRED_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.18, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'execution_group') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN execution_group varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'execution_group') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN execution_group varchar(200) null;
  END IF;
END $$;

-- === 3. PREFERRED_NODE and PREFERRED_NODE_AUTO on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x. Optional in 3.19, so it may already be present.

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'preferred_node_auto') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN preferred_node_auto bool not null default false;
  END IF;
END $$;

-- === 4. RETRY_POLICY and RETRY_ATTEMPT on QRTZ_TRIGGERS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent, so on a database coming
-- from 3.x both columns are always absent. Nullable with no default: an existing row
-- reads as "no retry policy".

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'retry_policy') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN retry_policy varchar(250) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'retry_attempt') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN retry_attempt integer null;
  END IF;
END $$;

-- === 5. QRTZ_PAUSED_JOB_GRPS ===
-- REQUIRED for 4.x, and new in it -- 3.x has no equivalent. One row per paused job
-- group, mirroring QRTZ_PAUSED_TRIGGER_GRPS. Guarded on every dialect, SQLite
-- included: CREATE TABLE IF NOT EXISTS is conditional DDL SQLite does have.

CREATE TABLE IF NOT EXISTS qrtz_paused_job_grps (
  sched_name TEXT NOT NULL,
  job_group TEXT NOT NULL,
  PRIMARY KEY (sched_name, job_group)
);

-- ===== database/migrations/4.2/add_continuations_postgres.sql =====
--
-- Quartz.NET schema migration -- add the continuation columns
--
-- Introduced in Quartz.NET 4.2.0 (#3805)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.2  REQUIRED. A 4.2 node reads and writes these three columns on every trigger
--        it stores, so it refuses to start against a database without them.
--
--        Safe to run while 4.0 and 4.1 nodes are still up: the columns are nullable
--        with no default, so every existing row is already valid, and those nodes
--        never read them. What a 4.0 or 4.1 node cannot do is settle a continuation,
--        so migrate, roll every node, and only then start scheduling them.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql first; this file is what 4.2 adds
--        on top of it.
--
-- A continuation is a trigger that waits, in the store, for another trigger's
-- firing to end. It is held in the new TRIGGER_STATE value 'AWAITING' and is never
-- acquired while it is there; the parent's completion settles it inside the
-- parent's own transaction, so whichever node ran the parent is the node that
-- releases it and a crash cannot lose one.
--
-- CONTINUES_TRIGGER_NAME and CONTINUES_TRIGGER_GROUP name the trigger waited for;
-- they carry the same declaration as TRIGGER_NAME and TRIGGER_GROUP, made
-- nullable, because what they hold is a trigger key.
--
-- CONTINUATION_CONDITION is the integer of the ContinuationCondition flags that
-- release the wait: 1 OnSuccess, 2 OnFailure, 4 OnCancellation, 8 OnVeto, and
-- 15 OnAnyOutcome. A row that names a parent but has no condition reads as 15.
--
-- No index is added. The settlement lookup filters SCHED_NAME and TRIGGER_STATE by
-- equality, which is what IDX_QRTZ_T_NFT_ST already leads with, and 'AWAITING' is a
-- small partition even of a schedule that uses continuations heavily. Measure
-- before adding one.
--
-- ALL THREE COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continues_trigger_name') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continues_trigger_name text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continues_trigger_group') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continues_trigger_group text null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'continuation_condition') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN continuation_condition integer null;
  END IF;
END $$;

-- ===== database/migrations/4.3/add_fire_progress_postgres.sql =====
--
-- Quartz.NET schema migration -- add the fire progress columns
--
-- Introduced in Quartz.NET 4.3.0 (#3874)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node writes these two columns when a running job reports its
--        progress and reads them whenever it lists what is executing, so it refuses to
--        start against a database without them.
--
--        Safe to run while 4.2 nodes are still up: the columns are nullable with no
--        default, so every existing row is already valid, and a 4.2 node never names them.
--        A firing a 4.2 node runs reads as one that has reported no progress.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- PROGRESS is the percentage, 0 to 100, a running job last passed to
-- IJobExecutionContext.ReportProgress; PROGRESS_MESSAGE is the message it passed with it,
-- truncated to 250 characters. The scheduler writes them at most once a second per firing
-- and only when they change, by ENTRY_ID -- the row belongs to the node writing it, so no
-- lock is taken. The row is deleted when the firing completes, as it always was.
--
-- No index is added. The write is by primary key, and the read is the fire-instance listing,
-- which already reads this table.
--
-- BOTH COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'progress') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN progress integer null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'progress_message') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN progress_message varchar(250) null;
  END IF;
END $$;

-- ===== database/migrations/4.3/add_overlap_policy_postgres.sql =====
--
-- Quartz.NET schema migration -- add the overlap policy column
--
-- Introduced in Quartz.NET 4.3.0 (#3875)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node reads and writes this column on every trigger it stores, so
--        it refuses to start against a database without it.
--
--        Safe to run while 4.2 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.2 node never names it. What a 4.2
--        node cannot do is honour a policy -- it fires every trigger as Default does -- so
--        migrate, roll every node, and only then give a trigger an overlap policy.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- OVERLAP_POLICY is the integer of the trigger's OverlapPolicy: what happens when a
-- firing comes due while an earlier firing of the same trigger is still running.
-- NULL and 0 are Default, 1 Skip, 2 BufferOne, 3 CancelPrevious, 4 AllowAll. The store
-- adds 16 to CancelPrevious (19) while a firing that started under another policy may
-- still be running, and clears it once none is.
--
-- No index is added. The column is read with the rest of the trigger's row, never
-- searched on.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'overlap_policy') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN overlap_policy integer null;
  END IF;
END $$;

-- ===== database/migrations/4.3/add_pause_reason_postgres.sql =====
--
-- Quartz.NET schema migration -- add the pause reason columns
--
-- Introduced in Quartz.NET 4.3.0 (#3879)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.3  REQUIRED. A 4.3 node writes these columns whenever it pauses a trigger or a group,
--        so it refuses to start against a database without them.
--
--        Safe to run while 4.2 nodes are still up: the columns are nullable with no default,
--        so every existing row is already valid, and a 4.2 node never names them. A pause a
--        4.2 node makes leaves them NULL, which reads as a pause that gave no reason. A 4.2
--        node's resume leaves a trigger's columns behind, so a 4.3 node reports them only
--        while the trigger is paused -- migrate, and roll every node.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.3 adds on top of them.
--
-- PAUSE_REASON is why the trigger or group was paused, as the caller said it, cut to 250
-- characters. PAUSED_BY is who asked, cut to 200: a user name, or 'quartz:retries-exhausted'
-- when the scheduler paused a trigger whose retries ran out. PAUSED_AT is when, in ticks,
-- as every other instant in this schema is stored.
--
-- A 4.3 node writes all three on every pause, a pause without a reason included, and clears
-- them on a trigger it resumes. A group's row is deleted when the group is resumed, so it
-- takes its columns with it.
--
-- No index is added. The columns are read with the row they are on, never searched on.
--
-- ALL NINE COLUMNS MUST BE ADDED TOGETHER.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

-- === 1. QRTZ_TRIGGERS ===

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'pause_reason') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN pause_reason varchar(250) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'paused_by') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN paused_by varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'paused_at') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN paused_at bigint null;
  END IF;
END $$;

-- === 2. QRTZ_PAUSED_TRIGGER_GRPS ===

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_trigger_grps' AND column_name = 'pause_reason') THEN
    ALTER TABLE qrtz_paused_trigger_grps ADD COLUMN pause_reason varchar(250) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_trigger_grps' AND column_name = 'paused_by') THEN
    ALTER TABLE qrtz_paused_trigger_grps ADD COLUMN paused_by varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_trigger_grps' AND column_name = 'paused_at') THEN
    ALTER TABLE qrtz_paused_trigger_grps ADD COLUMN paused_at bigint null;
  END IF;
END $$;

-- === 3. QRTZ_PAUSED_JOB_GRPS ===

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_job_grps' AND column_name = 'pause_reason') THEN
    ALTER TABLE qrtz_paused_job_grps ADD COLUMN pause_reason varchar(250) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_job_grps' AND column_name = 'paused_by') THEN
    ALTER TABLE qrtz_paused_job_grps ADD COLUMN paused_by varchar(200) null;
  END IF;
END $$;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_paused_job_grps' AND column_name = 'paused_at') THEN
    ALTER TABLE qrtz_paused_job_grps ADD COLUMN paused_at bigint null;
  END IF;
END $$;

-- ===== database/migrations/4.6/add_scheduler_version_postgres.sql =====
--
-- Quartz.NET schema migration -- add the scheduler version column
--
-- Introduced in Quartz.NET 4.6.0 (#4067)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  REQUIRED. A 4.6 node writes this column at every check-in and reads it for every
--        live node, so it refuses to start against a database without it.
--
--        Safe to run while 4.5 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.5 node never names it. A 4.5
--        node's row therefore reads NULL, which a 4.6 node reads as 'older than 4.6'.
--
--        Run it, then roll every node to 4.6. A 4.6 node keeps taking TRIGGER_ACCESS for
--        every firing round while any live row reads NULL or a version below 4.6, and goes
--        lock-free on its next check-in after the last such row ages out. Nothing has to be
--        configured for the rollout; the column is what coordinates it.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.6 adds on top of them.
--
-- QUARTZ_VERSION is the Quartz version the node runs, as major.minor.patch ('4.6.0'),
-- written with LAST_CHECKIN_TIME at every check-in. NULL is a node older than 4.6, or a
-- row an older node inserted. A node's first check-in recovers and deletes its own stale
-- row before it writes a fresh one, so a node downgraded under the same instance id never
-- keeps a newer stamp.
--
-- A 4.6 node fires a round of triggers whose jobs allow concurrent execution without the
-- cluster's TRIGGER_ACCESS lock only while every live row -- one not past its check-in
-- failure threshold -- carries 4.6 or later. The lock is what kept a 4.5 node's misfire
-- and edit writes from landing over such a round, and a 4.6 node cannot guard writes it
-- does not make.
--
-- No index is added. The column is read with the rest of the row, which the check-in
-- already reads, and never searched on.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_scheduler_state' AND column_name = 'quartz_version') THEN
    ALTER TABLE qrtz_scheduler_state ADD COLUMN quartz_version varchar(20) null;
  END IF;
END $$;

-- ===== database/migrations/4.6/add_fired_trigger_interrupt_postgres.sql =====
--
-- Quartz.NET schema migration -- add the fired trigger interrupt column
--
-- Introduced in Quartz.NET 4.6.0 (#4071)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  REQUIRED. A 4.6 node writes this column when an interrupt is asked of a firing
--        another node runs, and reads it for its own firings at every check-in, so it
--        refuses to start against a database without it.
--
--        Safe to run while 4.5 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.5 node never names it. A request
--        for a firing a 4.5 node runs is recorded and never answered; a 4.6 node says so
--        once per such node. Roll every node before relying on a cluster-wide interrupt.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.6 adds on top of them.
--
-- INTERRUPT_REQUESTED_TIME is when an interrupt of the firing was requested from a node
-- other than the one running it, in ticks like FIRED_TIME. NULL is a firing nobody asked
-- to interrupt. The row is deleted when the firing completes, as it always was, so a
-- request never outlives its firing.
--
-- The node running the firing reads the requests on its own rows at every check-in and
-- cancels each job's token, as a local interrupt does: a request lands within one
-- check-in interval.
--
-- No index is added. The read filters SCHED_NAME and INSTANCE_NAME, which
-- IDX_QRTZ_FT_INST_JOB_REQ_RCVRY already leads with, and one node's rows are the firings
-- it holds: at most its thread count plus one acquisition batch.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_fired_triggers' AND column_name = 'interrupt_requested_time') THEN
    ALTER TABLE qrtz_fired_triggers ADD COLUMN interrupt_requested_time bigint null;
  END IF;
END $$;

-- ===== database/migrations/4.6/add_trigger_misfire_threshold_postgres.sql =====
--
-- Quartz.NET schema migration -- add the trigger misfire threshold column
--
-- Introduced in Quartz.NET 4.6.0 (#4070)
--
-- PostgreSQL only. Run the file matching your database; the other dialects live
-- alongside this one in the same folder.
--
-- STATUS
--   4.6  REQUIRED. A 4.6 node reads and writes this column on every trigger it stores,
--        and its misfire scan and acquisition compare against it, so it refuses to start
--        against a database without it.
--
--        Safe to run while 4.5 nodes are still up: the column is nullable with no default,
--        so every existing row is already valid, and a 4.5 node never names it. What a
--        4.5 node cannot do is honour a threshold -- it judges every trigger by the store's
--        MisfireThreshold -- so migrate, roll every node, and only then give a trigger a
--        threshold of its own.
--
--   4.1  Run ../4.2/add_continuations_postgres.sql first on a database created by
--        4.0 or 4.1, and every later required migration.
--
--   3.x  Not applicable. Upgrading from 3.x means running
--        ../4.0/schema_30_to_40_upgrade_postgres.sql and every later migration first;
--        this file is what 4.6 adds on top of them.
--
-- MISFIRE_THRESHOLD is the trigger's own misfire threshold in milliseconds: how late it
-- may fire before its misfire instruction applies. NULL is the store's MisfireThreshold,
-- which is what every row a node older than 4.6 writes reads as. A 4.5 node that updates
-- a row leaves the column as it found it, because it never names it.
--
-- No index is added. The misfire scan and acquisition range over IDX_QRTZ_T_NFT_ST on
-- NEXT_FIRE_TIME as before, and read this column as a residual beside it.
--
-- Replace 'QRTZ_' with your configured table prefix if different.
-- Every statement checks first, so this script is safe to run more than once.
--
-- !! FIRST RUN IN TEST ENVIRONMENT AGAINST A COPY OF YOUR PRODUCTION DATABASE !!
--

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                 WHERE table_name = 'qrtz_triggers' AND column_name = 'misfire_threshold') THEN
    ALTER TABLE qrtz_triggers ADD COLUMN misfire_threshold bigint null;
  END IF;
END $$;
