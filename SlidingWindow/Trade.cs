using System;

namespace SlidingWindow
{
    public class Trade : ISlidingWindowItem
    {
        public DateTime TimeStamp { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Volume => Price * Quantity;

        public static ISlidingWindowCalculation CreateCalculation()
        {
            return new Calculation();
        }
    }
}