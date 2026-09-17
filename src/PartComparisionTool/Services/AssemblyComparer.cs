using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PartComparisionTool.Models;

namespace PartComparisionTool.Services
{
    internal sealed class AssemblyComparer
    {
        private const double ExactLengthTolerance = 1.0;

        public ComparisonResult Compare(AssemblySnapshot target, AssemblySnapshot candidate)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            var differences = new List<string>();
            var score = 35; // Profile and length have already passed the fast filter.

            if (SameText(target.MainMaterial, candidate.MainMaterial))
                score += 5;
            else
                differences.Add($"material differs ({target.MainMaterial} vs {candidate.MainMaterial})");

            if (SameText(target.MainFinish, candidate.MainFinish))
                score += 5;
            else
                differences.Add($"finish differs ({Display(target.MainFinish)} vs {Display(candidate.MainFinish)})");

            if (SameText(target.MainGeometryKey, candidate.MainGeometryKey) && !string.IsNullOrEmpty(target.MainGeometryKey))
                score += 10;
            else
                differences.Add("main-part geometry/end cuts differ");

            var weightDifference = candidate.Weight - target.Weight;
            var absoluteWeightDifference = Math.Abs(weightDifference);
            if (absoluteWeightDifference <= 2.0)
                score += 10;
            else if (absoluteWeightDifference <= 10.0)
                score += 7;
            else if (absoluteWeightDifference <= 25.0)
                score += 4;

            if (absoluteWeightDifference > 2.0)
                differences.Add($"weight {Signed(weightDifference)} kg");

            if (target.SecondaryParts.Count == candidate.SecondaryParts.Count)
                score += 5;
            else
                differences.Add(DescribeCountDifference(target.SecondaryParts.Count, candidate.SecondaryParts.Count, "fitting/secondary"));

            var targetDetailedParts = target.SecondaryParts.Select(DetailedPartKey).OrderBy(x => x).ToList();
            var candidateDetailedParts = candidate.SecondaryParts.Select(DetailedPartKey).OrderBy(x => x).ToList();
            var detailedPartsEqual = targetDetailedParts.SequenceEqual(candidateDetailedParts);

            if (detailedPartsEqual)
            {
                score += 10;
            }
            else
            {
                var targetSimpleParts = target.SecondaryParts.Select(SimplePartKey).ToList();
                var candidateSimpleParts = candidate.SecondaryParts.Select(SimplePartKey).ToList();
                var simpleSimilarity = MultisetSimilarity(targetSimpleParts, candidateSimpleParts);
                score += (int)Math.Round(simpleSimilarity * 8.0);
                AddPartDifferenceSummary(differences, targetSimpleParts, candidateSimpleParts);
            }

            var targetBolts = target.Bolts.Select(x => x.Signature).OrderBy(x => x).ToList();
            var candidateBolts = candidate.Bolts.Select(x => x.Signature).OrderBy(x => x).ToList();
            var boltsEqual = targetBolts.SequenceEqual(candidateBolts);

            if (boltsEqual)
            {
                score += 10;
            }
            else
            {
                var boltSimilarity = MultisetSimilarity(targetBolts, candidateBolts);
                score += (int)Math.Round(boltSimilarity * 5.0);
                differences.Add(DescribeCountOrPatternDifference(target.Bolts.Count, candidate.Bolts.Count, "bolt/hole group"));
            }

            var targetWelds = target.Welds.Select(x => x.Signature).OrderBy(x => x).ToList();
            var candidateWelds = candidate.Welds.Select(x => x.Signature).OrderBy(x => x).ToList();
            var weldsEqual = targetWelds.SequenceEqual(candidateWelds);

            if (weldsEqual)
            {
                score += 10;
            }
            else
            {
                var weldSimilarity = MultisetSimilarity(targetWelds, candidateWelds);
                score += (int)Math.Round(weldSimilarity * 5.0);
                differences.Add(DescribeCountOrPatternDifference(target.Welds.Count, candidate.Welds.Count, "weld"));
            }

            score = Math.Max(0, Math.Min(100, score));

            var exact = Math.Abs(target.MainLength - candidate.MainLength) <= ExactLengthTolerance
                        && SameText(target.MainProfile, candidate.MainProfile)
                        && SameText(target.MainMaterial, candidate.MainMaterial)
                        && SameText(target.MainFinish, candidate.MainFinish)
                        && SameText(target.MainGeometryKey, candidate.MainGeometryKey)
                        && detailedPartsEqual
                        && boltsEqual
                        && weldsEqual
                        && absoluteWeightDifference <= 2.0;

            var quality = exact
                ? MatchQuality.Exact
                : score >= 85
                    ? MatchQuality.VeryClose
                    : score >= 70
                        ? MatchQuality.Close
                        : MatchQuality.Possible;

            if (exact)
                differences.Clear();

            return new ComparisonResult
            {
                Quality = quality,
                Score = exact ? 100 : score,
                Phase = candidate.Phase,
                AssemblyMark = candidate.AssemblyMark,
                MainPartMark = candidate.MainPartMark,
                Profile = candidate.MainProfile,
                Length = candidate.MainLength,
                Weight = candidate.Weight,
                WeightDifference = weightDifference,
                FittingCount = candidate.SecondaryParts.Count,
                Differences = exact ? "No differences found by the comparison rules." : string.Join("; ", differences.Distinct()),
                MainPart = candidate.MainPart
            };
        }

        private static void AddPartDifferenceSummary(List<string> differences, IList<string> target, IList<string> candidate)
        {
            var missing = GetMultisetDifference(target, candidate);
            var extra = GetMultisetDifference(candidate, target);

            if (missing.Count > 0)
                differences.Add($"missing {missing.Count} fitting(s): {string.Join(", ", missing.Take(3))}");

            if (extra.Count > 0)
                differences.Add($"has {extra.Count} additional/different fitting(s): {string.Join(", ", extra.Take(3))}");

            if (missing.Count == 0 && extra.Count == 0)
                differences.Add("fitting geometry/position differs");
        }

        private static List<string> GetMultisetDifference(IEnumerable<string> source, IEnumerable<string> subtract)
        {
            var remaining = source.ToList();
            foreach (var item in subtract)
            {
                var index = remaining.FindIndex(x => SameText(x, item));
                if (index >= 0)
                    remaining.RemoveAt(index);
            }

            return remaining;
        }

        private static double MultisetSimilarity(IList<string> first, IList<string> second)
        {
            if (first.Count == 0 && second.Count == 0)
                return 1.0;

            var remaining = second.ToList();
            var matches = 0;

            foreach (var item in first)
            {
                var index = remaining.FindIndex(x => SameText(x, item));
                if (index < 0)
                    continue;

                matches++;
                remaining.RemoveAt(index);
            }

            return (2.0 * matches) / Math.Max(1, first.Count + second.Count);
        }

        private static string DetailedPartKey(PartSnapshot part)
        {
            return string.Join("|", SimplePartKey(part), part.GeometryKey ?? string.Empty);
        }

        private static string SimplePartKey(PartSnapshot part)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} x {1:0}mm",
                string.IsNullOrWhiteSpace(part.Profile) ? "part" : part.Profile,
                part.Length);
        }

        private static string DescribeCountDifference(int targetCount, int candidateCount, string noun)
        {
            var difference = candidateCount - targetCount;
            if (difference < 0)
                return $"{Math.Abs(difference)} fewer {noun}(s)";
            if (difference > 0)
                return $"{difference} extra {noun}(s)";
            return $"{noun} details differ";
        }

        private static string DescribeCountOrPatternDifference(int targetCount, int candidateCount, string noun)
        {
            if (targetCount != candidateCount)
                return DescribeCountDifference(targetCount, candidateCount, noun);

            return $"{noun} size/type/spacing differs";
        }

        private static string Signed(double value)
        {
            return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
        }

        private static string Display(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "blank" : value;
        }

        private static bool SameText(string first, string second)
        {
            return string.Equals(first ?? string.Empty, second ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
