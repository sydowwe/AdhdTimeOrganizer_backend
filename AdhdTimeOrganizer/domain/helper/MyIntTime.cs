using System.Text.Json.Serialization;

namespace AdhdTimeOrganizer.domain.helper;

public record MyIntTime
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

    public int GetInSeconds => Hours * 3600 + Minutes * 60 + Seconds;

    // Addition operator
    public static MyIntTime operator +(MyIntTime left, MyIntTime right)
    {
        return new MyIntTime(left.GetInSeconds + right.GetInSeconds);
    }
    
    // Subtraction operator
    public static MyIntTime operator -(MyIntTime left, MyIntTime right)
    {
        return new MyIntTime(left.GetInSeconds - right.GetInSeconds);
    }
    
    // Greater than operator
    public static bool operator >(MyIntTime left, MyIntTime right)
    {
        return left.GetInSeconds > right.GetInSeconds;
    }
    
    // Less than operator
    public static bool operator <(MyIntTime left, MyIntTime right)
    {
        return left.GetInSeconds < right.GetInSeconds;
    }
    
    // Greater than or equal operator
    public static bool operator >=(MyIntTime left, MyIntTime right)
    {
        return left.GetInSeconds >= right.GetInSeconds;
    }
    
    // Less than or equal operator
    public static bool operator <=(MyIntTime left, MyIntTime right)
    {
        return left.GetInSeconds <= right.GetInSeconds;
    }
}

public static class MyIntTimeExtensions
{
    public static MyIntTime ToMyIntTime(this TimeSpan timeSpan)
    {
        return new MyIntTime(timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
    }

    public static bool IsNullOrZero(this MyIntTime? time) => time is null || time.GetInSeconds == 0;
}