namespace SlidingWindow
{
    public class Calculation : ISlidingWindowCalculation
    {
        public decimal Volume { get; private set; }
        public int CurrentCount { get; private set; }

        public Calculation()
        {
            Volume = 0;
            CurrentCount = 0;
        }

        public void Add(ISlidingWindowItem item)
        {
            if (item is Trade trade)
            {
                Volume = (Volume * CurrentCount + trade.Volume) / ++CurrentCount;
            }
        }

        public void Add(ISlidingWindowCalculation windowCalculation)
        {
            if (windowCalculation is Calculation calculation)
            {
                int newCount = CurrentCount + calculation.CurrentCount;

                if (newCount <= 0) return;

                Volume = (Volume * CurrentCount + calculation.Volume * calculation.CurrentCount) / newCount;
                CurrentCount = newCount;
            }
        }

        public void Remove(ISlidingWindowCalculation windowCalculation)
        {
            if (windowCalculation is Calculation calculation)
            {
                int newCount = CurrentCount - calculation.CurrentCount;

                if (newCount <= 0)
                {
                    Volume = 0;
                    CurrentCount = 0;
                }
                else
                {
                    Volume = (Volume * CurrentCount - calculation.Volume * calculation.CurrentCount) / newCount;
                    CurrentCount = newCount;
                }
            }
        }
    }
}