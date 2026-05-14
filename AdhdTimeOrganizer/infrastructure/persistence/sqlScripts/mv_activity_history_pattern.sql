CREATE MATERIALIZED VIEW public.mv_activity_history_pattern AS
  SELECT ah.user_id,
         ah.activity_id,
         0 AS pattern_type,
         EXTRACT(ISODOW FROM ah.start_timestamp)::int AS pattern_value,
         COUNT(*) AS occurrence_count,
         make_time(
           (AVG(EXTRACT(HOUR   FROM ah.start_timestamp::time)))::int,
           (AVG(EXTRACT(MINUTE FROM ah.start_timestamp::time)))::int, 0) AS avg_start_time,
         make_time(
           (AVG(EXTRACT(HOUR   FROM ah.end_timestamp::time)))::int,
           (AVG(EXTRACT(MINUTE FROM ah.end_timestamp::time)))::int, 0) AS avg_end_time
  FROM   public.activity_history ah
  GROUP  BY ah.user_id, ah.activity_id, EXTRACT(ISODOW FROM ah.start_timestamp)
  HAVING COUNT(*) >= 3

  UNION ALL

  SELECT ah.user_id,
         ah.activity_id,
         1 AS pattern_type,
         EXTRACT(DAY FROM ah.start_timestamp)::int AS pattern_value,
         COUNT(*) AS occurrence_count,
         make_time(
           (AVG(EXTRACT(HOUR   FROM ah.start_timestamp::time)))::int,
           (AVG(EXTRACT(MINUTE FROM ah.start_timestamp::time)))::int, 0) AS avg_start_time,
         make_time(
           (AVG(EXTRACT(HOUR   FROM ah.end_timestamp::time)))::int,
           (AVG(EXTRACT(MINUTE FROM ah.end_timestamp::time)))::int, 0) AS avg_end_time
  FROM   public.activity_history ah
  GROUP  BY ah.user_id, ah.activity_id, EXTRACT(DAY FROM ah.start_timestamp)
  HAVING COUNT(*) >= 3;

CREATE UNIQUE INDEX ux_activity_history_pattern
  ON public.mv_activity_history_pattern (user_id, activity_id, pattern_type, pattern_value);

CREATE INDEX ix_activity_history_pattern_lookup
  ON public.mv_activity_history_pattern (user_id, pattern_type, pattern_value);
