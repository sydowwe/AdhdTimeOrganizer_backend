-- activity_history stores instants (timestamptz). Their weekday, day of month and time of day are taken in
-- the owner's zone (user.timezone), because the planner these suggestions feed is in wall-clock time. Read
-- straight off the timestamptz they would follow the refreshing session's TimeZone instead — UTC on every
-- server we run, since Npgsql sets none — so a Bratislava user's 00:30 Tuesday habit surfaced on Mondays at
-- 22:30.
--
-- A stored id Postgres does not know falls back to UTC rather than failing the refresh: the API validates
-- with TimeZoneInfo.FindSystemTimeZoneById, which also accepts Windows ids ("Central Europe Standard Time"),
-- and one bad row in an AT TIME ZONE would stop this view refreshing for every user.
--
-- avg_start_time / avg_end_time are the mean time of day — see mv_planner_task_pattern.sql for the shape.
CREATE MATERIALIZED VIEW planning.mv_activity_history_pattern AS
WITH local_history AS (SELECT ah.user_id,
                              ah.activity_id,
                              ah.start_timestamp AT TIME ZONE tz.zone AS local_start,
                              ah.end_timestamp AT TIME ZONE tz.zone   AS local_end
                       FROM history.activity_history ah
                                JOIN (SELECT u.id, COALESCE(n.name, 'UTC') AS zone
                                      FROM "user"."user" u
                                               LEFT JOIN pg_timezone_names n ON n.name = u.timezone) tz
                                     ON tz.id = ah.user_id)
SELECT lh.user_id,
       lh.activity_id,
       0                                                                                         AS pattern_type,
       EXTRACT(ISODOW FROM lh.local_start)::int                                                  AS pattern_value,
       COUNT(*)                                                                                  AS occurrence_count,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM lh.local_start::time)) / 60), 1439)::int)::time AS avg_start_time,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM lh.local_end::time)) / 60), 1439)::int)::time   AS avg_end_time
FROM local_history lh
GROUP BY lh.user_id, lh.activity_id, EXTRACT(ISODOW FROM lh.local_start)
HAVING COUNT(*) >= 3

UNION ALL

SELECT lh.user_id,
       lh.activity_id,
       1                                                                                         AS pattern_type,
       EXTRACT(DAY FROM lh.local_start)::int                                                     AS pattern_value,
       COUNT(*)                                                                                  AS occurrence_count,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM lh.local_start::time)) / 60), 1439)::int)::time AS avg_start_time,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM lh.local_end::time)) / 60), 1439)::int)::time   AS avg_end_time
FROM local_history lh
GROUP BY lh.user_id, lh.activity_id, EXTRACT(DAY FROM lh.local_start)
HAVING COUNT(*) >= 3;

CREATE UNIQUE INDEX ux_activity_history_pattern
    ON planning.mv_activity_history_pattern (user_id, activity_id, pattern_type, pattern_value);

CREATE INDEX ix_activity_history_pattern_lookup
    ON planning.mv_activity_history_pattern (user_id, pattern_type, pattern_value);
