using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.domain.helper;

public record IntTime
{
    [JsonConstructor]
    public IntTime(int hours, int minutes)
    {
        Hours = hours;
        Minutes = minutes;
    }

    public IntTime(int seconds)
    {
        Hours = seconds / 3600;
        Minutes = seconds % 3600 / 60;
    }

    public int Hours { get; }
    public int Minutes { get; }

    public int TotalSeconds => Hours * 3600 + Minutes * 60 ;

    // Addition operator
    public static IntTime operator +(IntTime left, IntTime right)
    {
        return new IntTime(left.TotalSeconds + right.TotalSeconds);
    }
    
    // Subtraction operator
    public static IntTime operator -(IntTime left, IntTime right)
    {
        return new IntTime(left.TotalSeconds - right.TotalSeconds);
    }
    
    // Greater than operator
    public static bool operator >(IntTime left, IntTime right)
    {
        return left.TotalSeconds > right.TotalSeconds;
    }
    
    // Less than operator
    public static bool operator <(IntTime left, IntTime right)
    {
        return left.TotalSeconds < right.TotalSeconds;
    }
    
    // Greater than or equal operator
    public static bool operator >=(IntTime left, IntTime right)
    {
        return left.TotalSeconds >= right.TotalSeconds;
    }
    
    // Less than or equal operator
    public static bool operator <=(IntTime left, IntTime right)
    {
        return left.TotalSeconds <= right.TotalSeconds;
    }
}

public static class MyIntTimeExtensions
{
    public static IntTime ToMyIntTime(this TimeSpan timeSpan)
    {
        return new IntTime(timeSpan.Hours, timeSpan.Minutes);
    }

    public static bool IsNullOrZero(this IntTime? time) => time is null || time.TotalSeconds == 0;
}