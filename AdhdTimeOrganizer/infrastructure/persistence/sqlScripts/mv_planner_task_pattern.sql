CREATE MATERIALIZED VIEW public.mv_planner_task_pattern AS
  SELECT pt.user_id,
         pt.activity_id,
         pt.importance_id,
         pt.is_background,
         0 AS pattern_type,
         EXTRACT(ISODOW FROM c.date)::int AS pattern_value,
         COUNT(*) AS occurrence_count,
         make_time(
           (AVG(EXTRACT(HOUR   FROM pt.start_time)))::int,
           (AVG(EXTRACT(MINUTE FROM pt.start_time)))::int, 0) AS avg_start_time,
         make_time(
           (AVG(EXTRACT(HOUR   FROM pt.end_time)))::int,
           (AVG(EXTRACT(MINUTE FROM pt.end_time)))::int, 0) AS avg_end_time
  FROM   public.planner_task pt
  JOIN   public.calendar c ON c.id = pt.calendar_id
  WHERE  pt.status != 4
  GROUP  BY pt.user_id, pt.activity_id, pt.importance_id, pt.is_background,
            EXTRACT(ISODOW FROM c.date)
  HAVING COUNT(*) >= 3

  UNION ALL

  SELECT pt.user_id,
         pt.activity_id,
         pt.importance_id,
         pt.is_background,
         1 AS pattern_type,
         EXTRACT(DAY FROM c.date)::int AS pattern_value,
         COUNT(*) AS occurrence_count,
         make_time(
           (AVG(EXTRACT(HOUR   FROM pt.start_time)))::int,
           (AVG(EXTRACT(MINUTE FROM pt.start_time)))::int, 0) AS avg_start_time,
         make_time(
           (AVG(EXTRACT(HOUR   FROM pt.end_time)))::int,
           (AVG(EXTRACT(MINUTE FROM pt.end_time)))::int, 0) AS avg_end_time
  FROM   public.planner_task pt
  JOIN   public.calendar c ON c.id = pt.calendar_id
  WHERE  pt.status != 4
  GROUP  BY pt.user_id, pt.activity_id, pt.importance_id, pt.is_background,
            EXTRACT(DAY FROM c.date)
  HAVING COUNT(*) >= 3;

CREATE UNIQUE INDEX ux_planner_task_pattern
  ON public.mv_planner_task_pattern (user_id, activity_id, importance_id, is_background, pattern_type, pattern_value);

CREATE INDEX ix_planner_task_pattern_lookup
  ON public.mv_planner_task_pattern (user_id, pattern_type, pattern_value);
