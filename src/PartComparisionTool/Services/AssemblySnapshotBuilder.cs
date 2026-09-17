using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PartComparisionTool.Models;
using Tekla.Structures.Geometry3d;
using Tekla.Structures.Model;

namespace PartComparisionTool.Services
{
    internal sealed class AssemblySnapshotBuilder
    {
        private const double GeometryRounding = 0.5;

        public AssemblySnapshot Build(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

            var mainPart = assembly.GetMainPart() as Part;
            if (mainPart == null)
                throw new InvalidOperationException("The selected assembly does not have a steel main part.");

            var toMain = MatrixFactory.ToCoordinateSystem(mainPart.GetCoordinateSystem());
            var secondaryParts = GetSecondaryParts(assembly);

            Phase phase;
            var phaseNumber = mainPart.GetPhase(out phase) ? phase.PhaseNumber : 0;

            var mainSnapshot = BuildPartSnapshot(mainPart, toMain);
            var snapshot = new AssemblySnapshot
            {
                MainPart = mainPart,
                Phase = phaseNumber,
                AssemblyMark = GetStringReportProperty(mainPart, "ASSEMBLY_POS"),
                MainPartMark = SafePartMark(mainPart),
                MainProfile = mainSnapshot.Profile,
                MainMaterial = mainSnapshot.Material,
                MainFinish = mainSnapshot.Finish,
                MainLength = mainSnapshot.Length,
                MainGeometryKey = mainSnapshot.GeometryKey,
                Weight = mainSnapshot.Weight
            };

            var partKeys = new Dictionary<int, string>
            {
                [mainPart.Identifier.ID] = GetPartComparisonKey(mainSnapshot)
            };

            foreach (var secondary in secondaryParts)
            {
                var secondarySnapshot = BuildPartSnapshot(secondary, toMain);
                snapshot.SecondaryParts.Add(secondarySnapshot);
                snapshot.Weight += secondarySnapshot.Weight;
                partKeys[secondary.Identifier.ID] = GetPartComparisonKey(secondarySnapshot);
            }

            var assemblyParts = new List<Part> { mainPart };
            assemblyParts.AddRange(secondaryParts);

            AddBolts(snapshot, assemblyParts, toMain);
            AddWelds(snapshot, assemblyParts, partKeys);

            snapshot.SecondaryParts.Sort((a, b) => string.CompareOrdinal(GetPartComparisonKey(a), GetPartComparisonKey(b)));
            snapshot.Bolts.Sort((a, b) => string.CompareOrdinal(a.Signature, b.Signature));
            snapshot.Welds.Sort((a, b) => string.CompareOrdinal(a.Signature, b.Signature));

            return snapshot;
        }

        public static double GetLength(Part part)
        {
            return GetDoubleReportProperty(part, "LENGTH");
        }

        public static string GetProfile(Part part)
        {
            return part != null && part.Profile != null
                ? part.Profile.ProfileString ?? string.Empty
                : string.Empty;
        }

        private static List<Part> GetSecondaryParts(Assembly assembly)
        {
            var result = new List<Part>();
            var secondaries = assembly.GetSecondaries();

            if (secondaries == null)
                return result;

            foreach (var item in secondaries)
            {
                var part = item as Part;
                if (part != null)
                    result.Add(part);
            }

            return result;
        }

        private static PartSnapshot BuildPartSnapshot(Part part, Matrix toMain)
        {
            return new PartSnapshot
            {
                Profile = GetProfile(part),
                Material = part != null && part.Material != null ? part.Material.MaterialString ?? string.Empty : string.Empty,
                Finish = part != null ? part.Finish ?? string.Empty : string.Empty,
                Length = GetDoubleReportProperty(part, "LENGTH"),
                Weight = GetDoubleReportProperty(part, "WEIGHT"),
                GeometryKey = BuildGeometryKey(part, toMain)
            };
        }

        private static void AddBolts(AssemblySnapshot snapshot, IEnumerable<Part> parts, Matrix toMain)
        {
            var seen = new HashSet<int>();

            foreach (var part in parts)
            {
                var bolts = part.GetBolts();
                if (bolts == null)
                    continue;

                while (bolts.MoveNext())
                {
                    var boltGroup = bolts.Current as BoltGroup;
                    if (boltGroup == null || !seen.Add(boltGroup.Identifier.ID))
                        continue;

                    var positions = new List<string>();
                    foreach (var item in boltGroup.BoltPositions)
                    {
                        var point = item as Point;
                        if (point != null)
                            positions.Add(PointKey(toMain.Transform(point)));
                    }

                    positions.Sort(StringComparer.Ordinal);

                    snapshot.Bolts.Add(new BoltSnapshot
                    {
                        BoltSize = boltGroup.BoltSize,
                        HoleTolerance = boltGroup.Tolerance,
                        SlotX = boltGroup.SlottedHoleX,
                        SlotY = boltGroup.SlottedHoleY,
                        Standard = boltGroup.BoltStandard ?? string.Empty,
                        HoleType = boltGroup.HoleType.ToString(),
                        HasBolt = boltGroup.Bolt,
                        Count = positions.Count,
                        PositionKey = string.Join(";", positions)
                    });
                }
            }
        }

        private static void AddWelds(
            AssemblySnapshot snapshot,
            IEnumerable<Part> parts,
            IDictionary<int, string> partKeys)
        {
            var seen = new HashSet<int>();

            foreach (var part in parts)
            {
                var welds = part.GetWelds();
                if (welds == null)
                    continue;

                while (welds.MoveNext())
                {
                    var weld = welds.Current as BaseWeld;
                    if (weld == null || !seen.Add(weld.Identifier.ID))
                        continue;

                    snapshot.Welds.Add(new WeldSnapshot
                    {
                        SizeAbove = weld.SizeAbove,
                        SizeBelow = weld.SizeBelow,
                        TypeAbove = weld.TypeAbove.ToString(),
                        TypeBelow = weld.TypeBelow.ToString(),
                        AroundWeld = weld.AroundWeld,
                        ShopWeld = weld.ShopWeld,
                        MainPartKey = GetWeldObjectKey(weld.MainObject, partKeys),
                        SecondaryPartKey = GetWeldObjectKey(weld.SecondaryObject, partKeys)
                    });
                }
            }
        }

        private static string GetWeldObjectKey(ModelObject modelObject, IDictionary<int, string> partKeys)
        {
            if (modelObject == null)
                return string.Empty;

            string key;
            if (partKeys.TryGetValue(modelObject.Identifier.ID, out key))
                return key;

            return modelObject.GetType().Name;
        }

        private static string BuildGeometryKey(Part part, Matrix toMain)
        {
            if (part == null)
                return string.Empty;

            try
            {
                var solid = part.GetSolid();
                if (solid == null)
                    return string.Empty;

                var edges = new List<string>();
                var edgeEnumerator = solid.GetEdgeEnumerator();

                while (edgeEnumerator.MoveNext())
                {
                    var edge = edgeEnumerator.Current as Tekla.Structures.Solid.Edge;
                    if (edge == null)
                        continue;

                    var first = PointKey(toMain.Transform(edge.StartPoint));
                    var second = PointKey(toMain.Transform(edge.EndPoint));

                    if (string.CompareOrdinal(first, second) > 0)
                    {
                        var temp = first;
                        first = second;
                        second = temp;
                    }

                    edges.Add(first + ">" + second);
                }

                edges.Sort(StringComparer.Ordinal);
                return Hash(string.Join(";", edges));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string PointKey(Point point)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.0},{1:0.0},{2:0.0}",
                Round(point.X),
                Round(point.Y),
                Round(point.Z));
        }

        private static double Round(double value)
        {
            return Math.Round(value / GeometryRounding, MidpointRounding.AwayFromZero) * GeometryRounding;
        }

        private static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(bytes.Length * 2);

                foreach (var b in bytes)
                    builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));

                return builder.ToString();
            }
        }

        private static string GetPartComparisonKey(PartSnapshot part)
        {
            return string.Join("|",
                part.Profile ?? string.Empty,
                part.Material ?? string.Empty,
                part.Finish ?? string.Empty,
                part.Length.ToString("0.0", CultureInfo.InvariantCulture),
                part.GeometryKey ?? string.Empty);
        }

        private static double GetDoubleReportProperty(ModelObject modelObject, string propertyName)
        {
            double value = 0.0;
            if (modelObject != null)
                modelObject.GetReportProperty(propertyName, ref value);
            return value;
        }

        private static string GetStringReportProperty(ModelObject modelObject, string propertyName)
        {
            var value = string.Empty;
            if (modelObject != null)
                modelObject.GetReportProperty(propertyName, ref value);
            return value ?? string.Empty;
        }

        private static string SafePartMark(Part part)
        {
            try
            {
                return part.GetPartMark() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
