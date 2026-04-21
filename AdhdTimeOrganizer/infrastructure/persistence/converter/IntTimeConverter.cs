using AdhdTimeOrganizer.domain.helper;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdhdTimeOrganizer.infrastructure.persistence.converter;

public class IntTimeConverter() : ValueConverter<IntTime, int>(
    myIntTime => myIntTime.TotalSeconds,
    seconds => new IntTime(seconds)
);