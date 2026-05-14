CREATE MATERIALIZED VIEW public.mv_template_suggestion_pattern AS
  SELECT c.user_id,
         c.applied_template_id AS template_id,
         0 AS pattern_type,
         EXTRACT(ISODOW FROM c.date)::int AS pattern_value,
         COUNT(*) AS occurrence_count
  FROM   public.calendar c
  WHERE  c.applied_template_id IS NOT NULL
  GROUP  BY c.user_id, c.applied_template_id, EXTRACT(ISODOW FROM c.date)
  HAVING COUNT(*) >= 2

  UNION ALL

  SELECT c.user_id,
         c.applied_template_id AS template_id,
         1 AS pattern_type,
         CASE c.day_type
           WHEN 'Workday'  THEN 0
           WHEN 'Weekend'  THEN 1
           WHEN 'Vacation' THEN 2
           WHEN 'SickDay'  THEN 3
           WHEN 'Special'  THEN 4
           ELSE -1
         END AS pattern_value,
         COUNT(*) AS occurrence_count
  FROM   public.calendar c
  WHERE  c.applied_template_id IS NOT NULL
  GROUP  BY c.user_id, c.applied_template_id, c.day_type
  HAVING COUNT(*) >= 2;

CREATE UNIQUE INDEX ux_template_suggestion_pattern
  ON public.mv_template_suggestion_pattern (user_id, template_id, pattern_type, pattern_value);

CREATE INDEX ix_template_suggestion_pattern_lookup
  ON public.mv_template_suggestion_pattern (user_id, pattern_type, pattern_value);
