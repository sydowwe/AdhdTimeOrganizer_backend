using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.Common.domain.helper;

public class MyIntTime
{
    [JsonConstructor]
    public MyIntTime(int hours, int minutes, int seconds)
    {
        Hours = hours;
        Minutes = minutes;
        Seconds = seconds;
    }

    public MyIntTime(int seconds)
    {
        Hours = seconds / 3600;
        Minutes = seconds % 3600 / 60;
        Seconds = seconds % 60;
    }

    public int Hours { get; }
    public int Minutes { get; }
    public int Seconds { get; }

    public int GetInSeconds()
    {
        return Hours * 3600 + Minutes * 60 + Seconds;
    }
}