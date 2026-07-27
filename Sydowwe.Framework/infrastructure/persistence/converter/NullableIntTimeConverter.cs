using AdhdTimeOrganizer.domain.helper;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdhdTimeOrganizer.infrastructure.persistence.converter;

public class NullableIntTimeConverter() : ValueConverter<IntTime?, int>(
    myIntTime => myIntTime == null ? 0 : myIntTime.TotalSeconds,
    seconds => new IntTime(seconds)
);