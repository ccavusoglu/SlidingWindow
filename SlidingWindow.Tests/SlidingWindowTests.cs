using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlidingWindow.Tests
{
    public class SlidingWindowTests
    {
        private readonly TestDateTimeProvider testDateTimeProvider = new TestDateTimeProvider();

        [Theory]
        [InlineData(-2)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void SlidingWindow_InvalidWindowLength_ThrowsException(int bucketInterval)
        {
            // Arrange
            var timeSpan = TimeSpan.FromMilliseconds(bucketInterval + 1);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingWindow<Trade>(timeSpan, 10, Trade.CreateCalculation));
        }

        [Fact]
        public void SlidingWindow_IfShiftToLeft_AdjustsCorrectly()
        {
            // Arrange
            var trades = CreateTradeItemsList(new[] {-1000, -900, -800, -700, -600, -500, -400, -300, -200, -100, 0},
                new[] {1000, 900, 800, 700, 600, 500, 400, 300, 200, 100, 0});

            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(500), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var trade in trades)
            {
                testDateTimeProvider.Now = trade.TimeStamp;
                window.Add(trade);
            }

            var currentCalculation = window.GetCurrentWindowCalculation() as Calculation;
            var shiftLeft = window.GetCalculationFor(testDateTimeProvider.Now.AddMilliseconds(-250)) as Calculation;

            // Assert
            Assert.Equal((250, 6), (currentCalculation.Volume, currentCalculation.CurrentCount));
            Assert.Equal((2500m / 5, 5), (shiftLeft.Volume, shiftLeft.CurrentCount));
        }

        [Theory]
        [InlineData(0, 250, 6)]
        [InlineData(10, 200, 5)]
        [InlineData(100, 200, 5)]
        [InlineData(101, 200, 5)]
        [InlineData(110, 150, 4)]
        [InlineData(500, 0, 1)]
        [InlineData(501, 0, 1)]
        [InlineData(510, 0, 0)]
        public void SlidingWindow_IfShiftToRight_AdjustsCorrectly(int shift, int expectedVolume, int expectedCount)
        {
            // Arrange
            var items = CreateTradeItemsList(new[] {-1000, -900, -800, -700, -600, -500, -400, -300, -200, -100, 0},
                new[] {1000, 900, 800, 700, 600, 500, 400, 300, 200, 100, 0});

            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(500), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            var currentCalculation = window.GetCurrentWindowCalculation() as Calculation;
            var shiftRight = window.GetCalculationFor(testDateTimeProvider.Now.AddMilliseconds(shift)) as Calculation;

            // Assert
            Assert.Equal((1500m / 6, 6), (currentCalculation.Volume, currentCalculation.CurrentCount));
            Assert.Equal((expectedVolume, expectedCount), (shiftRight.Volume, shiftRight.CurrentCount));
        }

        [Theory]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -25, 0}, new[] {100, 50, 10}}, 25, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -31, 0}, new[] {100, 50, 10}}, 25, 10, 10, 1)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -19, 0}, new[] {100, 50, 10}}, 25, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -31, 0}, new[] {100, 50, 10}}, 31, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -32, 0}, new[] {100, 50, 10}}, 31, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -39, 0}, new[] {100, 50, 10}}, 31, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -29, 0}, new[] {100, 50, 10}}, 31, 10, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -900, 0}, new[] {90, 50, 10}}, 900, 100, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -900, 0}, new[] {90, 50, 10}}, 901, 100, 50, 3)]
        public void SlidingWindow_WhenWindowLengthIsNotAFactorOfBucketInterval_AdjustsCorrectly(
            Trade[] items, int windowLengthInMs, int bucketInterval, decimal expectedVolume, decimal expectedCount)
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(windowLengthInMs), bucketInterval, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            var calculation = window.GetCurrentWindowCalculation() as Calculation;

            // Assert
            Assert.Equal((expectedVolume, expectedCount), (calculation.Volume, calculation.CurrentCount));
        }

        [Fact]
        public void Add_WhenBucketsAreOlderThanInterval_ShouldRemoveOlderBuckets()
        {
            // Arrange
            var windowInterval = TimeSpan.FromMilliseconds(500);
            var items = CreateTradeItemsList(new[] {-2000, -1000, 0}, new[] {200, 100, 10});
            var window = new SlidingWindow<Trade>(windowInterval, 500, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            var buckets = window.GetBuckets().Item1;
            var expectedOldestTimeStampKey = window.GetTimeStampKey(testDateTimeProvider.Now.Add(-windowInterval * 2));

            // Assert
            Assert.True(buckets.All(b => b.TimeStampKey >= expectedOldestTimeStampKey));
        }

        [Theory]
        [InlineData(10, -11)]
        [InlineData(100, -110)]
        [InlineData(1000, -1100)]
        public void Add_IfTimestampsDifferMoreThanBucketInterval_ShouldCreateNewBucket(int bucketInterval, int shift)
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMinutes(1), bucketInterval, Trade.CreateCalculation);
            var olderItem = CreateTradeItem(DateTime.Now.AddMilliseconds(shift));
            var newerItem = CreateTradeItem(DateTime.Now);

            // Act
            window.Add(olderItem);
            window.Add(newerItem);

            // Assert
            var buckets = window.GetBuckets().Item2;
            var firstBucket = buckets[window.GetTimeStampKey(olderItem.TimeStamp)];
            var secondBucket = buckets[window.GetTimeStampKey(newerItem.TimeStamp)];

            Assert.NotEqual(firstBucket, secondBucket);
        }

        [Fact]
        public void Add_WhenItemIsNotInsideOfWindowRangeButItsBucketIs_ShouldIncludeTheBucketInWindow()
        {
            // Arrange
            var items = CreateTradeItemsList(new[] {0, 990}, new[] {10, 20});
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(999), 100, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            testDateTimeProvider.Now = items[0].TimeStamp;
            window.Add(items[0]);
            testDateTimeProvider.Now = items[1].TimeStamp;
            window.Add(items[1]);
            var calculation = window.GetCalculationFor(items[0].TimeStamp.AddMilliseconds(900)) as Calculation;

            // Assert
            Assert.Equal((15, 2), (calculation.Volume, calculation.CurrentCount));
        }

        [Fact]
        public void Add_WhenFirstBucketShiftedOutsideOfTheWindow_ShouldRemoveTheBucketFromWindow()
        {
            // Arrange
            var items = CreateTradeItemsList(new[] {-90, 0}, new[] {10, 20});
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(100), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            testDateTimeProvider.Now = items[0].TimeStamp;
            window.Add(items[0]);
            testDateTimeProvider.Now = items[1].TimeStamp;
            window.Add(items[1]);
            testDateTimeProvider.Now = testDateTimeProvider.Now.AddMilliseconds(20);

            var calculationAfterSecondItem = window.GetCurrentWindowCalculation() as Calculation;

            // Assert
            Assert.Equal((items[1].Volume, 1), (calculationAfterSecondItem.Volume, calculationAfterSecondItem.CurrentCount));
        }

        [Fact]
        public void Add_NewItemAdded_ShouldCleanUpOldBuckets()
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(100), 1000, Trade.CreateCalculation, testDateTimeProvider);
            var olderItem = CreateTradeItem(DateTime.Now.AddMilliseconds(-1000));
            var newerItem = CreateTradeItem(DateTime.Now);

            // Act
            testDateTimeProvider.Now = olderItem.TimeStamp;
            window.Add(olderItem);
            testDateTimeProvider.Now = newerItem.TimeStamp;
            window.Add(newerItem);

            // Assert
            var bucketsList = window.GetBuckets().Item1;
            var buckets = window.GetBuckets().Item2;

            Assert.Single(buckets);
            Assert.Single(bucketsList);
            Assert.False(buckets.ContainsKey(window.GetTimeStampKey(olderItem.TimeStamp)));
            Assert.Equal(window.GetTimeStampKey(newerItem.TimeStamp), bucketsList.First.Value.TimeStampKey);
        }

        [Fact]
        public void Add_WhenItemsHaveUnorderedTimeStamps_ThrowsException()
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(100), 1000, Trade.CreateCalculation);
            var olderItem = CreateTradeItem(DateTime.Now.AddMilliseconds(-1000));
            var newerItem = CreateTradeItem(DateTime.Now);

            // Act
            window.Add(newerItem);

            // Assert
            Assert.Throws<ArgumentException>(() => window.Add(olderItem));
        }

        [Fact]
        public void GetCurrentCalculation_WhenNoItemInWindow_ShouldReturnEmpty()
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(100), 10, Trade.CreateCalculation);

            // Act
            var calculation = window.GetCurrentWindowCalculation() as Calculation;

            // Assert
            Assert.Equal((0, 0), (calculation.Volume, calculation.CurrentCount));
        }

        [Theory]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100}, new[] {10}}, 50, 0, 0)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -20}, new[] {10, 20}}, 15, 0, 0)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -20, -10}, new[] {10, 20, 111}}, 10, 0, 0)]
        public void GetCurrentCalculation_WhenNoItemInWindow_ShouldUpdateWindowAndReturnEmpty(
            Trade[] items, int windowLengthInMs, decimal expectedVolume, decimal expectedCount)
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(windowLengthInMs), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            testDateTimeProvider.Now = DateTime.Now;
            var calculation = window.GetCurrentWindowCalculation() as Calculation;

            // Assert
            Assert.Equal((expectedVolume, expectedCount), (calculation.Volume, calculation.CurrentCount));
        }

        [Theory]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {0, 0}, new[] {10, 10}}, 10, 10, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-11, 0}, new[] {10, 20}}, 10, 20, 1)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, 0}, new[] {10, 50}}, 1000, 30, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-9, 0}, new[] {10, 20}}, 10, 15, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-10, 0}, new[] {10, 20}}, 10, 15, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-11, 0}, new[] {10, 20}}, 10, 20, 1)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-100, -20, 0}, new[] {10, 20, 30}}, 50, 25, 2)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-5000, -1999, -1000, -500, 0}, new[] {10, 20, 30, 40, 50}}, 2000, 35, 4)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-5000, -2000, -1000, -500, 0}, new[] {10, 20, 30, 40, 50}}, 2000, 35, 4)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-5000, -2001, -1000, -500, 0}, new[] {10, 20, 30, 40, 50}}, 2000, 40, 3)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-5000, -1989, -1000, -500, 0}, new[] {10, 20, 30, 40, 50}}, 2000, 35, 4)]
        public void GetCurrentCalculation_WhenMultipleItemsAdded_ShouldUpdateWindowCorrectly(
            Trade[] items, int windowLengthInMs, decimal expectedVolume, decimal expectedCount)
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(windowLengthInMs), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            testDateTimeProvider.Now = items[^1].TimeStamp;

            var calculation = window.GetCurrentWindowCalculation() as Calculation;

            // Assert
            Assert.Equal((expectedVolume, expectedCount), (calculation.Volume, calculation.CurrentCount));
        }

        [Fact]
        public void GetCalculationFor_WhenNoItemInWindow_ShouldReturnEmpty()
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(100), 10, Trade.CreateCalculation);

            // Act
            var calculation = window.GetCalculationFor(DateTime.Now.AddMilliseconds(100)) as Calculation;

            // Assert
            Assert.Equal((0, 0), (calculation.Volume, calculation.CurrentCount));
        }

        [Fact]
        public void GetCalculationFor_IfTimeStampOlderThanWindowLength_ThrowsException()
        {
            // Arrange
            testDateTimeProvider.Now = DateTime.Now;

            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(5), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => window.GetCalculationFor(testDateTimeProvider.Now.AddMilliseconds(-6)));
        }

        [Theory]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -75, -50, -25, 0}, new[] {9, 6, 3, 4, 3, 2, 1}}, 1000, -100, 6, 3)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 5, 4, 3, 2, 1}}, 1000, -100, 6, 4)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 5, 4, 3, 2, 1}}, 1000, -1000, 9, 1)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 5, 4, 3, 2, 1}}, 2000, -1001, 0, 0)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 5, 4, 3, 2, 1}}, 2000, -1011, 0, 0)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 3, 4, 3, 2, 1}}, 1000, 0, 4, 7)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 6, 4, 3, 2, 1}}, 1000, -1, 5, 6)]
        [InlineMemberData(nameof(CreateTradeItems), new object[] {new[] {-1000, -500, -300, -100, -50, -25, 0}, new[] {9, 6, 2, 4, 3, 2, 1}}, 1000, 100, 3, 6)]
        public void GetCalculationFor_WhenMultipleItemsAdded_ShouldUpdateWindowCorrectly(
            Trade[] items, int windowLengthInMs, int calculationForTimeStamp, decimal expectedVolume, decimal expectedCount)
        {
            // Arrange
            var window = new SlidingWindow<Trade>(TimeSpan.FromMilliseconds(windowLengthInMs), 10, Trade.CreateCalculation, testDateTimeProvider);

            // Act
            foreach (var item in items)
            {
                testDateTimeProvider.Now = item.TimeStamp;
                window.Add(item);
            }

            var calculation = window.GetCalculationFor(testDateTimeProvider.Now.AddMilliseconds(calculationForTimeStamp)) as Calculation;

            // Assert
            Assert.Equal((expectedVolume, expectedCount), (calculation.Volume, calculation.CurrentCount));
        }

        public static IEnumerable<Trade[]> CreateTradeItems(int[] timeStampShiftsInMs, int[] prices)
        {
            return CreateTradeItems(timeStampShiftsInMs, prices, null);
        }

        public static IEnumerable<Trade[]> CreateTradeItems(int[] timeStampShiftsInMs, int[] prices, int[]? quantities)
        {
            var items = new List<Trade>();

            if (timeStampShiftsInMs.Length > 0)
            {
                var refTimeStamp = DateTime.Now;
                refTimeStamp = refTimeStamp.AddMilliseconds(-refTimeStamp.Millisecond); // get rid of fractional milliseconds for testing edge cases

                for (int i = 0; i < timeStampShiftsInMs.Length; i++)
                {
                    var timeStamp = refTimeStamp.AddMilliseconds(timeStampShiftsInMs[i]);

                    items.Add(new Trade
                    {
                        TimeStamp = timeStamp,
                        Price = prices[i],
                        Quantity = quantities?[i] ?? 1
                    });
                }
            }

            return new List<Trade[]>()
            {
                items.ToArray()
            };
        }

        private static Trade CreateTradeItem(DateTime timeStamp)
        {
            return new Trade()
            {
                TimeStamp = timeStamp,
                Price = new Random().Next(100),
                Quantity = new Random().Next(100)
            };
        }

        private static Trade[] CreateTradeItemsList(int[] timeStampShiftsInMs, int[] prices)
        {
            return CreateTradeItems(timeStampShiftsInMs, prices).First();
        }
    }

    internal class TestDateTimeProvider : IDateTimeProvider
    {
        public DateTime Now { get; set; }
    }
}