using System;
using System.Collections.Generic;
using System.Linq;

namespace wintabs.Utilities
{
    internal static class Collections
    {
        public static (IEnumerable<T> addList, IEnumerable<T> removeList, IEnumerable<T> persistList) Changes<T>(this IEnumerable<T> enumerable, IEnumerable<T> newEnumerable)
        {
            return enumerable.Changes(newEnumerable, EqualityComparer<T>.Default);
        }

        public static (IEnumerable<T> addList, IEnumerable<T> removeList, IEnumerable<T> persistList) Changes<T>(this IEnumerable<T> enumerable, IEnumerable<T> newEnumerable, IEqualityComparer<T> equalityComparer)
        {
            return (
                addList: newEnumerable.Except(enumerable, equalityComparer),
                removeList: enumerable.Except(newEnumerable, equalityComparer),
                persistList: enumerable.Intersect(newEnumerable, equalityComparer));
        }

        public static int IndexOf<T>(this IReadOnlyList<T> list, T value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Equals(value, list[i]))
                {
                    return i;
                }
            }
            return -1;
        }

        public static int IndexOf<T>(this IReadOnlyList<T> list, Predicate<T> pred)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (pred(list[i]))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
