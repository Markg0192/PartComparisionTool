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
                differences.Add($"Material differs: target {Display(target.MainMaterial)}, candidate {Display(candidate.MainMaterial)}.");

            if (SameText(target.MainFinish, candidate.MainFinish))
                score += 5;
            else
                differences.Add($"Finish differs: target {Display(target.MainFinish)}, candidate {Display(candidate.MainFinish)}.");

            var mainGeometryEqual = SameText(target.MainGeometryKey, candidate.MainGeometryKey)
                                    && !string.IsNullOrEmpty(target.MainGeometryKey);

            if (mainGeometryEqual)
                score += 10;
            else
                differences.Add("Main member cuts or shape differ.");

            var weightDifference = candidate.Weight - target.Weight;
            var absoluteWeightDifference = Math.Abs(weightDifference);

            if (absoluteWeightDifference <= 2.0)
                score += 10;
            else if (absoluteWeightDifference <= 10.0)
                score += 7;
            else if (absoluteWeightDifference <= 25.0)
                score += 4;

            if (absoluteWeightDifference > 2.0)
            {
                differences.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Candidate is {0:0.0} kg {1}.",
                    absoluteWeightDifference,
                    weightDifference < 0 ? "lighter" : "heavier"));
            }

            if (target.SecondaryParts.Count == candidate.SecondaryParts.Count)
                score += 5;

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
                AddPartDifferenceSummary(differences, target.SecondaryParts, candidate.SecondaryParts);
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
                AddBoltDifferenceSummary(differences, target.Bolts, candidate.Bolts);
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
                AddWeldDifferenceSummary(differences, target.Welds, candidate.Welds);
            }

            score = Math.Max(0, Math.Min(100, score));

            var exact = Math.Abs(target.MainLength - candidate.MainLength) <= ExactLengthTolerance
                        && SameText(target.MainProfile, candidate.MainProfile)
                        && SameText(target.MainMaterial, candidate.MainMaterial)
                        && SameText(target.MainFinish, candidate.MainFinish)
                        && mainGeometryEqual
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
                Differences = exact
                    ? "No differences found. Profile, length, material, finish, fittings, holes/bolts and welds all match."
                    : string.Join(" ", differences.Distinct()),
                MainPart = candidate.MainPart
            };
        }

        private static void AddPartDifferenceSummary(
            ICollection<string> differences,
            IList<PartSnapshot> target,
            IList<PartSnapshot> candidate)
        {
            var missing = GetPartDifference(target, candidate);
            var extra = GetPartDifference(candidate, target);

            if (missing.Count > 0)
                differences.Add(FormatPartList("Missing", missing));

            if (extra.Count > 0)
                differences.Add(FormatPartList("Extra", extra));

            if (missing.Count == 0 && extra.Count == 0)
                differences.Add("Fittings are the same sizes, but at least one fitting is in a different position or has different cuts/shape.");
        }

        private static List<PartSnapshot> GetPartDifference(
            IEnumerable<PartSnapshot> source,
            IEnumerable<PartSnapshot> subtract)
        {
            var remaining = source.ToList();

            foreach (var item in subtract)
            {
                var key = SimplePartKey(item);
                var index = remaining.FindIndex(x => SameText(SimplePartKey(x), key));
                if (index >= 0)
                    remaining.RemoveAt(index);
            }

            return remaining;
        }

        private static string FormatPartList(string prefix, IList<PartSnapshot> parts)
        {
            var descriptions = parts
                .Take(3)
                .Select(DescribePart)
                .ToList();

            var more = parts.Count > 3 ? $" plus {parts.Count - 3} more" : string.Empty;
            var noun = parts.Count == 1 ? "fitting" : "fittings";

            return $"{prefix} {parts.Count} {noun}: {string.Join(", ", descriptions)}{more}.";
        }

        private static string DescribePart(PartSnapshot part)
        {
            var description = string.Format(
                CultureInfo.InvariantCulture,
                "{0}, {1:0} mm long",
                string.IsNullOrWhiteSpace(part.Profile) ? "part" : part.Profile,
                part.Length);

            if (!string.IsNullOrWhiteSpace(part.Material))
                description += $", {part.Material}";

            if (!string.IsNullOrWhiteSpace(part.Finish))
                description += $", finish {part.Finish}";

            return description;
        }

        private static void AddBoltDifferenceSummary(
            ICollection<string> differences,
            IList<BoltSnapshot> target,
            IList<BoltSnapshot> candidate)
        {
            var targetDetailKeys = target.Select(BoltDetailKey).OrderBy(x => x).ToList();
            var candidateDetailKeys = candidate.Select(BoltDetailKey).OrderBy(x => x).ToList();

            if (target.Count == candidate.Count && targetDetailKeys.SequenceEqual(candidateDetailKeys))
            {
                var example = target.FirstOrDefault();
                differences.Add(example == null
                    ? "Hole/bolt positions differ."
                    : $"Hole/bolt positions differ ({DescribeBolt(example)})." );
                return;
            }

            var missing = GetBoltDifference(target, candidate);
            var extra = GetBoltDifference(candidate, target);

            if (missing.Count > 0)
                differences.Add(FormatBoltList("Missing", missing));

            if (extra.Count > 0)
                differences.Add(FormatBoltList("Extra/different", extra));

            if (missing.Count == 0 && extra.Count == 0)
                differences.Add("Hole/bolt sizes, type or spacing differ.");
        }

        private static List<BoltSnapshot> GetBoltDifference(
            IEnumerable<BoltSnapshot> source,
            IEnumerable<BoltSnapshot> subtract)
        {
            var remaining = source.ToList();

            foreach (var item in subtract)
            {
                var key = BoltDetailKey(item);
                var index = remaining.FindIndex(x => SameText(BoltDetailKey(x), key));
                if (index >= 0)
                    remaining.RemoveAt(index);
            }

            return remaining;
        }

        private static string FormatBoltList(string prefix, IList<BoltSnapshot> bolts)
        {
            var descriptions = bolts.Take(2).Select(DescribeBolt).ToList();
            var more = bolts.Count > 2 ? $" plus {bolts.Count - 2} more" : string.Empty;
            var noun = bolts.Count == 1 ? "group" : "groups";
            return $"{prefix} {bolts.Count} hole/bolt {noun}: {string.Join(", ", descriptions)}{more}.";
        }

        private static string DescribeBolt(BoltSnapshot bolt)
        {
            var holeSize = bolt.BoltSize + bolt.HoleTolerance;
            var description = bolt.HasBolt
                ? string.Format(CultureInfo.InvariantCulture, "{0} x M{1:0.#} bolts (Ø{2:0.#} holes)", bolt.Count, bolt.BoltSize, holeSize)
                : string.Format(CultureInfo.InvariantCulture, "{0} x Ø{1:0.#} holes", bolt.Count, holeSize);

            if (bolt.SlotX > 0.0 || bolt.SlotY > 0.0)
            {
                description += string.Format(
                    CultureInfo.InvariantCulture,
                    ", slot {0:0.#} x {1:0.#} mm",
                    bolt.SlotX,
                    bolt.SlotY);
            }

            return description;
        }

        private static string BoltDetailKey(BoltSnapshot bolt)
        {
            return string.Join("|",
                bolt.BoltSize.ToString("0.0", CultureInfo.InvariantCulture),
                bolt.HoleTolerance.ToString("0.0", CultureInfo.InvariantCulture),
                bolt.SlotX.ToString("0.0", CultureInfo.InvariantCulture),
                bolt.SlotY.ToString("0.0", CultureInfo.InvariantCulture),
                bolt.Standard ?? string.Empty,
                bolt.HoleType ?? string.Empty,
                bolt.HasBolt ? "BOLT" : "HOLE",
                bolt.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddWeldDifferenceSummary(
            ICollection<string> differences,
            IList<WeldSnapshot> target,
            IList<WeldSnapshot> candidate)
        {
            var targetDetails = target.Select(WeldDetailKey).OrderBy(x => x).ToList();
            var candidateDetails = candidate.Select(WeldDetailKey).OrderBy(x => x).ToList();

            if (target.Count == candidate.Count && targetDetails.SequenceEqual(candidateDetails))
            {
                differences.Add("Weld sizes/types match, but at least one weld is attached to a different fitting.");
                return;
            }

            var missing = GetWeldDifference(target, candidate);
            var extra = GetWeldDifference(candidate, target);

            if (missing.Count > 0)
                differences.Add(FormatWeldList("Missing", missing));

            if (extra.Count > 0)
                differences.Add(FormatWeldList("Extra/different", extra));

            if (missing.Count == 0 && extra.Count == 0)
                differences.Add("Weld size, type or attachment differs.");
        }

        private static List<WeldSnapshot> GetWeldDifference(
            IEnumerable<WeldSnapshot> source,
            IEnumerable<WeldSnapshot> subtract)
        {
            var remaining = source.ToList();

            foreach (var item in subtract)
            {
                var key = WeldDetailKey(item);
                var index = remaining.FindIndex(x => SameText(WeldDetailKey(x), key));
                if (index >= 0)
                    remaining.RemoveAt(index);
            }

            return remaining;
        }

        private static string FormatWeldList(string prefix, IList<WeldSnapshot> welds)
        {
            var descriptions = welds.Take(2).Select(DescribeWeld).ToList();
            var more = welds.Count > 2 ? $" plus {welds.Count - 2} more" : string.Empty;
            var noun = welds.Count == 1 ? "weld" : "welds";
            return $"{prefix} {welds.Count} {noun}: {string.Join(", ", descriptions)}{more}.";
        }

        private static string DescribeWeld(WeldSnapshot weld)
        {
            var size = DescribeWeldSize(weld.SizeAbove, weld.SizeBelow);
            var type = CleanWeldType(!string.IsNullOrWhiteSpace(weld.TypeAbove) ? weld.TypeAbove : weld.TypeBelow);
            var location = weld.ShopWeld ? "shop" : "site";
            var around = weld.AroundWeld ? ", all around" : string.Empty;

            return $"{size} {type} {location} weld{around}".Trim();
        }

        private static string DescribeWeldSize(double above, double below)
        {
            if (above > 0.0 && below > 0.0 && Math.Abs(above - below) > 0.01)
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}/{1:0.#} mm", above, below);

            var size = Math.Max(above, below);
            return size > 0.0
                ? size.ToString("0.#", CultureInfo.InvariantCulture) + " mm"
                : "unsized";
        }

        private static string CleanWeldType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "weld";

            return value
                .Replace("WELD_TYPE_", string.Empty)
                .Replace("_", " ")
                .ToLowerInvariant();
        }

        private static string WeldDetailKey(WeldSnapshot weld)
        {
            return string.Join("|",
                weld.SizeAbove.ToString("0.0", CultureInfo.InvariantCulture),
                weld.SizeBelow.ToString("0.0", CultureInfo.InvariantCulture),
                weld.TypeAbove ?? string.Empty,
                weld.TypeBelow ?? string.Empty,
                weld.AroundWeld ? "AROUND" : "EDGE",
                weld.ShopWeld ? "SHOP" : "SITE");
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
            return string.Join("|",
                SimplePartKey(part),
                part.GeometryKey ?? string.Empty);
        }

        private static string SimplePartKey(PartSnapshot part)
        {
            return string.Join("|",
                part.Profile ?? string.Empty,
                part.Material ?? string.Empty,
                part.Finish ?? string.Empty,
                part.Length.ToString("0.0", CultureInfo.InvariantCulture));
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
