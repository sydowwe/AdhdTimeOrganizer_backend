-- avg_start_time / avg_end_time are the mean time of day: averaged as seconds since midnight, rounded to
-- the minute, and held at 23:59 so a mean in the day's last half-minute does not round to 24:00 and wrap to
-- 00:00. Averaging hours and minutes separately (the earlier shape) made 09:50 + 10:10 come out as 10:30.
-- start_time / end_time and calendar.date are already the user's wall-clock values, so no zone conversion.
CREATE MATERIALIZED VIEW planning.mv_planner_task_pattern AS
SELECT pt.user_id,
       pt.activity_id,
       pt.importance_id,
       pt.is_background,
       0                                                                                   AS pattern_type,
       EXTRACT(ISODOW FROM c.date)::int                                                    AS pattern_value,
       COUNT(*)                                                                            AS occurrence_count,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM pt.start_time)) / 60), 1439)::int)::time AS avg_start_time,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM pt.end_time)) / 60), 1439)::int)::time   AS avg_end_time
FROM planning.planner_task pt
         JOIN planning.calendar c ON c.id = pt.calendar_id
WHERE pt.status != 4
GROUP BY pt.user_id, pt.activity_id, pt.importance_id, pt.is_background,
         EXTRACT(ISODOW FROM c.date)
HAVING COUNT(*) >= 3

UNION ALL

SELECT pt.user_id,
       pt.activity_id,
       pt.importance_id,
       pt.is_background,
       1                                                                                   AS pattern_type,
       EXTRACT(DAY FROM c.date)::int                                                       AS pattern_value,
       COUNT(*)                                                                            AS occurrence_count,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM pt.start_time)) / 60), 1439)::int)::time AS avg_start_time,
       make_interval(mins => LEAST(round(AVG(EXTRACT(EPOCH FROM pt.end_time)) / 60), 1439)::int)::time   AS avg_end_time
FROM planning.planner_task pt
         JOIN planning.calendar c ON c.id = pt.calendar_id
WHERE pt.status != 4
GROUP BY pt.user_id, pt.activity_id, pt.importance_id, pt.is_background,
         EXTRACT(DAY FROM c.date)
HAVING COUNT(*) >= 3;

CREATE UNIQUE INDEX ux_planner_task_pattern
    ON planning.mv_planner_task_pattern (user_id, activity_id, importance_id, is_background, pattern_type, pattern_value);

CREATE INDEX ix_planner_task_pattern_lookup
    ON planning.mv_planner_task_pattern (user_id, pattern_type, pattern_value);
