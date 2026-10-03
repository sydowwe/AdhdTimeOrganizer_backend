-- avg_start_time / avg_end_time are the mean time of day, averaged on the clock: each time is an angle
-- (seconds since midnight / 86400 of a full turn), the mean is atan2 of the mean sine and cosine, and that
-- is rounded to the nearest minute modulo the day. So 09:50 + 10:10 is 10:00, and 23:50 + 00:10 is 00:00
-- rather than the 12:00 a plain average gives. Two earlier shapes were wrong: hours and minutes averaged
-- separately (09:50 + 10:10 = 10:30), then plain seconds since midnight (23:50 + 00:10 = 12:00).
-- When the times cancel out on the clock (06:00 + 18:00) there is no circular mean; the plain average is
-- used, which is as good a guess as any.
-- start_time / end_time and calendar.date are already the user's wall-clock values, so no zone conversion.
CREATE MATERIALIZED VIEW planning.mv_planner_task_pattern AS
WITH task AS (SELECT pt.user_id,
                     pt.activity_id,
                     pt.importance_id,
                     pt.is_background,
                     EXTRACT(ISODOW FROM c.date)::int  AS iso_day_of_week,
                     EXTRACT(DAY FROM c.date)::int     AS day_of_month,
                     EXTRACT(EPOCH FROM pt.start_time) AS start_s,
                     EXTRACT(EPOCH FROM pt.end_time)   AS end_s
              FROM planning.planner_task pt
                       JOIN planning.calendar c ON c.id = pt.calendar_id
              WHERE pt.status != 4),
     pattern AS (SELECT user_id, activity_id, importance_id, is_background,
                        0                                 AS pattern_type,
                        iso_day_of_week                   AS pattern_value,
                        COUNT(*)                          AS occurrence_count,
                        AVG(start_s)                      AS start_plain,
                        AVG(sin(2 * pi() * start_s / 86400)) AS start_sin,
                        AVG(cos(2 * pi() * start_s / 86400)) AS start_cos,
                        AVG(end_s)                        AS end_plain,
                        AVG(sin(2 * pi() * end_s / 86400))   AS end_sin,
                        AVG(cos(2 * pi() * end_s / 86400))   AS end_cos
                 FROM task
                 GROUP BY user_id, activity_id, importance_id, is_background, iso_day_of_week
                 HAVING COUNT(*) >= 3

                 UNION ALL

                 SELECT user_id, activity_id, importance_id, is_background,
                        1                                 AS pattern_type,
                        day_of_month                      AS pattern_value,
                        COUNT(*)                          AS occurrence_count,
                        AVG(start_s)                      AS start_plain,
                        AVG(sin(2 * pi() * start_s / 86400)) AS start_sin,
                        AVG(cos(2 * pi() * start_s / 86400)) AS start_cos,
                        AVG(end_s)                        AS end_plain,
                        AVG(sin(2 * pi() * end_s / 86400))   AS end_sin,
                        AVG(cos(2 * pi() * end_s / 86400))   AS end_cos
                 FROM task
                 GROUP BY user_id, activity_id, importance_id, is_background, day_of_month
                 HAVING COUNT(*) >= 3),
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
       importance_id,
       is_background,
       pattern_type,
       pattern_value,
       occurrence_count,
       make_interval(mins => round(start_mean_s / 60)::int % 1440)::time AS avg_start_time,
       make_interval(mins => round(end_mean_s / 60)::int % 1440)::time   AS avg_end_time
FROM mean_seconds;

CREATE UNIQUE INDEX ux_planner_task_pattern
    ON planning.mv_planner_task_pattern (user_id, activity_id, importance_id, is_background, pattern_type, pattern_value);

CREATE INDEX ix_planner_task_pattern_lookup
    ON planning.mv_planner_task_pattern (user_id, pattern_type, pattern_value);
