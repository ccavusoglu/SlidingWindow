namespace SlidingWindow
{
    public interface ISlidingWindowCalculation
    {
        void Add(ISlidingWindowItem item);
        void Add(ISlidingWindowCalculation windowCalculation);
        void Remove(ISlidingWindowCalculation windowCalculation);
    }
}