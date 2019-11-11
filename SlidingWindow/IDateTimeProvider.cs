using System;

namespace SlidingWindow
{
    public interface IDateTimeProvider
    {
        DateTime Now { get; }
    }
}