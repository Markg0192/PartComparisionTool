using System;
using System.Collections.Generic;
using System.Linq;

namespace PartComparisionTool.Services
{
    internal static class PhaseParser
    {
        public static HashSet<int> Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Enter at least one phase number.");

            var phases = new HashSet<int>();
            var tokens = text
                .Replace(";", ",")
                .Replace(" ", ",")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawToken in tokens)
            {
                var token = rawToken.Trim();
                if (token.Contains("-"))
                {
                    var range = token.Split('-');
                    int start;
                    int end;

                    if (range.Length != 2
                        || !int.TryParse(range[0], out start)
                        || !int.TryParse(range[1], out end)
                        || start <= 0
                        || end <= 0)
                    {
                        throw new InvalidOperationException($"'{token}' is not a valid phase or phase range.");
                    }

                    if (start > end)
                    {
                        var temp = start;
                        start = end;
                        end = temp;
                    }

                    for (var phase = start; phase <= end; phase++)
                        phases.Add(phase);

                    continue;
                }

                int value;
                if (!int.TryParse(token, out value) || value <= 0)
                    throw new InvalidOperationException($"'{token}' is not a valid phase number.");

                phases.Add(value);
            }

            if (phases.Count == 0)
                throw new InvalidOperationException("Enter at least one phase number.");

            return phases;
        }

        public static string Format(IEnumerable<int> phases)
        {
            return string.Join(", ", phases.OrderBy(x => x));
        }
    }
}
