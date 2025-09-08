using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.domain.helper;

public record MyIntTime
{
    [JsonConstructor]
    public MyIntTime(int hours, int minutes)
    {
        Hours = hours;
        Minutes = minutes;
    }

    public MyIntTime(int seconds)
    {
        Hours = seconds / 3600;
        Minutes = seconds % 3600 / 60;
    }

    public int Hours { get; }
    public int Minutes { get; }

    public int TotalSeconds => Hours * 3600 + Minutes * 60 ;

    // Addition operator
    public static MyIntTime operator +(MyIntTime left, MyIntTime right)
    {
        return new MyIntTime(left.TotalSeconds + right.TotalSeconds);
    }
    
    // Subtraction operator
    public static MyIntTime operator -(MyIntTime left, MyIntTime right)
    {
        return new MyIntTime(left.TotalSeconds - right.TotalSeconds);
    }
    
    // Greater than operator
    public static bool operator >(MyIntTime left, MyIntTime right)
    {
        return left.TotalSeconds > right.TotalSeconds;
    }
    
    // Less than operator
    public static bool operator <(MyIntTime left, MyIntTime right)
    {
        return left.TotalSeconds < right.TotalSeconds;
    }
    
    // Greater than or equal operator
    public static bool operator >=(MyIntTime left, MyIntTime right)
    {
        return left.TotalSeconds >= right.TotalSeconds;
    }
    
    // Less than or equal operator
    public static bool operator <=(MyIntTime left, MyIntTime right)
    {
        return left.TotalSeconds <= right.TotalSeconds;
    }
}

public static class MyIntTimeExtensions
{
    public static MyIntTime ToMyIntTime(this TimeSpan timeSpan)
    {
        return new MyIntTime(timeSpan.Hours, timeSpan.Minutes);
    }

    public static bool IsNullOrZero(this MyIntTime? time) => time is null || time.TotalSeconds == 0;
}