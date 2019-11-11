using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SlidingWindow.Tests")]

namespace SlidingWindow
{
    public class SlidingWindow<T> where T : ISlidingWindowItem
    {
        private readonly int bucketInterval;
        private readonly TimeSpan windowLength;
        private readonly TimeSpan internalWindowLength;
        private readonly LinkedList<T> itemsList;
        private readonly LinkedList<Bucket> bucketsList;
        private readonly Dictionary<long, Bucket> buckets;
        private readonly Func<ISlidingWindowCalculation> calculationProvider;
        private readonly Window currentWindow;
        private readonly object addLock = new object();
        private readonly IDateTimeProvider dateTimeProvider;

        public SlidingWindow(TimeSpan windowLength, int bucketInterval, Func<ISlidingWindowCalculation> calculationProvider) :
            this(windowLength, bucketInterval, calculationProvider, new InternalDateTimeProvider())
        {
        }

        public SlidingWindow(TimeSpan windowLength, int bucketInterval, Func<ISlidingWindowCalculation> calculationProvider,
            IDateTimeProvider dateTimeProvider)
        {
            if (windowLength.TotalMilliseconds <= 0 || windowLength.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(windowLength));

            this.windowLength = windowLength;
            // Should be able to calculate a window for the time: 'Now - WindowLength'
            internalWindowLength = windowLength.Multiply(2);
            this.bucketInterval = bucketInterval;

            itemsList = new LinkedList<T>();
            buckets = new Dictionary<long, Bucket>();
            bucketsList = new LinkedList<Bucket>();
            this.calculationProvider = calculationProvider;
            this.dateTimeProvider = dateTimeProvider;
            currentWindow = new Window(calculationProvider());
        }

        public void Add(T item)
        {
            lock (addLock)
            {
                var lastItem = itemsList.Last;

                // TODO: Should assume all items are added in incremental order in time. Otherwise do what?
                if (lastItem?.Value.TimeStamp > item.TimeStamp)
                    throw new ArgumentException("Item to add is older than existing Items. " +
                                                $"Item's TimeStamp: {item.TimeStamp.Ticks} " +
                                                $"Newest Item's TimeStamp: {lastItem.Value.TimeStamp.Ticks}");

                var node = itemsList.AddLast(item);
                var bucket = AddToBucket(item, node);

                // Add to corresponding Bucket and CurrentWindow individually
                // Removing will occur for Buckets but adding should be for individual items
                // Check and clear old records when adding
                currentWindow.AddItem(item, bucket);
                var now = dateTimeProvider.Now;
                currentWindow.Update(GetWindowStartTimeStamp(now), GetTimeStampKey(now), bucketsList);
                CleanUpBuckets();
            }
        }

        // for internal tests
        internal (LinkedList<Bucket>, IDictionary<long, Bucket>) GetBuckets()
        {
            return (bucketsList, buckets);
        }

        public ISlidingWindowCalculation GetCurrentWindowCalculation()
        {
            var now = dateTimeProvider.Now;
            currentWindow.Update(GetWindowStartTimeStamp(now), GetTimeStampKey(now), bucketsList);

            return currentWindow.Calculation;
        }

        public ISlidingWindowCalculation GetCalculationFor(DateTime timeStamp)
        {
            if (dateTimeProvider.Now - timeStamp > windowLength)
                throw new ArgumentOutOfRangeException(nameof(timeStamp));

            // Use CurrentWindow as base. Later, cache the Windows generated and use the one covering most of the requested frame.
            var window = new Window(calculationProvider());
            window.Clone(currentWindow);
            window.Update(GetWindowStartTimeStamp(timeStamp), GetTimeStampKey(timeStamp), bucketsList);

            return window.Calculation;
        }

        public long GetTimeStampKey(DateTime timeStamp)
        {
            var milliseconds = timeStamp.Ticks / TimeSpan.TicksPerMillisecond;
            var remainder = milliseconds % bucketInterval;

            return milliseconds - remainder;
        }

        public long GetWindowStartTimeStamp(DateTime endTimeStamp)
        {
            var startTimeStamp = GetTimeStampKey(endTimeStamp) - (long) windowLength.TotalMilliseconds;
            var remainder = startTimeStamp % bucketInterval;

            return startTimeStamp - remainder;
        }

        private LinkedListNode<Bucket> AddToBucket(T item, LinkedListNode<T> node)
        {
            LinkedListNode<Bucket> bucketNode;

            if (!TryGetBucket(item, out var bucket))
            {
                var key = GetTimeStampKey(item.TimeStamp);
                bucket = new Bucket(key, calculationProvider());
                buckets.Add(key, bucket);
                bucketNode = bucketsList.AddLast(bucket);
            }
            else if (bucketsList.Last.Value.Equals(bucket))
            {
                bucketNode = bucketsList.Last;
            }
            else
            {
                bucketNode = bucketsList.Find(bucket);
            }

            bucket.Add(node);

            return bucketNode;
        }

        private void CleanUpBuckets()
        {
            var bucketNode = bucketsList.First;
            var difference = GetTimeStampKey(dateTimeProvider.Now) - bucketNode.Value.TimeStampKey;

            while (difference > internalWindowLength.TotalMilliseconds)
            {
                buckets.Remove(bucketNode.Value.TimeStampKey);
                bucketsList.Remove(bucketNode.Value);

                bucketNode = bucketNode.Next;

                if (bucketNode == null) break;

                difference = GetTimeStampKey(dateTimeProvider.Now) - bucketNode.Value.TimeStampKey;
            }
        }

        private bool TryGetBucket(T item, out Bucket bucket)
        {
            var key = GetTimeStampKey(item.TimeStamp);

            return buckets.TryGetValue(key, out bucket);
        }

        internal class Bucket
        {
            public readonly long TimeStampKey;
            internal ISlidingWindowCalculation Calculation;

            internal Bucket(long timeStampKey, ISlidingWindowCalculation calculation)
            {
                Calculation = calculation;
                TimeStampKey = timeStampKey;
            }

            internal void Add(LinkedListNode<T> node)
            {
                Calculation.Add(node.Value);
            }

            public override string ToString()
            {
                return $"[key: {TimeStampKey}], [calculation: {Calculation}]";
            }
        }

        private class Window
        {
            internal readonly ISlidingWindowCalculation Calculation;
            private LinkedListNode<Bucket>? firstBucketNode;
            private LinkedListNode<Bucket>? lastBucketNode;

            internal Window(ISlidingWindowCalculation calculation)
            {
                Calculation = calculation;
            }

            private void AddBucket(LinkedListNode<Bucket> node)
            {
                Calculation.Add(node.Value.Calculation);
                SetFirstAndLastNodes(node);
            }

            internal void AddItem(T item, LinkedListNode<Bucket> node)
            {
                Calculation.Add(item);
                SetFirstAndLastNodes(node);
            }

            // Bucket at 'startTimeStamp' is inclusive
            internal void Update(long startTimeStamp, long endTimeStamp, LinkedList<Bucket> buckets)
            {
                var bucketNode = buckets.First;

                // Shift Right if necessary
                if (bucketNode != null)
                {
                    // Check only Buckets contained in the Window
                    while (bucketNode.Value.TimeStampKey >= firstBucketNode?.Value.TimeStampKey &&
                           bucketNode.Value.TimeStampKey < startTimeStamp)
                    {
                        // Bucket is older than window interval
                        Remove(bucketNode.Value);
                        bucketNode = bucketNode.Next;
                        firstBucketNode = bucketNode;

                        if (bucketNode == null) break;
                    }
                }

                // Shift the first item to the right
                if (firstBucketNode != null)
                {
                    while (firstBucketNode.Value.TimeStampKey < startTimeStamp)
                    {
                        Remove(firstBucketNode.Value);
                        firstBucketNode = firstBucketNode.Next;

                        if (firstBucketNode == null) break;
                    }
                }

                bucketNode = buckets.Last;

                // Shift Left if necessary, 'lastBucketNode' adjusted here
                while (bucketNode != null && bucketNode.Value.TimeStampKey > endTimeStamp)
                {
                    if (bucketNode.Value.TimeStampKey <= lastBucketNode?.Value.TimeStampKey)
                    {
                        Remove(bucketNode.Value);
                        lastBucketNode = bucketNode.Previous;
                    }

                    bucketNode = bucketNode.Previous;
                }

                // Shift Left and Adjust 'firstBucketNode'
                if (firstBucketNode != null)
                {
                    bucketNode = firstBucketNode.Previous;

                    while (bucketNode != null && bucketNode.Value.TimeStampKey >= startTimeStamp)
                    {
                        AddBucket(bucketNode);
                        firstBucketNode = bucketNode;
                        bucketNode = bucketNode.Previous;
                    }
                }
            }

            private void Remove(Bucket bucket)
            {
                Calculation.Remove(bucket.Calculation);
            }

            private void SetFirstAndLastNodes(LinkedListNode<Bucket> node)
            {
                if (firstBucketNode == null) firstBucketNode = node;
                lastBucketNode = node;
            }

            internal void Clone(Window currentWindow)
            {
                Calculation.Add(currentWindow.Calculation);
                firstBucketNode = currentWindow.firstBucketNode;
                lastBucketNode = currentWindow.lastBucketNode;
            }
        }

        private class InternalDateTimeProvider : IDateTimeProvider
        {
            public DateTime Now => DateTime.Now;
        }
    }
}