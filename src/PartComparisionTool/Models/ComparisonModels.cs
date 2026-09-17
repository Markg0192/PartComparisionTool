using System.Collections.Generic;
using Tekla.Structures.Model;

namespace PartComparisionTool.Models
{
    public enum MatchQuality
    {
        Exact,
        VeryClose,
        Close,
        Possible
    }

    public sealed class ComparisonResult
    {
        public MatchQuality Quality { get; set; }
        public int Score { get; set; }
        public int Phase { get; set; }
        public string AssemblyMark { get; set; }
        public string MainPartMark { get; set; }
        public string Profile { get; set; }
        public double Length { get; set; }
        public double Weight { get; set; }
        public double WeightDifference { get; set; }
        public int FittingCount { get; set; }
        public string Differences { get; set; }

        internal Part MainPart { get; set; }
    }

    public sealed class SearchProgress
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public string Message { get; set; }
    }

    internal sealed class AssemblySnapshot
    {
        public Part MainPart { get; set; }
        public int Phase { get; set; }
        public string AssemblyMark { get; set; }
        public string MainPartMark { get; set; }
        public string MainProfile { get; set; }
        public string MainMaterial { get; set; }
        public string MainFinish { get; set; }
        public double MainLength { get; set; }
        public double Weight { get; set; }
        public string MainGeometryKey { get; set; }
        public List<PartSnapshot> SecondaryParts { get; } = new List<PartSnapshot>();
        public List<BoltSnapshot> Bolts { get; } = new List<BoltSnapshot>();
        public List<WeldSnapshot> Welds { get; } = new List<WeldSnapshot>();
    }

    internal sealed class PartSnapshot
    {
        public string Profile { get; set; }
        public string Material { get; set; }
        public string Finish { get; set; }
        public double Length { get; set; }
        public double Weight { get; set; }
        public string GeometryKey { get; set; }

        public string SimpleKey
        {
            get
            {
                return string.Join("|",
                    Profile ?? string.Empty,
                    Material ?? string.Empty,
                    Finish ?? string.Empty,
                    Length.ToString("0.0"));
            }
        }
    }

    internal sealed class BoltSnapshot
    {
        public double BoltSize { get; set; }
        public double HoleTolerance { get; set; }
        public double SlotX { get; set; }
        public double SlotY { get; set; }
        public string Standard { get; set; }
        public string HoleType { get; set; }
        public bool HasBolt { get; set; }
        public int Count { get; set; }
        public string PositionKey { get; set; }

        public string Signature
        {
            get
            {
                return string.Join("|",
                    BoltSize.ToString("0.0"),
                    HoleTolerance.ToString("0.0"),
                    SlotX.ToString("0.0"),
                    SlotY.ToString("0.0"),
                    Standard ?? string.Empty,
                    HoleType ?? string.Empty,
                    HasBolt ? "BOLT" : "HOLE",
                    Count.ToString(),
                    PositionKey ?? string.Empty);
            }
        }
    }

    internal sealed class WeldSnapshot
    {
        public double SizeAbove { get; set; }
        public double SizeBelow { get; set; }
        public string TypeAbove { get; set; }
        public string TypeBelow { get; set; }
        public bool AroundWeld { get; set; }
        public bool ShopWeld { get; set; }
        public string MainPartKey { get; set; }
        public string SecondaryPartKey { get; set; }

        public string Signature
        {
            get
            {
                return string.Join("|",
                    SizeAbove.ToString("0.0"),
                    SizeBelow.ToString("0.0"),
                    TypeAbove ?? string.Empty,
                    TypeBelow ?? string.Empty,
                    AroundWeld ? "AROUND" : "EDGE",
                    ShopWeld ? "SHOP" : "SITE",
                    MainPartKey ?? string.Empty,
                    SecondaryPartKey ?? string.Empty);
            }
        }
    }
}
