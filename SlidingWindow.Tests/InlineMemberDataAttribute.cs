using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace SlidingWindow.Tests
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineMemberDataAttribute : MemberDataAttributeBase
    {
        private readonly object[] otherParameters;

        public InlineMemberDataAttribute(string memberName, object[] memberParameters, params object[] otherParameters) : base(memberName, memberParameters)
        {
            this.otherParameters = otherParameters;
        }

        protected override object[] ConvertDataItem(MethodInfo testMethod, object item)
        {
            if (item == null)
                return new object[] { };

            if (!(item is object[] array))
            {
                throw new ArgumentException(
                    $"Property {MemberName} on {MemberType ?? testMethod.DeclaringType} yielded an item that is not an object[]");
            }

            var aggregatedArray = new List<object> {array};
            aggregatedArray.AddRange(otherParameters);

            return aggregatedArray.ToArray();
        }
    }
}