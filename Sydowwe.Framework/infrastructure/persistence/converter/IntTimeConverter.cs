using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sydowwe.Framework.domain.valueObject;

namespace Sydowwe.Framework.infrastructure.persistence.converter;

public class IntTimeConverter() : ValueConverter<IntTime, int>(
    myIntTime => myIntTime.TotalSeconds,
    seconds => new IntTime(seconds)
);