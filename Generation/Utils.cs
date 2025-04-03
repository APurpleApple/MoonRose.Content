using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoonRose.Content
{
    public static class Utils
    {
        public static List<T> TopologicalSort<T>(IEnumerable<(T, string, List<string>)> values)
        {
            Dictionary<string, (T, List<string>, bool)> nodes = new();
            foreach (var item in values)
            {
                nodes.Add(item.Item2, (item.Item1, item.Item3, false));
            }
            List<T> result = new();

            while(nodes.Count > 0)
            {
                Visit(nodes.First().Key);
            }

            void Visit(string node)
            {
                if (!nodes.TryGetValue(node, out var v))
                {
                    return;
                }
                if (v.Item3)
                {
                    throw new Exception("Circle Dependency Encountered !");
                }

                v.Item3 = true;

                foreach (var edge in v.Item2)
                {
                    Visit(edge);
                }

                nodes.Remove(node);
                result.Add(v.Item1);
            }

            return result;
        }
    }
}
