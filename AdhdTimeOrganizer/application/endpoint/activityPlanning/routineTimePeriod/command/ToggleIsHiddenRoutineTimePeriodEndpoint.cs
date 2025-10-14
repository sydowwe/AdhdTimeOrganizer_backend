﻿using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence;

namespace AdhdTimeOrganizer.application.endpoint.activityPlanning.routineTimePeriod.command;

public class ToggleIsHiddenRoutineTimePeriodEndpoint(AppCommandDbContext dbContext) : BaseToggleIsHiddenEndpoint<RoutineTimePeriod>(dbContext);