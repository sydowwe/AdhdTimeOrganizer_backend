using AdhdTimeOrganizer.domain.helper;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdhdTimeOrganizer.infrastructure.persistence.converter;

public class MyIntTimeConverter() : ValueConverter<MyIntTime, int>(
    myIntTime => myIntTime.GetInSeconds(),
    seconds => new MyIntTime(seconds)
);