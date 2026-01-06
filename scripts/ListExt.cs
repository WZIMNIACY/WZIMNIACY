using System;
using System.Collections.Generic;

public static class ListExt
{
    /// <summary>
    /// Removes the element at the specified index by replacing it with the last element in the list.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="list">The list to modify.</param>
    /// <param name="i">The zero-based index of the element to remove.</param>
    /// <remarks>
    /// <para>
    /// This method performs the removal in <b>O(1)</b> time, regardless of the list's size or the index being removed.
    /// Standard <see cref="List{T}.RemoveAt(int)"/> is O(N) because it shifts all subsequent elements.
    /// </para>
    /// <para>
    /// <b>Warning:</b> This method does not preserve the order of elements in the list.
    /// Do not use this if the list must remain sorted or ordered.
    /// </para>
    /// </remarks>
    public static void SwapRemove<T>(this List<T> list, int i)
    {
        if (i >= list.Count)
        {
            throw new IndexOutOfRangeException();
        }

        var lastIndex = list.Count - 1;
        T lastCard = list[lastIndex];
        list[i] = lastCard;
        list.RemoveAt(lastIndex);
    }
}
