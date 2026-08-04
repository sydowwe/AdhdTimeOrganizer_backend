using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sydowwe.Framework.domain.valueObject;

namespace Sydowwe.Framework.infrastructure.persistence.converter;

public class NullableIntTimeConverter() : ValueConverter<IntTime?, int>(
    myIntTime => myIntTime == null ? 0 : myIntTime.TotalSeconds,
    seconds => new IntTime(seconds)
);