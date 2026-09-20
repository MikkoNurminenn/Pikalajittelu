using System;
using System.Collections.Generic;

namespace MikkoMods
{
    // Pure routing policy, independent of Unity. Actual transfers still go through
    // Valheim's inventory API, which decides capacity and item compatibility.
    public static class ConsolidationPlan
    {
        public struct Route
        {
            public int From;
            public int To;
            public Route(int from, int to) { From = from; To = to; }
        }

        public static List<Route> Create(int[] amounts, string[] stableIds)
        {
            if (amounts == null || stableIds == null || amounts.Length != stableIds.Length)
                throw new ArgumentException("Each chest needs a quantity and stable ID.");
            var order = new List<int>();
            for (int i = 0; i < amounts.Length; i++)
                if (amounts[i] > 0) order.Add(i);
            order.Sort(delegate(int a, int b)
            {
                int quantity = amounts[b].CompareTo(amounts[a]);
                return quantity != 0 ? quantity : StringComparer.Ordinal.Compare(stableIds[a], stableIds[b]);
            });
            var routes = new List<Route>();
            // Drain the smallest piles first. Overflow may go to the next preferred
            // chest, but never backwards. The ranking stays fixed for this run.
            for (int from = order.Count - 1; from > 0; from--)
                for (int to = 0; to < from; to++)
                    routes.Add(new Route(order[from], order[to]));
            return routes;
        }
    }
}
