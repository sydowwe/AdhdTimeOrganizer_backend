-- activity_history stores instants (timestamptz). Their weekday, day of month and time of day are taken in
-- the owner's zone (user.timezone), because the planner these suggestions feed is in wall-clock time. Read
-- straight off the timestamptz they would follow the refreshing session's TimeZone instead — UTC on every
-- server we run, since Npgsql sets none — so a Bratislava user's 00:30 Tuesday habit surfaced on Mondays at
-- 22:30. A change of user.timezone marks this view dirty (SuggestionPatternRefreshInterceptor).
--
-- A stored id Postgres does not know falls back to UTC rather than failing the refresh: the API validates
-- with TimeZoneInfo.FindSystemTimeZoneById, which also accepts Windows ids ("Central Europe Standard Time"),
-- and one bad row in an AT TIME ZONE would stop this view refreshing for every user.
--
-- avg_start_time / avg_end_time are the mean time of day averaged on the clock — see
-- mv_planner_task_pattern.sql for the method and why.
--
-- Occurrences are sittings, not rows (the rule HistorySittings applies everywhere else). A timer session —
-- a Pomodoro writes one row per phase — is one sitting per activity, from its first focus period's start to
-- its last focus period's end. Break rows (ShortBreak / LongBreak) are left out altogether: they say when the
-- user rested, not when they did the activity, and the rest activity may be a different one or the same one.
-- A row outside a session is a sitting of its own.
--
-- FO11, the recency window: the same 84/28 days for a weekday pattern and 365/92 for a day of the month as
-- mv_planner_task_pattern.sql, which explains them. Measured on the sitting's local date against the
-- session's current_date (UTC), a day loose at either edge. The outer 366-day cut is on the instant, so it
-- never drops a sitting the 365-day window would keep.
CREATE MATERIALIZED VIEW planning.mv_activity_history_pattern AS
WITH local_history AS (SELECT ah.id,
                              ah.user_id,
                              ah.activity_id,
                              ah.session_id,
                              ah.start_timestamp AT TIME ZONE tz.zone AS local_start,
                              ah.end_timestamp AT TIME ZONE tz.zone   AS local_end
                       FROM history.activity_history ah
                                JOIN (SELECT u.id, COALESCE(n.name, 'UTC') AS zone
                                      FROM "user"."user" u
                                               LEFT JOIN pg_timezone_names n ON n.name = u.timezone) tz
                                     ON tz.id = ah.user_id
                       WHERE (ah.phase IS NULL OR ah.phase = 'Focus')
                         AND ah.start_timestamp >= now() - interval '366 days'),
     sitting AS (SELECT user_id,
                        activity_id,
                        min(local_start) AS local_start,
                        max(local_end)   AS local_end
                 FROM local_history
                 GROUP BY user_id, activity_id, session_id, CASE WHEN session_id IS NULL THEN id END),
     sitting_time AS (SELECT user_id,
                             activity_id,
                             local_start::date                     AS local_date,
                             EXTRACT(ISODOW FROM local_start)::int AS iso_day_of_week,
                             EXTRACT(DAY FROM local_start)::int    AS day_of_month,
                             EXTRACT(EPOCH FROM local_start::time) AS start_s,
                             EXTRACT(EPOCH FROM local_end::time)   AS end_s
                      FROM sitting),
     pattern AS (SELECT user_id, activity_id,
                        0                                    AS pattern_type,
                        iso_day_of_week                      AS pattern_value,
                        COUNT(*)                             AS occurrence_count,
                        AVG(start_s)                         AS start_plain,
                        AVG(sin(2 * pi() * start_s / 86400)) AS start_sin,
                        AVG(cos(2 * pi() * start_s / 86400)) AS start_cos,
                        AVG(end_s)                           AS end_plain,
                        AVG(sin(2 * pi() * end_s / 86400))   AS end_sin,
                        AVG(cos(2 * pi() * end_s / 86400))   AS end_cos
                 FROM sitting_time
                 WHERE local_date >= current_date - 84
                 GROUP BY user_id, activity_id, iso_day_of_week
                 HAVING COUNT(*) >= 3
                    AND max(local_date) >= current_date - 28

                 UNION ALL

                 SELECT user_id, activity_id,
                        1                                    AS pattern_type,
                        day_of_month                         AS pattern_value,
                        COUNT(*)                             AS occurrence_count,
                        AVG(start_s)                         AS start_plain,
                        AVG(sin(2 * pi() * start_s / 86400)) AS start_sin,
                        AVG(cos(2 * pi() * start_s / 86400)) AS start_cos,
                        AVG(end_s)                           AS end_plain,
                        AVG(sin(2 * pi() * end_s / 86400))   AS end_sin,
                        AVG(cos(2 * pi() * end_s / 86400))   AS end_cos
                 FROM sitting_time
                 WHERE local_date >= current_date - 365
                 GROUP BY user_id, activity_id, day_of_month
                 HAVING COUNT(*) >= 3
                    AND max(local_date) >= current_date - 92),
     mean_seconds AS (SELECT p.*,
                             CASE
                                 WHEN sqrt(start_sin ^ 2 + start_cos ^ 2) < 1e-6 THEN start_plain
                                 ELSE (atan2(start_sin, start_cos) / (2 * pi()) * 86400 + 86400)::numeric % 86400
                                 END AS start_mean_s,
                             CASE
                                 WHEN sqrt(end_sin ^ 2 + end_cos ^ 2) < 1e-6 THEN end_plain
                                 ELSE (atan2(end_sin, end_cos) / (2 * pi()) * 86400 + 86400)::numeric % 86400
                                 END AS end_mean_s
                      FROM pattern p)
SELECT user_id,
       activity_id,
       pattern_type,
       pattern_value,
       occurrence_count,
       make_interval(mins => round(start_mean_s / 60)::int % 1440)::time AS avg_start_time,
       make_interval(mins => round(end_mean_s / 60)::int % 1440)::time   AS avg_end_time
FROM mean_seconds;

CREATE UNIQUE INDEX ux_activity_history_pattern
    ON planning.mv_activity_history_pattern (user_id, activity_id, pattern_type, pattern_value);

CREATE INDEX ix_activity_history_pattern_lookup
    ON planning.mv_activity_history_pattern (user_id, pattern_type, pattern_value);
